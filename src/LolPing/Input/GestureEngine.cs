using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using LolPing.Core;

namespace LolPing.Input;

public static class WheelGeometry
{
    /// <summary>Bán kính vòng trong (vùng huỷ / ô giữa) so với bán kính ngoài.</summary>
    public const double InnerRatio = 0.34;

    /// <summary>0..7 = hướng (0 = Bắc, theo chiều kim đồng hồ), 8 = giữa, -1 = không chọn.</summary>
    public static int SlotAt(double dx, double dy, double radius, bool allowCenter)
    {
        double d = Math.Sqrt(dx * dx + dy * dy);
        if (d < radius * InnerRatio) return allowCenter ? Defaults.Center : -1;
        double angle = Math.Atan2(dx, -dy) * 180 / Math.PI;
        if (angle < 0) angle += 360;
        return (int)Math.Round(angle / 45.0) % 8;
    }
}

public interface IGestureSink
{
    double WheelRadiusPx(int x, int y);
    void ShowWheel(WheelKind kind, int x, int y);
    void SelectWheel(int index);
    void HideWheel();
    void Commit(WheelKind kind, int slot, int x, int y);
}

/// <summary>
/// Hook chuột cấp thấp. Giữ phím kích hoạt (hoặc phím biểu cảm) + nhấn chuột trái:
///  • kéo quá ngưỡng → hiện vòng chọn, thả để chọn hướng;
///  • không kéo → đặt ô giữa (nếu bật) hoặc chuyển cú nhấp cho ứng dụng bên dưới;
///  • nhấn chuột phải khi đang kéo → huỷ.
/// Hook chạy trên luồng giao diện; mọi việc nặng được đẩy qua Dispatcher để hook trả về nhanh.
/// </summary>
public sealed class GestureEngine : IDisposable
{
    private readonly Native.HookProc _proc; // giữ tham chiếu để GC không thu hồi delegate
    private readonly IntPtr _hook;
    private readonly IGestureSink _sink;
    private readonly Dispatcher _ui;
    private Gesture? _g;
    private bool _swallowRightUp;

    private sealed class Gesture
    {
        public WheelKind Kind;
        public int X, Y;
        public bool Shown;
        public int Sel = -1;
        public double RadiusPx;
        public long StartedAt;
    }

    public GestureEngine(IGestureSink sink)
    {
        _sink = sink;
        _ui = Dispatcher.CurrentDispatcher;
        _proc = Proc;
        _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private IntPtr Proc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
                if (info.dwExtraInfo != InputSim.Marker && Handle((int)wParam, info.pt))
                    return (IntPtr)1;
            }
            catch (Exception ex)
            {
                _g = null;
                Log.Error("Mouse hook", ex);
            }
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool Handle(int msg, Native.POINT pt)
    {
        var s = SettingsStore.Current;

        switch (msg)
        {
            case Native.WM_LBUTTONDOWN:
            {
                // Phòng khi mất sự kiện nhả chuột (UAC, khoá màn hình…): huỷ cử chỉ cũ thay vì kẹt chuột.
                if (_g != null) Cancel();

                if (!s.Enabled) return false;
                var kind = HeldKind(s);
                if (kind == null) return false;
                if (Fullscreen.ShouldPause(s)) return false;

                _g = new Gesture
                {
                    Kind = kind.Value,
                    X = pt.X,
                    Y = pt.Y,
                    RadiusPx = _sink.WheelRadiusPx(pt.X, pt.Y),
                    StartedAt = Stopwatch.GetTimestamp()
                };
                InputSim.SendDummyKey();
                return true;
            }

            case Native.WM_MOUSEMOVE:
            {
                var g = _g;
                if (g == null) return false;
                if (Stopwatch.GetElapsedTime(g.StartedAt) > TimeSpan.FromSeconds(20)) { Cancel(); return false; }

                double dx = pt.X - g.X, dy = pt.Y - g.Y;
                if (!g.Shown && Math.Sqrt(dx * dx + dy * dy) >= Math.Max(2, s.DragThreshold))
                {
                    g.Shown = true;
                    var (k, x, y) = (g.Kind, g.X, g.Y);
                    Post(() => _sink.ShowWheel(k, x, y));
                }
                if (g.Shown)
                {
                    int sel = WheelGeometry.SlotAt(dx, dy, g.RadiusPx, allowCenter: false);
                    if (sel != g.Sel)
                    {
                        g.Sel = sel;
                        Post(() => _sink.SelectWheel(sel));
                    }
                }
                return false; // con trỏ vẫn di chuyển bình thường
            }

            case Native.WM_LBUTTONUP:
            {
                var g = _g;
                if (g == null) return false;
                _g = null;

                if (g.Shown)
                {
                    Post(() =>
                    {
                        _sink.HideWheel();
                        if (g.Sel >= 0) _sink.Commit(g.Kind, g.Sel, g.X, g.Y);
                    });
                }
                else
                {
                    bool clickPlaces = g.Kind == WheelKind.Signal ? s.ClickToPing : s.ClickToEmote;
                    var center = s.Wheel(g.Kind)[Defaults.Center];
                    if (clickPlaces && (!string.IsNullOrEmpty(center.ItemId) || center.HasAction))
                        Post(() => _sink.Commit(g.Kind, Defaults.Center, g.X, g.Y));
                    else
                        Post(InputSim.Click); // trả lại cú nhấp cho ứng dụng bên dưới
                }
                return true;
            }

            case Native.WM_RBUTTONDOWN:
                if (_g == null) return false;
                Cancel();
                _swallowRightUp = true;
                return true;

            case Native.WM_RBUTTONUP:
                if (!_swallowRightUp) return false;
                _swallowRightUp = false;
                return true;
        }
        return false;
    }

    private static WheelKind? HeldKind(AppSettings s)
    {
        bool trigger = InputSim.IsDown(s.TriggerKey);
        bool emote = InputSim.IsDown(s.EmoteKey);
        if (trigger && !emote) return WheelKind.Signal;
        if (emote && !trigger) return WheelKind.Emote;
        return null; // giữ cả hai (ví dụ Ctrl+Alt) → để nguyên cho ứng dụng
    }

    private void Cancel()
    {
        var g = _g;
        _g = null;
        if (g is { Shown: true }) Post(_sink.HideWheel);
    }

    private void Post(Action a) => _ui.BeginInvoke(DispatcherPriority.Send, a);

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
    }
}
