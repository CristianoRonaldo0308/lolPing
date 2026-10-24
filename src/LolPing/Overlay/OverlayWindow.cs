using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using LolPing.Core;
using LolPing.Input;

namespace LolPing.Overlay;

/// <summary>
/// Cửa sổ trong suốt, xuyên chuột, luôn trên cùng, phủ đúng một màn hình.
/// Mỗi màn hình một cửa sổ để vẽ đúng tỉ lệ DPI riêng của màn hình đó.
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private IntPtr _hwnd;

    public Rect BoundsPx { get; }

    public OverlayWindow(Rect boundsPx)
    {
        BoundsPx = boundsPx;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        Title = "lolPing overlay";
        Width = Math.Max(1, boundsPx.Width);
        Height = Math.Max(1, boundsPx.Height);
        Content = _canvas;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE,
                ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_LAYERED);
            Place();
        };
        Loaded += (_, _) => Place();
        DpiChanged += (_, _) => Dispatcher.InvokeAsync(Place);
    }

    public double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    public bool Contains(int x, int y) =>
        x >= BoundsPx.Left && x < BoundsPx.Right && y >= BoundsPx.Top && y < BoundsPx.Bottom;

    public Point ToLocal(int x, int y) => new((x - BoundsPx.X) / Scale, (y - BoundsPx.Y) / Scale);

    /// <summary>Đặt lại vị trí theo điểm ảnh vật lý và đưa lên trên cùng.</summary>
    public void Place()
    {
        if (_hwnd == IntPtr.Zero) return;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST,
            (int)BoundsPx.X, (int)BoundsPx.Y, (int)BoundsPx.Width, (int)BoundsPx.Height, Native.SWP_NOACTIVATE);
    }

    public void BringToTop()
    {
        if (_hwnd != IntPtr.Zero)
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public void AddWheel(WheelView wheel, Point center)
    {
        Canvas.SetLeft(wheel, center.X - wheel.Width / 2);
        Canvas.SetTop(wheel, center.Y - wheel.Height / 2);
        wheel.RenderTransformOrigin = new Point(0.5, 0.5);
        var st = new ScaleTransform(0.88, 0.88);
        wheel.RenderTransform = st;
        wheel.Opacity = 0;
        _canvas.Children.Add(wheel);

        var dur = TimeSpan.FromMilliseconds(110);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        wheel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur));
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
    }

    public void RemoveWheel(WheelView wheel)
    {
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(70));
        a.Completed += (_, _) => _canvas.Children.Remove(wheel);
        wheel.BeginAnimation(OpacityProperty, a);
    }

    /// <summary>Hoạt ảnh tín hiệu/biểu cảm tại điểm (toạ độ nội bộ của cửa sổ).</summary>
    public void AddPing(ItemDef item, Point p, double iconSize, double seconds, string? label, Brush labelBrush)
    {
        // Giới hạn số hoạt ảnh cùng lúc để không làm nặng máy khi bấm liên tục.
        const string PingTag = "ping";
        var pings = _canvas.Children.OfType<FrameworkElement>().Where(e => Equals(e.Tag, PingTag)).ToList();
        for (int i = 0; i < pings.Count - 40; i++) _canvas.Children.Remove(pings[i]);

        bool emote = item.Kind == WheelKind.Emote;
        var root = new Canvas { IsHitTestVisible = false, Tag = PingTag };
        Canvas.SetLeft(root, p.X);
        Canvas.SetTop(root, p.Y);
        var accent = item.Accent;

        if (!emote)
        {
            // Quầng sáng dưới chân + hai vòng lan toả.
            var glow = new Ellipse
            {
                Width = iconSize * 1.3,
                Height = iconSize * 0.42,
                Fill = new RadialGradientBrush(Color.FromArgb(0x90, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B))
            };
            Canvas.SetLeft(glow, -glow.Width / 2);
            Canvas.SetTop(glow, -glow.Height / 2);
            root.Children.Add(glow);

            for (int k = 0; k < 2; k++)
            {
                double d = iconSize * 0.9;
                var ring = new Ellipse
                {
                    Width = d,
                    Height = d * 0.42,
                    Stroke = new SolidColorBrush(accent),
                    StrokeThickness = 3,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    Opacity = 0
                };
                Canvas.SetLeft(ring, -d / 2);
                Canvas.SetTop(ring, -d * 0.21);
                var st = new ScaleTransform(0.3, 0.3);
                ring.RenderTransform = st;
                root.Children.Add(ring);

                var begin = TimeSpan.FromMilliseconds(k * 320);
                var dur = new Duration(TimeSpan.FromMilliseconds(900));
                var grow = new DoubleAnimation(0.3, 2.0, dur) { BeginTime = begin, RepeatBehavior = new RepeatBehavior(2), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, grow.Clone());
                ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0.95, 0, dur) { BeginTime = begin, RepeatBehavior = new RepeatBehavior(2) });
            }
        }

        // Biểu tượng nảy lên.
        var icon = new Image
        {
            Source = item.Image,
            Width = iconSize,
            Height = iconSize,
            RenderTransformOrigin = new Point(0.5, 1)
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        Canvas.SetLeft(icon, -iconSize / 2);
        Canvas.SetTop(icon, -iconSize * (emote ? 0.6 : 1.05));
        var pop = new ScaleTransform(0, 0);
        var lift = new TranslateTransform();
        icon.RenderTransform = new TransformGroup { Children = { pop, lift } };
        root.Children.Add(icon);

        var popAnim = new DoubleAnimationUsingKeyFrames();
        popAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.25, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        popAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)), new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
        pop.BeginAnimation(ScaleTransform.ScaleXProperty, popAnim);
        pop.BeginAnimation(ScaleTransform.ScaleYProperty, popAnim.Clone());

        if (emote)
            lift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, -iconSize * 0.45, TimeSpan.FromSeconds(seconds)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });

        // Tên người đặt tín hiệu.
        if (!string.IsNullOrWhiteSpace(label))
        {
            const double w = 260;
            var tag = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x0B, 0x0F, 0x1A)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 2, 7, 3),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock
                {
                    Text = label,
                    Foreground = labelBrush,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            var holder = new Grid { Width = w, Children = { tag } };
            Canvas.SetLeft(holder, -w / 2);
            Canvas.SetTop(holder, emote ? iconSize * 0.45 : iconSize * 0.28);
            root.Children.Add(holder);
        }

        _canvas.Children.Add(root);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(350))
        {
            BeginTime = TimeSpan.FromSeconds(Math.Max(0.4, seconds - 0.35))
        };
        fade.Completed += (_, _) => _canvas.Children.Remove(root);
        root.BeginAnimation(OpacityProperty, fade);
    }
}
