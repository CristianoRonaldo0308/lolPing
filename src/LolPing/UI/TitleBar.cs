using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using LolPing.Core;

namespace LolPing.UI;

/// <summary>
/// Thanh tiêu đề tự vẽ (biểu tượng, tên, nút thu nhỏ / phóng to / đóng).
/// Giao diện Fluent tối kéo nội dung lên vùng tiêu đề nên nền của app che mất nút hệ thống;
/// thay vào đó cửa sổ dùng WindowChrome và các nút này, vẫn kéo thả, nhấp đúp, Win+mũi tên và snap như thường.
/// </summary>
public static class TitleBar
{
    private const double Height = 40;

    public static void Attach(Window window, string title, bool resizable = true)
    {
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = Height,
            ResizeBorderThickness = resizable ? new Thickness(6) : new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });

        var content = window.Content as UIElement;
        window.Content = null;

        var bar = new DockPanel { Height = Height, Background = Brushes.Transparent, LastChildFill = true };
        DockPanel.SetDock(bar, Dock.Top);

        var close = MakeButton("", isClose: true);
        close.Click += (_, _) => window.Close();
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(close);

        Button? max = null;
        if (resizable)
        {
            max = MakeButton("");
            max.Click += (_, _) => window.WindowState =
                window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            DockPanel.SetDock(max, Dock.Right);
            bar.Children.Add(max);

            var min = MakeButton("");
            min.Click += (_, _) => window.WindowState = WindowState.Minimized;
            DockPanel.SetDock(min, Dock.Right);
            bar.Children.Add(min);
        }

        var caption = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0) };
        caption.Children.Add(new Image { Source = Icons.Logo, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center });
        caption.Children.Add(new TextBlock
        {
            Text = title,
            Margin = new Thickness(12, 0, 0, 0),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextBrush"]
        });
        bar.Children.Add(caption);

        var root = new DockPanel();
        root.Children.Add(bar);
        if (content != null) root.Children.Add(content);
        window.Content = root;

        // Khi phóng to, Windows đẩy mép cửa sổ ra ngoài màn hình vài điểm ảnh: bù lại để không mất nút.
        void Sync()
        {
            root.Margin = window.WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            if (max != null && max.Content is TextBlock glyph)
                glyph.Text = window.WindowState == WindowState.Maximized ? "" : "";
        }
        window.StateChanged += (_, _) => Sync();
        Sync();
    }

    private static Button MakeButton(string glyph, bool isClose = false)
    {
        var hover = isClose ? new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)) : new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        var normal = Brushes.Transparent;

        var text = new TextBlock
        {
            Text = glyph,
            FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var b = new Button
        {
            Content = text,
            Width = 46,
            Height = Height,
            Focusable = false,
            Cursor = Cursors.Arrow,
            Template = FlatTemplate()
        };
        b.Background = normal;
        b.MouseEnter += (_, _) => { b.Background = hover; if (isClose) text.Foreground = Brushes.White; };
        b.MouseLeave += (_, _) => { b.Background = normal; text.Foreground = (Brush)Application.Current.Resources["TextBrush"]; };
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        return b;
    }

    /// <summary>Nút phẳng: chỉ nền + nội dung, không viền/hiệu ứng của theme.</summary>
    private static ControlTemplate FlatTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding(nameof(Control.Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }
}
