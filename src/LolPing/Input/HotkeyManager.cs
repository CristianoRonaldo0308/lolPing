using System;
using System.Collections.Generic;
using System.Windows.Interop;
using LolPing.Core;

namespace LolPing.Input;

/// <summary>Phím tắt toàn cục qua RegisterHotKey (hoạt động cả khi tín hiệu đang tắt).</summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, (Hotkey Key, Action Callback)> _map = new();
    private bool _suspended;

    public HotkeyManager()
    {
        _source = new HwndSource(new HwndSourceParameters("lolPingHotkeys") { Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    /// <summary>Tạm gỡ đăng ký khi người dùng đang nhập phím tắt mới, để tổ hợp cũ không bị nuốt.</summary>
    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value) return;
            _suspended = value;
            foreach (var (id, entry) in _map)
            {
                if (value) Native.UnregisterHotKey(_source.Handle, id);
                else RegisterNative(id, entry.Key);
            }
        }
    }

    /// <returns>false nếu tổ hợp đã bị ứng dụng khác chiếm.</returns>
    public bool Register(int id, Hotkey? key, Action callback)
    {
        if (_map.ContainsKey(id))
        {
            Native.UnregisterHotKey(_source.Handle, id);
            _map.Remove(id);
        }
        if (key == null || key.IsEmpty) return true;

        _map[id] = (key, callback);
        return _suspended || RegisterNative(id, key);
    }

    private bool RegisterNative(int id, Hotkey k)
    {
        uint mods = Native.MOD_NOREPEAT;
        if (k.Ctrl) mods |= Native.MOD_CONTROL;
        if (k.Alt) mods |= Native.MOD_ALT;
        if (k.Shift) mods |= Native.MOD_SHIFT;
        if (k.Win) mods |= Native.MOD_WIN;
        return Native.RegisterHotKey(_source.Handle, id, mods, (uint)k.Vk);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _map.TryGetValue(wParam.ToInt32(), out var entry))
        {
            handled = true;
            entry.Callback();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _map.Keys) Native.UnregisterHotKey(_source.Handle, id);
        _map.Clear();
        _source.Dispose();
    }
}
