using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using LolPing.Audio;
using LolPing.Core;

namespace LolPing.Net;

public sealed class Peer
{
    public string Id { get; init; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#FFFFFF";
    public DateTime LastSeen { get; set; }
    public bool ViaLan { get; set; }
    public bool ViaRelay { get; set; }
}

/// <summary>
/// Phòng chơi chung (tối đa 8 người): gửi/nhận tín hiệu, biểu cảm, âm thanh riêng qua LAN và/hoặc Internet.
/// Cùng một tin nhắn có thể đến bằng cả hai đường; trùng lặp được lọc theo Id.
/// Mọi sự kiện công khai được gọi trên luồng giao diện.
/// </summary>
public sealed class RoomService : IDisposable
{
    public const int MaxMembers = 8;
    private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(16);

    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _heartbeat;
    private readonly ConcurrentDictionary<string, byte> _seen = new();
    private readonly Queue<string> _seenOrder = new();
    private readonly Dictionary<string, Peer> _peers = new();
    private readonly HashSet<string> _sentAssets = new();
    private readonly Dictionary<string, Queue<DateTime>> _rate = new();
    private readonly HashSet<string> _requested = new();

    private RoomCrypto? _crypto;
    private LanTransport? _lan;
    private RelayTransport? _relay;

    public string PeerId { get; } = Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 6).ToLowerInvariant();

    public string? Code => _crypto?.Code;
    public bool InRoom => _crypto != null;
    public int LanCount => _lan?.Count ?? 0;
    public string? LanError => _lan?.Error;
    public RelayState RelayState => _relay?.State ?? RelayState.Off;
    public string? RelayError => _relay?.Error;
    public IReadOnlyList<Peer> Peers => _peers.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Trạng thái phòng / thành viên / kết nối thay đổi.</summary>
    public event Action? Changed;

    /// <summary>Một người khác vừa ping (đã kiểm tra, đã lọc trùng).</summary>
    public event Action<NetMessage, Peer?>? PingReceived;

    private static AppSettings S => SettingsStore.Current;

    public RoomService()
    {
        _ui = Dispatcher.CurrentDispatcher;
        _heartbeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _heartbeat.Tick += (_, _) => Heartbeat();
        AssetCache.Init();
    }

    public static string MyName =>
        string.IsNullOrWhiteSpace(S.PlayerName) ? Environment.UserName : S.PlayerName.Trim();

    // ───────────── Vào / ra phòng ─────────────

    public string Create()
    {
        var code = RoomCrypto.NewCode();
        Enter(code);
        return code;
    }

    /// <summary>Nhận "PING-XXXXX-XXXXX" hoặc lời mời "PING-XXXXX-XXXXX @ server".</summary>
    public bool Join(string input, out string? error)
    {
        error = null;
        var parts = input.Split('@', 2);
        var code = RoomCrypto.Normalize(parts[0]);
        if (code == null)
        {
            error = Loc.I["ErrRoomCode"];
            return false;
        }
        if (parts.Length == 2 && RelayTransport.IsValidUrl(parts[1]))
        {
            S.ServerUrl = parts[1].Trim();
            S.InternetEnabled = true;
        }
        Enter(code);
        return true;
    }

    private void Enter(string code)
    {
        if (InRoom) Leave();
        _crypto = RoomCrypto.For(code);
        S.RoomCode = code;
        StartTransports();
        _heartbeat.Start();
        Broadcast(new NetMessage { T = NetMessage.Hello, Name = MyName, Color = S.LabelColor });
        Log.Write($"Joined room (id {_crypto.RoomId[..8]}…)");
        RaiseChanged();
    }

    public void Leave()
    {
        if (!InRoom) return;
        Broadcast(new NetMessage { T = NetMessage.Bye });
        StopTransports();
        _heartbeat.Stop();
        _crypto = null;
        _peers.Clear();
        _sentAssets.Clear();
        _requested.Clear();
        S.RoomCode = "";
        RaiseChanged();
    }

    /// <summary>Gọi lại khi đổi server / bật tắt LAN, Internet.</summary>
    public void RestartTransports()
    {
        if (!InRoom) return;
        StopTransports();
        StartTransports();
        RaiseChanged();
    }

    private void StartTransports()
    {
        var crypto = _crypto!;
        if (S.LanEnabled)
        {
            _lan = new LanTransport(crypto.RoomId, PeerId, data => OnData(data, lan: true), RaiseChangedAsync);
            _lan.Start();
        }
        if (S.InternetEnabled && RelayTransport.IsValidUrl(S.ServerUrl))
        {
            _relay = new RelayTransport(S.ServerUrl, crypto.RoomId, data => OnData(data, lan: false), RaiseChangedAsync);
            _relay.Start();
        }
    }

    private void StopTransports()
    {
        _lan?.Dispose();
        _relay?.Dispose();
        _lan = null;
        _relay = null;
    }

    private void Heartbeat()
    {
        Broadcast(new NetMessage { T = NetMessage.Hello, Name = MyName, Color = S.LabelColor });
        var now = DateTime.UtcNow;
        var gone = _peers.Values.Where(p => now - p.LastSeen > PeerTimeout).Select(p => p.Id).ToList();
        foreach (var id in gone) _peers.Remove(id);
        if (gone.Count > 0) RaiseChanged();
    }

    // ───────────── Gửi ─────────────

    /// <summary>Gửi ping của mình cho cả phòng. x, y đã chuẩn hoá theo màn hình (0..1).</summary>
    public void SendPing(ItemDef item, WheelKind kind, int screen, double x, double y)
    {
        if (!InRoom) return;
        var msg = new NetMessage { T = NetMessage.PingT, Kind = (int)kind, Screen = screen, X = x, Y = y, Name = MyName, Color = S.LabelColor };
        var files = new List<string>();

        if (item.IsClip && ClipStore.Find(item.Id) is { } clip)
        {
            msg.Label = clip.Name;
            msg.Vol = clip.Volume;
            msg.Sound = Track(Path.Combine(AppPaths.Sounds, clip.SoundFile), files);
            if (clip.ImageFile != null)
                msg.Image = Track(Path.Combine(AppPaths.Images, clip.ImageFile), files);
            else if (CustomImagePath(clip.IconId) is { } iconFile)
                msg.Image = Track(iconFile, files);
            else if (clip.IconId != null && Catalog.Get(clip.IconId) is { IsCustom: false })
                msg.Icon = clip.IconId;
        }
        else if (item.IsCustom)
        {
            msg.Label = item.DisplayName;
            if (CustomImagePath(item.Id) is { } file) msg.Image = Track(file, files);
        }
        else
        {
            msg.Item = item.Id;
        }

        // Lần đầu dùng trong phiên: gửi kèm file trước để người nhận có ngay.
        foreach (var hash in files)
            if (_sentAssets.Add(hash) && AssetCache.MakeAsset(hash) is { } asset)
                Broadcast(asset);

        Broadcast(msg);
    }

    private static string? Track(string path, List<string> files)
    {
        var hash = AssetCache.HashOf(path);
        if (hash != null) files.Add(hash);
        return hash;
    }

    private static string? CustomImagePath(string? id) =>
        S.CustomImages.FirstOrDefault(c => c.Id == id) is { } ci ? Path.Combine(AppPaths.Images, ci.FileName) : null;

    private void Broadcast(NetMessage msg)
    {
        var crypto = _crypto;
        if (crypto == null) return;
        msg.Id = NetMessage.NewId();
        msg.From = PeerId;
        MarkSeen(msg.Id);
        var sealedBytes = crypto.Seal(msg.ToBytes());
        _lan?.Send(sealedBytes);
        _relay?.Send(sealedBytes);
    }

    // ───────────── Nhận ─────────────

    /// <summary>Chạy trên luồng mạng: giải mã, lọc trùng rồi chuyển sang luồng giao diện.</summary>
    private void OnData(byte[] data, bool lan)
    {
        var crypto = _crypto;
        if (crypto == null) return;
        var plain = crypto.Open(data);
        if (plain == null) return;
        var msg = NetMessage.Parse(plain);
        if (msg == null || msg.From == PeerId || string.IsNullOrEmpty(msg.Id) || !MarkSeen(msg.Id)) return;
        _ui.InvokeAsync(() => Handle(msg, lan));
    }

    private bool MarkSeen(string id)
    {
        if (!_seen.TryAdd(id, 0)) return false;
        lock (_seenOrder)
        {
            _seenOrder.Enqueue(id);
            while (_seenOrder.Count > 4000) _seen.TryRemove(_seenOrder.Dequeue(), out _);
        }
        return true;
    }

    private void Handle(NetMessage msg, bool lan)
    {
        if (!InRoom) return;
        var peer = Touch(msg, lan);

        switch (msg.T)
        {
            case NetMessage.Bye:
                if (_peers.Remove(msg.From)) RaiseChanged();
                break;

            case NetMessage.PingT:
                if (!Allow(msg.From)) return;
                var missing = AssetCache.Missing(msg.Sound, msg.Image).ToList();
                foreach (var hash in missing)
                    if (_requested.Add(hash)) Broadcast(new NetMessage { T = NetMessage.Need, Hash = hash });
                PingReceived?.Invoke(msg, peer);
                break;

            case NetMessage.Need:
                if (msg.Hash != null && AssetCache.MakeAsset(msg.Hash) is { } asset) Broadcast(asset);
                break;

            case NetMessage.Asset:
                if (msg.Hash != null && msg.Ext != null && msg.Data != null && AssetCache.PathOf(msg.Hash) == null)
                {
                    if (AssetCache.Store(msg.Hash, msg.Ext, msg.Data) != null) _requested.Remove(msg.Hash);
                }
                break;
        }
    }

    private Peer? Touch(NetMessage msg, bool lan)
    {
        if (msg.T == NetMessage.Bye) return null;
        bool added = false;
        if (!_peers.TryGetValue(msg.From, out var peer))
        {
            if (_peers.Count >= MaxMembers - 1) return null;
            peer = new Peer { Id = msg.From };
            _peers[msg.From] = peer;
            added = true;
        }
        bool changed = added;
        if (msg.Name != null && msg.Name != peer.Name) { peer.Name = msg.Name.Length > 24 ? msg.Name[..24] : msg.Name; changed = true; }
        if (msg.Color != null && msg.Color != peer.Color) { peer.Color = msg.Color; changed = true; }
        if (lan && !peer.ViaLan) { peer.ViaLan = true; changed = true; }
        if (!lan && !peer.ViaRelay) { peer.ViaRelay = true; changed = true; }
        peer.LastSeen = DateTime.UtcNow;
        if (changed) RaiseChanged();
        return peer;
    }

    /// <summary>Chống spam: tối đa 8 ping mỗi 2 giây cho mỗi người.</summary>
    private bool Allow(string from)
    {
        if (!_rate.TryGetValue(from, out var q)) _rate[from] = q = new Queue<DateTime>();
        var now = DateTime.UtcNow;
        while (q.Count > 0 && now - q.Peek() > TimeSpan.FromSeconds(2)) q.Dequeue();
        if (q.Count >= 8) return false;
        q.Enqueue(now);
        return true;
    }

    private void RaiseChanged() => Changed?.Invoke();
    private void RaiseChangedAsync() => _ui.InvokeAsync(RaiseChanged);

    public void Dispose()
    {
        if (InRoom) Broadcast(new NetMessage { T = NetMessage.Bye });
        _heartbeat.Stop();
        StopTransports();
    }
}
