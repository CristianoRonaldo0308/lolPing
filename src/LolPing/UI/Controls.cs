using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LolPing.Core;

namespace LolPing.UI;

/// <summary>Một hàng trong trang Cài đặt. Giao diện nằm trong App.xaml.</summary>
public class SettingRow : ContentControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingRow));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingRow));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingRow));

    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string? Header { get => (string?)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string? Description { get => (string?)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
}

/// <summary>Hộp thoại nhập một dòng chữ (đổi tên âm thanh…).</summary>
public static class TextPrompt
{
    public static string? Ask(Window owner, string title, string initial)
    {
        var box = new TextBox { Text = initial, FontSize = 15, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 18), MaxLength = 60 };
        var ok = new Button { Content = Loc.I["Ok"], MinWidth = 96, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Loc.I["Cancel"], MinWidth = 96, IsCancel = true };
        var w = new Window
        {
            Title = title,
            Owner = owner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current.Resources["WindowBrush"],
            FlowDirection = Loc.I.Flow,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Width = 380,
                Children =
                {
                    box,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } }
                }
            }
        };
        ok.Click += (_, _) => w.DialogResult = true;
        TitleBar.Attach(w, title, resizable: false);
        w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return w.ShowDialog() == true ? box.Text.Trim() : null;
    }
}

/// <summary>Ô hiển thị và ghi lại tổ hợp phím (nhấn bút chì rồi bấm tổ hợp, Esc để huỷ).</summary>
public sealed class HotkeyBox : Border
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(Hotkey), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((HotkeyBox)d).Render()));

    private static readonly Brush ChipBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1F, 0x5F, 0xC8)));

    private readonly WrapPanel _chips = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private bool _capturing;

    public Hotkey? Hotkey
    {
        get => (Hotkey?)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    /// <summary>Bắt buộc có Ctrl/Alt/Win (dùng cho phím tắt toàn cục).</summary>
    public bool RequireModifier { get; set; } = true;

    public HotkeyBox()
    {
        Focusable = true;
        FocusVisualStyle = null;
        MinWidth = 200;
        MinHeight = 44;
        Padding = new Thickness(10, 6, 6, 6);
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        BorderBrush = (Brush)Application.Current.Resources["CardBorderBrush"];
        Background = (Brush)Application.Current.Resources["TileBrush"];
        Cursor = Cursors.Hand;

        var edit = new Button
        {
            Content = new TextBlock { Text = "", FontFamily = (FontFamily)Application.Current.Resources["IconFont"], FontSize = 16 },
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(10, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false
        };
        edit.Click += (_, _) => BeginCapture();
        DockPanel.SetDock(edit, Dock.Right);

        var dock = new DockPanel { LastChildFill = true };
        dock.Children.Add(edit);
        dock.Children.Add(_chips);
        Child = dock;

        MouseLeftButtonUp += (_, _) => BeginCapture();
        PreviewKeyDown += OnPreviewKeyDown;
        LostKeyboardFocus += (_, _) => EndCapture();
        Loaded += (_, _) => Loc.I.PropertyChanged += OnLoc;
        Unloaded += (_, _) => Loc.I.PropertyChanged -= OnLoc;
        Render();
    }

    private void OnLoc(object? s, System.ComponentModel.PropertyChangedEventArgs e) => Render();

    public void BeginCapture()
    {
        _capturing = true;
        if (App.Me.Hotkeys != null) App.Me.Hotkeys.Suspended = true;
        Keyboard.Focus(this);
        BorderBrush = (Brush)Application.Current.Resources["AccentBrush"];
        Render();
    }

    private void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        if (App.Me.Hotkeys != null) App.Me.Hotkeys.Suspended = false;
        BorderBrush = (Brush)Application.Current.Resources["CardBorderBrush"];
        Render();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { EndCapture(); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed or Key.None)
            return;

        var mods = Keyboard.Modifiers;
        var hk = new Hotkey
        {
            Ctrl = mods.HasFlag(ModifierKeys.Control),
            Alt = mods.HasFlag(ModifierKeys.Alt),
            Shift = mods.HasFlag(ModifierKeys.Shift),
            Win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin),
            Vk = KeyInterop.VirtualKeyFromKey(key)
        };
        if (RequireModifier && !(hk.Ctrl || hk.Alt || hk.Win))
        {
            _chips.Children.Clear();
            _chips.Children.Add(Hint(Loc.I["NeedModifier"]));
            return;
        }

        Hotkey = hk;
        EndCapture();
    }

    private void Render()
    {
        _chips.Children.Clear();
        if (_capturing)
        {
            _chips.Children.Add(Hint(Loc.I["PressKeys"]));
            return;
        }
        if (Hotkey is not { IsEmpty: false } hk)
        {
            _chips.Children.Add(Hint(Loc.I["NoHotkey"]));
            return;
        }
        foreach (var part in hk.Parts())
        {
            _chips.Children.Add(new Border
            {
                Background = ChipBrush,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 4, 10, 5),
                Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock { Text = part, FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = Brushes.White }
            });
        }
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 14,
        Foreground = (Brush)Application.Current.Resources["SubtextBrush"],
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    /// <summary>Hộp thoại nhỏ để gán phím tắt cho một ô trên vòng chọn.</summary>
    public static Hotkey? Ask(Window owner, Hotkey? initial)
    {
        var box = new HotkeyBox { RequireModifier = false, Hotkey = initial?.Clone(), Margin = new Thickness(0, 12, 0, 18) };
        var ok = new Button { Content = Loc.I["Ok"], MinWidth = 96, IsDefault = false, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Loc.I["Cancel"], MinWidth = 96, IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } };

        var w = new Window
        {
            Title = Loc.I["AssignHotkey"],
            Owner = owner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current.Resources["WindowBrush"],
            FlowDirection = Loc.I.Flow,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Width = 380,
                Children =
                {
                    new TextBlock { Text = Loc.I["AssignHotkeyDesc"], TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SubtextBrush"] },
                    box,
                    buttons
                }
            }
        };
        ok.Click += (_, _) => w.DialogResult = true;
        TitleBar.Attach(w, Loc.I["AssignHotkey"], resizable: false);
        w.Loaded += (_, _) => box.BeginCapture();
        return w.ShowDialog() == true && box.Hotkey is { IsEmpty: false } result ? result : null;
    }
}
