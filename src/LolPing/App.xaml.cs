using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LolPing.Audio;
using LolPing.Core;
using LolPing.Input;
using LolPing.Net;
using LolPing.Overlay;
using LolPing.UI;

namespace LolPing;

public partial class App : Application
{
    private const string MutexName = @"Local\lolPing.SingleInstance";
    private const string ShowEventName = @"Local\lolPing.Show";
    private const int ToggleHotkeyId = 1;
    private const int StopSoundsHotkeyId = 2;

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private GestureEngine? _gestures;
    private TrayIcon? _tray;
    private SettingsWindow? _window;
    private DispatcherTimer? _displayTimer;
    private bool _exiting;
    private bool _primary;

    public static App Me => (App)Current;
    public static AppSettings S => SettingsStore.Current;

    public SoundEngine Sound { get; private set; } = null!;
    public OverlayManager Overlay { get; private set; } = null!;
    public HotkeyManager Hotkeys { get; private set; } = null!;
    public RoomService Room { get; private set; } = null!;

    private readonly ConcurrentDictionary<string, float[]> _remoteSounds = new();
    private readonly ConcurrentDictionary<string, ImageSource> _remoteImages = new();
    public bool IsExiting => _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Chỉ chạy một phiên bản: mở lần hai sẽ đưa cửa sổ của phiên bản đang chạy lên.
        _mutex = new Mutex(true, MutexName, out bool created);
        if (!created)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }
        _primary = true;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        new Thread(() =>
        {
            while (_showEvent.WaitOne() && !_exiting)
                Dispatcher.InvokeAsync(ShowSettings);
        }) { IsBackground = true, Name = "lolPing show listener" }.Start();

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("Unhandled", ex.Exception);
            ex.Handled = true;
        };

        SettingsStore.Load();
        Loc.I.Load(S.Language);
        if (string.IsNullOrEmpty(S.Language)) S.Language = Loc.I.Lang;
        Catalog.ReloadCustom();

        Sound = new SoundEngine();
        SoundPack.Load(S.SoundFolder);
        IconPack.Load(S.IconFolder);
        ClipStore.Preload();
        Overlay = new OverlayManager(Fire);
        Overlay.Rebuild();
        Hotkeys = new HotkeyManager();

        try
        {
            _gestures = new GestureEngine(Overlay);
        }
        catch (Exception ex)
        {
            Log.Error("Install mouse hook", ex);
            MessageBox.Show(Loc.I["ErrHook"], "lolPing", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        S.Enabled = S.StartEnabled;
        if (S.StartWithWindows) AutoStart.Apply(true); // cập nhật đường dẫn nếu exe đã bị di chuyển
        RegisterToggleHotkey(showError: false);
        RegisterStopHotkey(showError: false);

        Room = new RoomService();
        Room.PingReceived += OnRemotePing;
        if (S.AutoRejoin && !string.IsNullOrWhiteSpace(S.RoomCode)) Room.Join(S.RoomCode, out _);

        _tray = new TrayIcon();
        S.PropertyChanged += OnSettingChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

        if (!e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
            ShowSettings();
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.Language):
                Loc.I.Load(S.Language);
                break;
            case nameof(AppSettings.ToggleHotkey):
                RegisterToggleHotkey(showError: true);
                break;
            case nameof(AppSettings.StopSoundsHotkey):
                RegisterStopHotkey(showError: true);
                break;
            case nameof(AppSettings.IconFolder):
                IconPack.Load(S.IconFolder);
                break;
            case nameof(AppSettings.SoundFolder):
                SoundPack.Load(S.SoundFolder);
                break;
            case nameof(AppSettings.ServerUrl):
            case nameof(AppSettings.InternetEnabled):
            case nameof(AppSettings.LanEnabled):
                Room.RestartTransports();
                break;
            case nameof(AppSettings.StartWithWindows):
                AutoStart.Apply(S.StartWithWindows);
                break;
            case nameof(AppSettings.Enabled):
                if (!S.Enabled) Overlay.HideWheel();
                break;
        }
    }

    private void RegisterToggleHotkey(bool showError)
    {
        bool ok = Hotkeys.Register(ToggleHotkeyId, S.ToggleHotkey, ToggleEnabled);
        if (!ok)
        {
            Log.Write($"Hotkey {S.ToggleHotkey} is in use");
            if (showError)
                MessageBox.Show(Loc.I.F("ErrHotkeyInUse", S.ToggleHotkey), "lolPing", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RegisterStopHotkey(bool showError)
    {
        bool ok = Hotkeys.Register(StopSoundsHotkeyId, S.StopSoundsHotkey, () => Sound.StopAll());
        if (!ok && showError)
            MessageBox.Show(Loc.I.F("ErrHotkeyInUse", S.StopSoundsHotkey), "lolPing", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        // Sự kiện này đến từ luồng khác và thường bắn nhiều lần liên tiếp: gom lại rồi dựng lại lớp phủ.
        Dispatcher.InvokeAsync(() =>
        {
            _displayTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _displayTimer.Tick -= DisplayTick;
            _displayTimer.Tick += DisplayTick;
            _displayTimer.Stop();
            _displayTimer.Start();
        });
    }

    private void DisplayTick(object? sender, EventArgs e)
    {
        _displayTimer!.Stop();
        Overlay.Rebuild();
    }

    /// <summary>Người dùng chọn một ô trên vòng chọn (hoặc nhấp đặt ô giữa).</summary>
    public void Fire(WheelKind kind, int slotIndex, int x, int y)
    {
        var slot = S.Wheel(kind)[slotIndex];
        if (Catalog.Get(slot.ItemId) is { } item)
        {
            Overlay.Ping(item, x, y);
            PlaySound(item);

            // Ô có gán ứng dụng/phím tắt chỉ hiện cho mình, không gửi vào phòng.
            if (!slot.HasAction && Room.InRoom)
            {
                var (screen, nx, ny) = Overlay.Normalize(x, y);
                Room.SendPing(item, kind, screen, nx, ny);
            }
        }
        if (slot.HasAction) ActionRunner.Run(slot);
    }

    public void PlaySound(ItemDef item)
    {
        if (!S.SoundEnabled) return;
        if (item.IsClip)
        {
            // Âm trong kho: luôn phát (đây là mục đích của nó), có âm lượng riêng và tuỳ chọn thay âm cũ.
            if (ClipStore.Get(item.Id) is { } data)
            {
                int clipVolume = ClipStore.Find(item.Id)?.Volume ?? 100;
                Sound.PlayClip(data, S.Volume / 100.0 * clipVolume / 100.0, replace: !S.ClipsOverlap);
            }
            return;
        }
        if (item.Kind == WheelKind.Emote && !S.EmoteSounds) return;
        // Ưu tiên file trong thư mục âm thanh riêng đặt theo id của mục (vd. e_laugh.ogg).
        Sound.Play(SoundPack.TryGet(item.Id) != null ? item.Id : item.Sound, S.Volume / 100.0);
    }

    // ───────────── Ping của người khác trong phòng ─────────────

    private void OnRemotePing(NetMessage msg, Peer? peer)
    {
        if (!S.Enabled) return;
        var kind = msg.Kind == (int)WheelKind.Signal ? WheelKind.Signal : WheelKind.Emote;
        var item = msg.Item != null && Catalog.Get(msg.Item) is { IsCustom: false } builtin ? builtin : RemoteItem(msg, kind);

        var (x, y) = Overlay.Denormalize(msg.Screen, msg.X, msg.Y);
        Overlay.Ping(item, x, y, peer?.Name ?? msg.Name ?? "?", peer?.Color ?? msg.Color);

        if (!S.SoundEnabled || !S.RoomSounds) return;
        if (msg.Sound != null) PlayRemoteSound(msg.Sound, msg.Vol);
        else if (msg.Item != null) PlaySound(item);
        else Sound.Play("pop", S.Volume / 100.0);
    }

    /// <summary>Mục tạm cho âm thanh/ảnh riêng của người khác (đã nhận vào bộ nhớ đệm).</summary>
    private ItemDef RemoteItem(NetMessage msg, WheelKind kind)
    {
        var label = msg.Label ?? "";
        var imageHash = msg.Image;
        var iconId = msg.Icon;
        return new ItemDef
        {
            Id = "remote:" + (imageHash ?? msg.Sound ?? iconId ?? "item"),
            Kind = kind,
            CustomName = label,
            Accent = Colors.White,
            Sound = "pop",
            Factory = () =>
            {
                if (imageHash != null && AssetCache.PathOf(imageHash) is { } path)
                {
                    try { return _remoteImages.GetOrAdd(imageHash, _ => ImageDecode.Load(path)); }
                    catch (Exception ex) { Log.Error("Remote image", ex); }
                }
                return Catalog.Get(iconId) is { IsCustom: false } icon ? icon.Image : Icons.ClipCard(label);
            }
        };
    }

    private void PlayRemoteSound(string hash, int clipVolume)
    {
        double volume = S.Volume / 100.0 * Math.Clamp(clipVolume, 10, 200) / 100.0;
        if (_remoteSounds.TryGetValue(hash, out var cached))
        {
            Sound.PlayClip(cached, volume, replace: !S.ClipsOverlap);
            return;
        }
        var path = AssetCache.PathOf(hash);
        if (path == null)
        {
            Sound.Play("pop", S.Volume / 100.0); // file đang được tải về; lần sau sẽ đúng âm
            return;
        }
        bool replace = !S.ClipsOverlap;
        Task.Run(() =>
        {
            try
            {
                var data = SoundPack.Decode(path, ClipStore.MaxSeconds);
                _remoteSounds[hash] = data;
                Sound.PlayClip(data, volume, replace);
            }
            catch (Exception ex)
            {
                Log.Error("Remote sound", ex);
            }
        });
    }

    /// <summary>Xem trước tại vị trí con trỏ (dùng trong Cài đặt).</summary>
    public void Preview(ItemDef item)
    {
        Native.GetCursorPos(out var pt);
        Overlay.Ping(item, pt.X, pt.Y);
        PlaySound(item);
    }

    public void ToggleEnabled() => S.Enabled = !S.Enabled;

    public void ShowSettings()
    {
        if (_exiting) return;
        _window ??= new SettingsWindow();
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Quit()
    {
        _exiting = true;
        _window?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        try
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            _gestures?.Dispose();
            Room?.Dispose();
            Hotkeys?.Dispose();
            _tray?.Dispose();
            Sound?.Dispose();
            Overlay?.CloseAll();
            if (_primary) SettingsStore.SaveNow();
            _showEvent?.Set();
        }
        catch (Exception ex)
        {
            Log.Error("Exit", ex);
        }
        base.OnExit(e);
    }
}
