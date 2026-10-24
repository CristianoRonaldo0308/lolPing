using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using LolPing.Core;

namespace LolPing.Input;

/// <summary>Trạng thái phím và giả lập nhập liệu. Mọi sự kiện giả lập mang <see cref="Marker"/> để hook bỏ qua.</summary>
internal static class InputSim
{
    public static readonly IntPtr Marker = new(0x4C4F4C50); // "LOLP"

    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private static readonly int[] HeldModifiers = { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, VK_LWIN, VK_RWIN };

    public static bool IsDown(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    public static bool IsDown(ModKey key) => key switch
    {
        ModKey.Alt => IsDown(VK_MENU),
        ModKey.Ctrl => IsDown(VK_CONTROL),
        ModKey.Shift => IsDown(VK_SHIFT),
        ModKey.Win => IsDown(VK_LWIN) || IsDown(VK_RWIN),
        _ => false
    };

    /// <summary>
    /// Nhấn-nhả một mã phím không dùng (0xE8) để Windows không coi lần nhả Win/Alt là "nhấn riêng":
    /// tránh mở Start menu hay kích hoạt thanh menu của ứng dụng sau khi đặt tín hiệu.
    /// </summary>
    public static void SendDummyKey()
    {
        Send(Key(0xE8, false), Key(0xE8, true));
    }

    public static void Click()
    {
        Send(Mouse(Native.MOUSEEVENTF_LEFTDOWN), Mouse(Native.MOUSEEVENTF_LEFTUP));
    }

    public static void SendHotkey(Hotkey hk)
    {
        var list = new List<Native.INPUT>();

        // Nhả các phím bổ trợ người dùng đang giữ (ví dụ phím kích hoạt), để không lẫn vào tổ hợp.
        foreach (var vk in HeldModifiers)
            if (IsDown(vk)) list.Add(Key(vk, true));

        var mods = new List<int>();
        if (hk.Ctrl) mods.Add(0xA2);
        if (hk.Alt) mods.Add(0xA4);
        if (hk.Shift) mods.Add(0xA0);
        if (hk.Win) mods.Add(VK_LWIN);

        foreach (var m in mods) list.Add(Key(m, false));
        list.Add(Key(hk.Vk, false));
        list.Add(Key(hk.Vk, true));
        for (int i = mods.Count - 1; i >= 0; i--) list.Add(Key(mods[i], true));

        Send(list.ToArray());
    }

    private static bool IsExtended(int vk) => vk is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 // PgUp PgDn End Home mũi tên
        or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90 or 0xA3 or 0xA5 or 0x2C;

    private static Native.INPUT Key(int vk, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        U = new Native.InputUnion
        {
            ki = new Native.KEYBDINPUT
            {
                wVk = (ushort)vk,
                dwFlags = (up ? Native.KEYEVENTF_KEYUP : 0) | (IsExtended(vk) ? Native.KEYEVENTF_EXTENDEDKEY : 0),
                dwExtraInfo = Marker
            }
        }
    };

    private static Native.INPUT Mouse(uint flags) => new()
    {
        type = Native.INPUT_MOUSE,
        U = new Native.InputUnion { mi = new Native.MOUSEINPUT { dwFlags = flags, dwExtraInfo = Marker } }
    };

    private static void Send(params Native.INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.INPUT>());
    }
}

/// <summary>
/// Quyết định có tạm dừng cử chỉ khi một ứng dụng đang chạy toàn màn hình hay không.
/// Chỉ tạm dừng với game: trình duyệt (YouTube, Netflix…), trình phát video, trình chiếu
/// và các ứng dụng người dùng cho phép vẫn ping được.
/// </summary>
internal static class Fullscreen
{
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

    /// <summary>Tên tiến trình (không có .exe) luôn được ping khi toàn màn hình.</summary>
    private static readonly HashSet<string> BuiltInAllowed = new(StringComparer.OrdinalIgnoreCase)
    {
        // Trình duyệt (Cốc Cốc và Yandex đều dùng browser.exe)
        "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "browser", "arc", "zen",
        "librewolf", "waterfox", "thorium", "chromium", "iexplore", "msedgewebview2",
        // Trình phát video / nhạc
        "vlc", "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64", "potplayer", "potplayer64", "potplayermini", "potplayermini64",
        "mpv", "wmplayer", "Microsoft.Media.Player", "Video.UI", "ApplicationFrameHost", "kmplayer", "gom", "Spotify",
        // Trình chiếu, đọc tài liệu, họp, ứng dụng thường ngày
        "POWERPNT", "Acrobat", "AcroRd32", "SumatraPDF", "explorer", "Teams", "ms-teams", "Zoom", "Discord",
        "Photos", "PhotosApp", "obs64", "WINWORD", "EXCEL", "Code", "devenv"
    };

    private static readonly ConcurrentDictionary<uint, string?> NameCache = new();
    private static string? _userListRaw;
    private static HashSet<string> _userList = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ứng dụng toàn màn hình gần nhất bị tạm dừng (để người dùng bấm "Cho phép").</summary>
    public static string? LastPaused { get; private set; }

    public static bool ShouldPause(AppSettings s)
    {
        if (!s.PauseInFullscreen) return false;

        // Game độc quyền màn hình (DirectX exclusive): lớp phủ không vẽ lên được nên luôn nhường.
        if (Native.SHQueryUserNotificationState(out var state) == 0 && state == QUNS_RUNNING_D3D_FULL_SCREEN)
            return Paused(ForegroundProcessName());

        var h = ForegroundFullscreenWindow();
        if (h == IntPtr.Zero) return false;

        var name = ProcessName(h);
        if (name != null && (BuiltInAllowed.Contains(name) || UserAllowed(s).Contains(name))) return false;
        return Paused(name);
    }

    public static bool IsAllowed(string name) =>
        BuiltInAllowed.Contains(name) || UserAllowed(SettingsStore.Current).Contains(name);

    public static IEnumerable<string> ParseList(string? raw) =>
        (raw ?? "").Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Select(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? x[..^4] : x)
            .Where(x => x.Length > 0);

    private static bool Paused(string? name)
    {
        if (name != null && name != LastPaused)
        {
            LastPaused = name;
            Log.Write($"Paused in full-screen app: {name}");
        }
        return true;
    }

    private static HashSet<string> UserAllowed(AppSettings s)
    {
        if (!ReferenceEquals(s.FullscreenAllowList, _userListRaw))
        {
            _userListRaw = s.FullscreenAllowList;
            _userList = new HashSet<string>(ParseList(_userListRaw), StringComparer.OrdinalIgnoreCase);
        }
        return _userList;
    }

    private static string? ForegroundProcessName()
    {
        var h = Native.GetForegroundWindow();
        return h == IntPtr.Zero ? null : ProcessName(h);
    }

    /// <summary>Tên tiến trình của cửa sổ, lưu tạm theo PID để hook chuột luôn trả về nhanh.</summary>
    private static string? ProcessName(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;
        if (NameCache.Count > 256) NameCache.Clear();
        return NameCache.GetOrAdd(pid, id =>
        {
            try { return Process.GetProcessById((int)id).ProcessName; }
            catch { return null; }
        });
    }

    /// <summary>Cửa sổ đang được chọn nếu nó phủ kín màn hình của nó (trò chơi, video toàn màn hình…).</summary>
    private static IntPtr ForegroundFullscreenWindow()
    {
        var h = Native.GetForegroundWindow();
        if (h == IntPtr.Zero || h == Native.GetShellWindow() || h == Native.GetDesktopWindow()) return IntPtr.Zero;

        Native.GetWindowThreadProcessId(h, out var pid);
        if (pid == Environment.ProcessId) return IntPtr.Zero;

        var cls = new StringBuilder(64);
        Native.GetClassName(h, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return IntPtr.Zero;

        if (!Native.GetWindowRect(h, out var r)) return IntPtr.Zero;
        var mon = Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST);
        var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(mon, ref mi)) return IntPtr.Zero;

        var m = mi.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom ? h : IntPtr.Zero;
    }
}

internal static class ActionRunner
{
    public static void Run(WheelSlot slot)
    {
        try
        {
            switch (slot.ActionKind)
            {
                case SlotActionKind.LaunchApp when !string.IsNullOrWhiteSpace(slot.AppPath):
                    var psi = new ProcessStartInfo(slot.AppPath!) { UseShellExecute = true };
                    if (!string.IsNullOrWhiteSpace(slot.AppArgs)) psi.Arguments = slot.AppArgs;
                    var dir = Path.GetDirectoryName(slot.AppPath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) psi.WorkingDirectory = dir;
                    Process.Start(psi);
                    break;

                case SlotActionKind.Hotkey when slot.Keys is { IsEmpty: false }:
                    InputSim.SendHotkey(slot.Keys);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Run slot action", ex);
        }
    }
}
