using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using LolPing.Core;
using LolPing.Input;

namespace LolPing.Overlay;

/// <summary>Quản lý các cửa sổ phủ (một cho mỗi màn hình), vòng chọn đang mở và hoạt ảnh tín hiệu.</summary>
public sealed class OverlayManager : IGestureSink
{
    public const double BaseWheelRadius = 150; // DIP ở tỉ lệ 100%
    public const double BaseIconSize = 64;

    private readonly List<OverlayWindow> _windows = new();
    private readonly Action<WheelKind, int, int, int> _commit;
    private OverlayWindow? _wheelWindow;
    private WheelView? _wheel;

    public OverlayManager(Action<WheelKind, int, int, int> commit)
    {
        _commit = commit;
    }

    public void Rebuild()
    {
        HideWheel();
        foreach (var w in _windows) w.Close();
        _windows.Clear();

        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var b = screen.Bounds; // điểm ảnh vật lý (tiến trình nhận biết DPI theo màn hình)
            var w = new OverlayWindow(new Rect(b.X, b.Y, b.Width, b.Height));
            w.Show();
            _windows.Add(w);
        }
    }

    public void CloseAll()
    {
        foreach (var w in _windows) w.Close();
        _windows.Clear();
    }

    private OverlayWindow? At(int x, int y) =>
        _windows.FirstOrDefault(w => w.Contains(x, y)) ?? _windows.FirstOrDefault();

    public double WheelRadiusPx(int x, int y)
    {
        double scale = At(x, y)?.Scale ?? 1;
        return BaseWheelRadius * SettingsStore.Current.WheelScale / 100.0 * scale;
    }

    public void ShowWheel(WheelKind kind, int x, int y)
    {
        HideWheel();
        var win = At(x, y);
        if (win == null) return;

        var wheel = new WheelView();
        wheel.Build(BaseWheelRadius * SettingsStore.Current.WheelScale / 100.0, SettingsStore.Current.Wheel(kind), editor: false);
        win.BringToTop();
        win.AddWheel(wheel, win.ToLocal(x, y));
        _wheel = wheel;
        _wheelWindow = win;
    }

    public void SelectWheel(int index)
    {
        if (_wheel != null) _wheel.Selected = index;
    }

    public void HideWheel()
    {
        if (_wheel != null && _wheelWindow != null) _wheelWindow.RemoveWheel(_wheel);
        _wheel = null;
        _wheelWindow = null;
    }

    public void Commit(WheelKind kind, int slot, int x, int y) => _commit(kind, slot, x, y);

    /// <summary>Đổi toạ độ điểm ảnh thành (màn hình, x, y) chuẩn hoá 0..1 — để gửi cho người khác.</summary>
    public (int Screen, double X, double Y) Normalize(int x, int y)
    {
        var win = At(x, y);
        if (win == null) return (0, 0.5, 0.5);
        var b = win.BoundsPx;
        return (_windows.IndexOf(win), (x - b.X) / Math.Max(1, b.Width), (y - b.Y) / Math.Max(1, b.Height));
    }

    /// <summary>Ngược lại: màn hình cùng số thứ tự (không có thì màn hình chính) → điểm ảnh.</summary>
    public (int X, int Y) Denormalize(int screen, double x, double y)
    {
        OverlayWindow? win = screen >= 0 && screen < _windows.Count ? _windows[screen] : null;
        if (win == null)
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            int primary = Array.FindIndex(screens, s => s.Primary);
            win = primary >= 0 && primary < _windows.Count ? _windows[primary] : _windows.FirstOrDefault();
        }
        if (win == null) return (0, 0);
        var b = win.BoundsPx;
        x = Math.Clamp(x, 0, 1);
        y = Math.Clamp(y, 0, 1);
        return ((int)(b.X + x * (b.Width - 1)), (int)(b.Y + y * (b.Height - 1)));
    }

    public void Ping(ItemDef item, int x, int y) => Ping(item, x, y, null, null);

    /// <summary>label/color khác null: ping của người khác (hiện tên và màu của họ).</summary>
    public void Ping(ItemDef item, int x, int y, string? otherName, string? otherColor)
    {
        var win = At(x, y);
        if (win == null) return;
        var s = SettingsStore.Current;

        string? label = otherName != null
            ? (s.ShowName ? otherName : null)
            : s.ShowName && !string.IsNullOrWhiteSpace(s.PlayerName) ? s.PlayerName.Trim() : null;
        Brush labelBrush;
        try { labelBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(otherColor ?? s.LabelColor)); }
        catch { labelBrush = Brushes.White; }

        win.BringToTop();
        win.AddPing(item, win.ToLocal(x, y), BaseIconSize * s.PingScale / 100.0, s.PingDuration, label, labelBrush);
    }
}
