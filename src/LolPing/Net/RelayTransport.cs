using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using LolPing.Core;

namespace LolPing.Net;

public enum RelayState { Off, Connecting, Connected, Full, Error }

/// <summary>
/// Kết nối Internet qua server relay (WebSocket). Server chỉ chuyển tiếp các gói đã mã hoá
/// cho người cùng RoomId, không đọc được nội dung. Tự kết nối lại khi rớt mạng.
/// </summary>
public sealed class RelayTransport : IDisposable
{
    public const WebSocketCloseStatus RoomFull = (WebSocketCloseStatus)4001;
    private const int MaxMessage = 8 * 1024 * 1024;

    private readonly Uri _uri;
    private readonly Action<byte[]> _onMessage;
    private readonly Action _onChanged;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _ws;

    public RelayState State { get; private set; } = RelayState.Off;
    public string? Error { get; private set; }

    public RelayTransport(string serverUrl, string roomId, Action<byte[]> onMessage, Action onChanged)
    {
        _uri = BuildUri(serverUrl, roomId);
        _onMessage = onMessage;
        _onChanged = onChanged;
    }

    /// <summary>"lolping.onrender.com", "https://…", "ws://192.168.1.5:8080" → wss://…/ws?room=…</summary>
    public static Uri BuildUri(string serverUrl, string roomId)
    {
        var s = serverUrl.Trim().TrimEnd('/');
        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) s = "wss://" + s[8..];
        else if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) s = "ws://" + s[7..];
        else if (!s.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) && !s.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            s = "wss://" + s;
        if (s.EndsWith("/ws", StringComparison.OrdinalIgnoreCase)) s = s[..^3];
        return new Uri($"{s}/ws?room={roomId}&v=1");
    }

    public static bool IsValidUrl(string serverUrl)
    {
        try { return !string.IsNullOrWhiteSpace(serverUrl) && BuildUri(serverUrl, new string('0', 32)).Host.Length > 0; }
        catch { return false; }
    }

    public void Start() => _ = RunLoop();

    private void Set(RelayState state, string? error = null)
    {
        State = state;
        Error = error;
        _onChanged();
    }

    private async Task RunLoop()
    {
        int delay = 2000;
        while (!_cts.IsCancellationRequested)
        {
            var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            try
            {
                Set(RelayState.Connecting);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                {
                    // Server miễn phí có thể mất ~1 phút để "thức dậy" sau khi ngủ.
                    timeout.CancelAfter(TimeSpan.FromSeconds(75));
                    await ws.ConnectAsync(_uri, timeout.Token);
                }
                _ws = ws;
                delay = 2000;
                Set(RelayState.Connected);
                Log.Write($"Relay connected: {_uri.Host}");
                await ReceiveLoop(ws);

                if (ws.CloseStatus == RoomFull)
                {
                    Set(RelayState.Full);
                    delay = 30000;
                }
                else Set(RelayState.Connecting);
            }
            catch (Exception ex) when (!_cts.IsCancellationRequested)
            {
                Set(RelayState.Error, ex.InnerException?.Message ?? ex.Message);
                Log.Write($"Relay error: {ex.Message}");
            }
            catch
            {
                break;
            }
            finally
            {
                _ws = null;
                ws.Dispose();
            }

            try { await Task.Delay(delay, _cts.Token); } catch { break; }
            delay = Math.Min(delay * 2, 30000);
        }
    }

    private async Task ReceiveLoop(ClientWebSocket ws)
    {
        var buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();
        while (ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
        {
            var r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
            if (r.MessageType == WebSocketMessageType.Close) break;
            ms.Write(buffer, 0, r.Count);
            if (ms.Length > MaxMessage) throw new InvalidDataException("message too large");
            if (!r.EndOfMessage) continue;
            if (r.MessageType == WebSocketMessageType.Binary) _onMessage(ms.ToArray());
            ms.SetLength(0);
        }
    }

    public void Send(byte[] data)
    {
        var ws = _ws;
        if (ws == null || ws.State != WebSocketState.Open) return;
        _ = SendAsync(ws, data);
    }

    private async Task SendAsync(ClientWebSocket ws, byte[] data)
    {
        try
        {
            await _sendLock.WaitAsync(_cts.Token);
            try { await ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, _cts.Token); }
            finally { _sendLock.Release(); }
        }
        catch { /* vòng kết nối lại sẽ xử lý */ }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _ws?.Abort(); } catch { }
    }
}
