using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LolPing.Core;

namespace LolPing.Net;

/// <summary>
/// Kết nối trong cùng mạng (wifi/LAN), không cần server:
///  • UDP broadcast cổng 47650 mỗi 3 giây để tìm máy cùng phòng;
///  • mỗi cặp máy giữ một kết nối TCP (máy có PeerId nhỏ hơn chủ động nối) để gửi tin nhắn đã mã hoá.
/// </summary>
public sealed class LanTransport : IDisposable
{
    public const int DiscoveryPort = 47650;
    private const string Magic = "LPNG1";
    private const int MaxFrame = 8 * 1024 * 1024;
    private const int MaxPeers = 7;

    private readonly string _roomId;
    private readonly string _peerId;
    private readonly Action<byte[]> _onMessage;
    private readonly Action _onChanged;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, Conn> _conns = new();
    private readonly ConcurrentDictionary<string, byte> _connecting = new();

    private UdpClient? _udp;
    private TcpListener? _listener;
    private int _tcpPort;

    public LanTransport(string roomId, string peerId, Action<byte[]> onMessage, Action onChanged)
    {
        _roomId = roomId;
        _peerId = peerId;
        _onMessage = onMessage;
        _onChanged = onChanged;
    }

    public int Count => _conns.Count;
    public string? Error { get; private set; }

    public void Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, 0);
            _listener.Start();
            _tcpPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptLoop();

            _udp = new UdpClient { EnableBroadcast = true };
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            _ = DiscoveryLoop();
            _ = AnnounceLoop();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Log.Error("LAN start", ex);
        }
    }

    public void Send(byte[] data)
    {
        foreach (var (id, conn) in _conns)
            _ = SendTo(id, conn, data);
    }

    private async Task SendTo(string id, Conn conn, byte[] data)
    {
        try
        {
            await conn.SendAsync(data, _cts.Token);
        }
        catch
        {
            Drop(id, conn);
        }
    }

    // ───────────── Tìm nhau ─────────────

    private async Task AnnounceLoop()
    {
        var payload = Encoding.UTF8.GetBytes($"{Magic}|{_roomId}|{_peerId}|{_tcpPort}");
        while (!_cts.IsCancellationRequested)
        {
            foreach (var addr in BroadcastAddresses())
            {
                try { await _udp!.SendAsync(payload, new IPEndPoint(addr, DiscoveryPort), _cts.Token); }
                catch { /* mạng có thể chặn broadcast trên một card; thử card khác */ }
            }
            try { await Task.Delay(3000, _cts.Token); } catch { break; }
        }
    }

    private static IEnumerable<IPAddress> BroadcastAddresses()
    {
        var result = new HashSet<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                    var ip = ua.Address.GetAddressBytes();
                    var mask = ua.IPv4Mask.GetAddressBytes();
                    var b = new byte[4];
                    for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
                    result.Add(new IPAddress(b));
                }
            }
        }
        catch { }
        return result;
    }

    private async Task DiscoveryLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await _udp!.ReceiveAsync(_cts.Token); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { continue; }

            var parts = Encoding.UTF8.GetString(r.Buffer).Split('|');
            if (parts.Length != 4 || parts[0] != Magic || parts[1] != _roomId) continue;
            var peer = parts[2];
            if (peer == _peerId || !int.TryParse(parts[3], out var port)) continue;

            // Máy có PeerId nhỏ hơn chủ động kết nối, tránh mở hai kết nối cho một cặp.
            if (string.CompareOrdinal(_peerId, peer) < 0 && !_conns.ContainsKey(peer) && _conns.Count < MaxPeers
                && _connecting.TryAdd(peer, 0))
                _ = ConnectTo(new IPEndPoint(r.RemoteEndPoint.Address, port), peer);
        }
    }

    // ───────────── TCP ─────────────

    private async Task ConnectTo(IPEndPoint ep, string expectedPeer)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(5000);
            await client.ConnectAsync(ep, timeout.Token);
            var conn = new Conn(client);
            await conn.SendAsync(Handshake(), timeout.Token);
            var reply = await conn.ReadFrameAsync(MaxFrame, timeout.Token);
            if (ParseHandshake(reply) != expectedPeer) throw new InvalidOperationException("handshake");
            Register(expectedPeer, conn);
        }
        catch (Exception ex)
        {
            client.Dispose();
            if (!_cts.IsCancellationRequested) Log.Write($"LAN connect {ep} failed: {ex.Message}");
        }
        finally
        {
            _connecting.TryRemove(expectedPeer, out _);
        }
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(_cts.Token); }
            catch { break; }
            _ = Accept(client);
        }
    }

    private async Task Accept(TcpClient client)
    {
        try
        {
            client.NoDelay = true;
            var conn = new Conn(client);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(5000);
            var hello = await conn.ReadFrameAsync(MaxFrame, timeout.Token);
            var peer = ParseHandshake(hello);
            if (peer == null || peer == _peerId || _conns.Count >= MaxPeers) { client.Dispose(); return; }
            await conn.SendAsync(Handshake(), timeout.Token);
            Register(peer, conn);
        }
        catch
        {
            client.Dispose();
        }
    }

    private byte[] Handshake() => Encoding.UTF8.GetBytes($"{Magic}|{_roomId}|{_peerId}");

    private string? ParseHandshake(byte[] data)
    {
        var parts = Encoding.UTF8.GetString(data).Split('|');
        return parts.Length == 3 && parts[0] == Magic && parts[1] == _roomId ? parts[2] : null;
    }

    private void Register(string peer, Conn conn)
    {
        if (!_conns.TryAdd(peer, conn))
        {
            conn.Dispose();
            return;
        }
        Log.Write($"LAN connected to {peer}");
        _onChanged();
        _ = ReadLoop(peer, conn);
    }

    private async Task ReadLoop(string peer, Conn conn)
    {
        try
        {
            while (!_cts.IsCancellationRequested)
                _onMessage(await conn.ReadFrameAsync(MaxFrame, _cts.Token));
        }
        catch { }
        Drop(peer, conn);
    }

    private void Drop(string peer, Conn conn)
    {
        if (_conns.TryRemove(new KeyValuePair<string, Conn>(peer, conn)))
        {
            conn.Dispose();
            _onChanged();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _udp?.Dispose(); } catch { }
        try { _listener?.Stop(); } catch { }
        foreach (var c in _conns.Values) c.Dispose();
        _conns.Clear();
    }

    /// <summary>Một kết nối TCP: khung = 4 byte độ dài (big-endian) + dữ liệu.</summary>
    private sealed class Conn : IDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public Conn(TcpClient client)
        {
            _client = client;
            _stream = client.GetStream();
        }

        public async Task SendAsync(byte[] data, CancellationToken ct)
        {
            var header = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
            await _sendLock.WaitAsync(ct);
            try
            {
                await _stream.WriteAsync(header, ct);
                await _stream.WriteAsync(data, ct);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task<byte[]> ReadFrameAsync(int max, CancellationToken ct)
        {
            var header = new byte[4];
            await _stream.ReadExactlyAsync(header, ct);
            int len = BinaryPrimitives.ReadInt32BigEndian(header);
            if (len <= 0 || len > max) throw new InvalidOperationException("bad frame");
            var data = new byte[len];
            await _stream.ReadExactlyAsync(data, ct);
            return data;
        }

        public void Dispose()
        {
            try { _client.Dispose(); } catch { }
        }
    }
}
