using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LolPing.Core;

public enum ModKey { Alt, Win, Ctrl, Shift }

public enum WheelKind { Signal, Emote }

public enum SlotActionKind { None, LaunchApp, Hotkey }

/// <summary>Tổ hợp phím (modifier + một phím thường theo mã virtual-key).</summary>
public sealed class Hotkey
{
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }
    public int Vk { get; set; }

    [JsonIgnore] public bool IsEmpty => Vk == 0;

    public Hotkey Clone() => (Hotkey)MemberwiseClone();

    public IEnumerable<string> Parts()
    {
        if (Ctrl) yield return "Ctrl";
        if (Alt) yield return "Alt";
        if (Shift) yield return "Shift";
        if (Win) yield return "Win";
        if (Vk != 0) yield return KeyName(Vk);
    }

    public override string ToString() => string.Join(" + ", Parts());

    public static string KeyName(int vk)
    {
        var k = KeyInterop.KeyFromVirtualKey(vk);
        if (k >= Key.D0 && k <= Key.D9) return ((char)('0' + (k - Key.D0))).ToString();
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return "Num" + (k - Key.NumPad0);
        return k switch
        {
            Key.OemPlus => "=",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.Oem1 => ";",
            Key.Oem2 => "/",
            Key.Oem3 => "`",
            Key.Oem4 => "[",
            Key.Oem5 => "\\",
            Key.Oem6 => "]",
            Key.Oem7 => "'",
            Key.Return => "Enter",
            Key.Prior => "PgUp",
            Key.Next => "PgDn",
            Key.Back => "Backspace",
            Key.Capital => "CapsLock",
            Key.Snapshot => "PrtSc",
            Key.None => "?",
            _ => k.ToString()
        };
    }
}

/// <summary>Một ô trong vòng chọn: tín hiệu/biểu cảm, có thể gán thêm ứng dụng hoặc phím tắt.</summary>
public sealed class WheelSlot
{
    public string? ItemId { get; set; }
    public SlotActionKind ActionKind { get; set; }
    public string? AppPath { get; set; }
    public string? AppArgs { get; set; }
    public Hotkey? Keys { get; set; }

    [JsonIgnore]
    public bool HasAction => ActionKind switch
    {
        SlotActionKind.LaunchApp => !string.IsNullOrWhiteSpace(AppPath),
        SlotActionKind.Hotkey => Keys is { IsEmpty: false },
        _ => false
    };

    [JsonIgnore]
    public string? ActionLabel => ActionKind switch
    {
        SlotActionKind.LaunchApp when !string.IsNullOrWhiteSpace(AppPath) => Path.GetFileNameWithoutExtension(AppPath),
        SlotActionKind.Hotkey when Keys is { IsEmpty: false } => Keys.ToString(),
        _ => null
    };

    public WheelSlot() { }
    public WheelSlot(string? itemId) { ItemId = itemId; }

    public void ClearAction()
    {
        ActionKind = SlotActionKind.None;
        AppPath = null;
        AppArgs = null;
        Keys = null;
    }
}

/// <summary>Một âm trong kho âm thanh (meme, hiệu ứng…), có thể kèm ảnh.</summary>
public sealed class SoundClip
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SoundFile { get; set; } = "";   // trong %AppData%\lolPing\sounds
    public string? ImageFile { get; set; }        // trong %AppData%\lolPing\images
    public string? IconId { get; set; }           // hoặc mượn biểu tượng của một mục khác (e_laugh, danger, img:…)
    public int Volume { get; set; } = 100;        // % (10..200)
}

public sealed class CustomImage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
}

/// <summary>Toàn bộ cài đặt, lưu ra settings.json. Thuộc tính công khai do CommunityToolkit sinh ra từ các trường.</summary>
public sealed partial class AppSettings : ObservableObject
{
    // Kích hoạt
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private ModKey _triggerKey = ModKey.Alt;
    [ObservableProperty] private int _dragThreshold = 8;
    [ObservableProperty] private bool _clickToPing = true;
    [ObservableProperty] private bool _pauseInFullscreen = true;
    [ObservableProperty] private string _fullscreenAllowList = ""; // tên exe vẫn ping khi toàn màn hình, cách nhau bởi dấu phẩy

    // Bật / tắt
    [ObservableProperty] private Hotkey _toggleHotkey = new() { Ctrl = true, Alt = true, Vk = 0x50 /* P */ };
    [ObservableProperty] private bool _startEnabled = true;
    [ObservableProperty] private bool _startWithWindows;

    // Tín hiệu & âm thanh
    [ObservableProperty] private bool _soundEnabled = true;
    [ObservableProperty] private int _volume = 60;          // 0..100
    [ObservableProperty] private int _pingScale = 100;      // %
    [ObservableProperty] private double _pingDuration = 2.5; // giây
    [ObservableProperty] private int _wheelScale = 100;     // %
    [ObservableProperty] private string _soundFolder = "";  // thư mục âm thanh riêng (.ogg/.wav/.mp3)
    [ObservableProperty] private string _iconFolder = "";   // thư mục biểu tượng riêng (.png/.webp/...)

    // Biểu cảm
    [ObservableProperty] private ModKey _emoteKey = ModKey.Ctrl;
    [ObservableProperty] private bool _clickToEmote;
    [ObservableProperty] private bool _emoteSounds = true;
    [ObservableProperty] private bool _clipsOverlap;          // false: âm kho mới dừng âm kho đang phát
    [ObservableProperty] private Hotkey _stopSoundsHotkey = new(); // Vk = 0: chưa đặt

    // Hồ sơ (dùng cục bộ, sau này hiển thị trong phòng)
    [ObservableProperty] private string _playerName = "";
    [ObservableProperty] private bool _showName = true;
    [ObservableProperty] private string _labelColor = "#4FC3F7";

    // Phòng
    [ObservableProperty] private string _roomCode = "";          // phòng đang ở, để tự vào lại khi mở app
    [ObservableProperty] private bool _autoRejoin = true;
    [ObservableProperty] private bool _lanEnabled = true;
    [ObservableProperty] private bool _internetEnabled = true;
    [ObservableProperty] private string _serverUrl = "";
    [ObservableProperty] private bool _roomSounds = true;          // phát âm thanh khi người khác ping

    // Ứng dụng
    [ObservableProperty] private string _language = "";

    public List<WheelSlot> SignalWheel { get; set; } = Defaults.SignalWheel();
    public List<WheelSlot> EmoteWheel { get; set; } = Defaults.EmoteWheel();
    public List<CustomImage> CustomImages { get; set; } = new();
    public List<SoundClip> Clips { get; set; } = new();

    // Phím vòng tín hiệu và phím biểu cảm không được trùng nhau: nếu trùng thì đổi chỗ.
    partial void OnTriggerKeyChanged(ModKey oldValue, ModKey newValue)
    {
        if (EmoteKey == newValue) EmoteKey = oldValue;
    }

    partial void OnEmoteKeyChanged(ModKey oldValue, ModKey newValue)
    {
        if (TriggerKey == newValue) TriggerKey = oldValue;
    }

    public List<WheelSlot> Wheel(WheelKind kind) => kind == WheelKind.Signal ? SignalWheel : EmoteWheel;

    /// <summary>Sửa dữ liệu thiếu hoặc lỗi sau khi đọc từ đĩa.</summary>
    public void Normalize()
    {
        SignalWheel = Fix(SignalWheel, Defaults.SignalWheel());
        EmoteWheel = Fix(EmoteWheel, Defaults.EmoteWheel());
        CustomImages ??= new();
        Clips ??= new();
        StopSoundsHotkey ??= new();
        ToggleHotkey ??= new() { Ctrl = true, Alt = true, Vk = 0x50 };
        if (TriggerKey == EmoteKey) EmoteKey = TriggerKey == ModKey.Ctrl ? ModKey.Shift : ModKey.Ctrl;
        Volume = System.Math.Clamp(Volume, 0, 100);
        DragThreshold = System.Math.Clamp(DragThreshold, 2, 40);
        PingScale = System.Math.Clamp(PingScale, 50, 200);
        WheelScale = System.Math.Clamp(WheelScale, 60, 160);
        PingDuration = System.Math.Clamp(PingDuration, 1, 6);
    }

    private static List<WheelSlot> Fix(List<WheelSlot>? list, List<WheelSlot> def)
    {
        if (list == null || list.Count == 0) return def;
        while (list.Count < Defaults.SlotCount) list.Add(new WheelSlot());
        if (list.Count > Defaults.SlotCount) list.RemoveRange(Defaults.SlotCount, list.Count - Defaults.SlotCount);
        for (int i = 0; i < list.Count; i++) list[i] ??= new WheelSlot();
        return list;
    }
}

public static class Defaults
{
    /// <summary>8 hướng (0 = Bắc, theo chiều kim đồng hồ) + ô giữa (chỉ số 8).</summary>
    public const int SlotCount = 9;
    public const int Center = 8;

    public static List<WheelSlot> SignalWheel() => new()
    {
        new("danger"), new("assist"), new("omw"), new("allin"),
        new("missing"), new("cleared"), new("push"), new("needvision"),
        new("generic")
    };

    public static List<WheelSlot> EmoteWheel() => new()
    {
        new("e_smile"), new("e_love"), new("e_wow"), new("e_gg"),
        new("e_laugh"), new("e_cool"), new("e_sad"), new("e_angry"),
        new("e_laugh")
    };
}
