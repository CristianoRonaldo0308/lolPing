using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LolPing.Core;
using LolPing.Input;

namespace LolPing.Overlay;

/// <summary>Vòng chọn 8 hướng + ô giữa. Dùng chung cho lớp phủ (khi kéo) và trình chỉnh sửa trong Cài đặt.</summary>
public sealed class WheelView : Canvas
{
    private const double Pad = 8;
    private static readonly ConcurrentDictionary<string, ImageSource?> AppIcons = new();

    private static readonly Brush SectorFill = Frozen(Color.FromArgb(0xEE, 0x12, 0x17, 0x24));
    private static readonly Brush SectorStroke = Frozen(Color.FromRgb(0x3A, 0x42, 0x58));
    private static readonly Brush HoverFill = Frozen(Color.FromArgb(0xF2, 0x1D, 0x26, 0x3A));
    private static readonly Brush SelectFill = Frozen(Color.FromArgb(0xF5, 0x24, 0x36, 0x5A));
    private static readonly Brush SelectStroke = Frozen(Color.FromRgb(0x5A, 0xB4, 0xFF));
    private static readonly Brush CenterFill = Frozen(Color.FromArgb(0xF5, 0x0E, 0x13, 0x20));
    private static readonly Brush Accent = Frozen(Color.FromRgb(0x4F, 0xB0, 0xFF));

    private readonly Path[] _sectors = new Path[8];
    private readonly FrameworkElement?[] _icons = new FrameworkElement?[Defaults.SlotCount];
    private Ellipse? _center;
    private int _selected = -1, _hover = -1;

    public double Radius { get; private set; } = 150;
    public bool EditorMode { get; private set; }
    private Point C => new(Radius + Pad, Radius + Pad);

    public void Build(double radius, IReadOnlyList<WheelSlot> slots, bool editor)
    {
        Radius = radius;
        EditorMode = editor;
        Children.Clear();
        Width = Height = 2 * (radius + Pad);
        Background = editor ? Brushes.Transparent : null;

        double r0 = radius * WheelGeometry.InnerRatio;
        var c = C;

        for (int i = 0; i < 8; i++)
        {
            var p = new Path
            {
                Data = Sector(c, r0 + 4, radius, i * 45 - 22.5 + 1.2, i * 45 + 22.5 - 1.2),
                Fill = SectorFill,
                Stroke = SectorStroke,
                StrokeThickness = 1.5
            };
            _sectors[i] = p;
            Children.Add(p);
        }

        _center = new Ellipse { Width = 2 * r0, Height = 2 * r0, Fill = CenterFill, Stroke = SectorStroke, StrokeThickness = 2 };
        SetLeft(_center, c.X - r0);
        SetTop(_center, c.Y - r0);
        Children.Add(_center);

        double iconSize = radius * 0.30;
        for (int i = 0; i < 8; i++)
        {
            double a = i * 45 * Math.PI / 180, rm = (r0 + radius) / 2 + 4;
            var pos = new Point(c.X + rm * Math.Sin(a), c.Y - rm * Math.Cos(a));
            _icons[i] = AddSlotVisual(slots[i], pos, iconSize, i);
        }

        // Ô giữa: trong trình sửa hiển thị biểu tượng + chữ "NHẤP"; trên lớp phủ là vùng huỷ nên để trống.
        if (editor)
        {
            var center = slots[Defaults.Center];
            _icons[Defaults.Center] = AddSlotVisual(center, new Point(c.X, c.Y - r0 * 0.18), r0 * 0.75, Defaults.Center);
            var lbl = new TextBlock
            {
                Text = Loc.I["WheelCenter"],
                Foreground = Accent,
                FontWeight = FontWeights.Bold,
                FontSize = Math.Max(11, radius * 0.07),
                Width = 2 * r0,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };
            SetLeft(lbl, c.X - r0);
            SetTop(lbl, c.Y + r0 * 0.32);
            Children.Add(lbl);
        }
        else
        {
            var x = new TextBlock
            {
                Text = "✕",
                Foreground = SectorStroke,
                FontSize = r0 * 0.5,
                Width = 2 * r0,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };
            SetLeft(x, c.X - r0);
            SetTop(x, c.Y - r0 * 0.36);
            Children.Add(x);
        }

        ApplyStates();
    }

    private FrameworkElement? AddSlotVisual(WheelSlot slot, Point pos, double size, int index)
    {
        var item = Catalog.Get(slot.ItemId);
        FrameworkElement? icon = null;

        if (item != null)
        {
            icon = new Image
            {
                Source = item.Image,
                Width = size,
                Height = size,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1)
            };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            SetLeft(icon, pos.X - size / 2);
            SetTop(icon, pos.Y - size / 2);
            Children.Add(icon);
        }

        if (slot.HasAction)
        {
            // Huy hiệu góc phải: biểu tượng ứng dụng hoặc bàn phím.
            double b = Math.Max(20, size * 0.48);
            var badge = new Border
            {
                Width = b,
                Height = b,
                CornerRadius = new CornerRadius(b / 2),
                Background = CenterFill,
                BorderBrush = SectorStroke,
                BorderThickness = new Thickness(1.5),
                IsHitTestVisible = false,
                Child = BadgeContent(slot, b)
            };
            SetLeft(badge, pos.X + size * 0.25);
            SetTop(badge, pos.Y - size * 0.75);
            Children.Add(badge);

            if (index != Defaults.Center && slot.ActionLabel is { } text)
            {
                var label = new Border
                {
                    Background = Frozen(Color.FromArgb(0xCC, 0x0B, 0x0F, 0x1A)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1, 5, 2),
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = text,
                        Foreground = Brushes.White,
                        FontSize = Math.Max(11, Radius * 0.065),
                        FontWeight = FontWeights.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = Radius * 0.75
                    }
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                SetLeft(label, pos.X - label.DesiredSize.Width / 2);
                SetTop(label, pos.Y + size * 0.45);
                Children.Add(label);
            }
        }

        return icon;
    }

    private static UIElement BadgeContent(WheelSlot slot, double b)
    {
        if (slot.ActionKind == SlotActionKind.LaunchApp && AppIcon(slot.AppPath!) is { } img)
            return new Image { Source = img, Width = b * 0.68, Height = b * 0.68 };

        return new TextBlock
        {
            Text = slot.ActionKind == SlotActionKind.LaunchApp ? "" : "",
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = b * 0.5,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static ImageSource? AppIcon(string path) => AppIcons.GetOrAdd(path, p =>
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(p);
            if (icon == null) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
    });

    public int Selected
    {
        get => _selected;
        set { _selected = value; ApplyStates(); }
    }

    public int Hover
    {
        get => _hover;
        set { if (_hover == value) return; _hover = value; ApplyStates(); }
    }

    private void ApplyStates()
    {
        for (int i = 0; i < 8; i++)
        {
            if (_sectors[i] == null) continue;
            bool sel = i == _selected, hov = i == _hover;
            _sectors[i].Fill = sel ? SelectFill : hov ? HoverFill : SectorFill;
            _sectors[i].Stroke = sel || hov ? SelectStroke : SectorStroke;
            _sectors[i].StrokeThickness = sel ? 2.5 : 1.5;
            if (_icons[i]?.RenderTransform is ScaleTransform st && !st.IsFrozen)
                st.ScaleX = st.ScaleY = sel ? 1.2 : 1.0;
        }
        if (_center != null)
            _center.Stroke = _hover == Defaults.Center ? SelectStroke : SectorStroke;
    }

    /// <summary>Ô tại điểm (toạ độ của chính control). -1 nếu ngoài vòng.</summary>
    public int HitSlot(Point p)
    {
        double dx = p.X - C.X, dy = p.Y - C.Y;
        if (Math.Sqrt(dx * dx + dy * dy) > Radius + Pad) return -1;
        return WheelGeometry.SlotAt(dx, dy, Radius, allowCenter: true);
    }

    private static Geometry Sector(Point c, double r0, double r1, double a0, double a1)
    {
        Point P(double r, double a)
        {
            double rad = a * Math.PI / 180;
            return new Point(c.X + r * Math.Sin(rad), c.Y - r * Math.Cos(rad));
        }

        var fig = new PathFigure { StartPoint = P(r1, a0), IsClosed = true, IsFilled = true };
        fig.Segments.Add(new ArcSegment(P(r1, a1), new Size(r1, r1), 0, false, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(P(r0, a1), true));
        fig.Segments.Add(new ArcSegment(P(r0, a0), new Size(r0, r0), 0, false, SweepDirection.Counterclockwise, true));
        var g = new PathGeometry();
        g.Figures.Add(fig);
        g.Freeze();
        return g;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
