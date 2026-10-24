using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LolPing.Core;

namespace LolPing.UI;

public enum IconChoiceKind { Item, File, Default }

public sealed record IconChoice(IconChoiceKind Kind, string? Value);

/// <summary>
/// Hộp thoại chọn hình đại diện cho một âm trong kho: biểu cảm, tín hiệu, ảnh riêng của bạn,
/// tải ảnh từ máy, hoặc quay về thẻ màu mặc định.
/// </summary>
public static class IconPicker
{
    public static IconChoice? Ask(Window owner, string soundName, string? currentIconId)
    {
        IconChoice? result = null;
        Window? w = null;

        Brush Res(string key) => (Brush)Application.Current.Resources[key];

        var sections = new StackPanel();
        void Section(string titleKey, IEnumerable<ItemDef> items)
        {
            var list = items.ToList();
            if (list.Count == 0) return;
            sections.Children.Add(new TextBlock
            {
                Text = Loc.I[titleKey],
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Res("TextBrush"),
                Margin = new Thickness(2, 12, 0, 6)
            });
            var wrap = new WrapPanel();
            foreach (var item in list)
            {
                bool current = item.Id == currentIconId;
                var img = new Image { Source = item.Image, Width = 48, Height = 48, Margin = new Thickness(0, 10, 0, 4) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                var tile = new Border
                {
                    Width = 104,
                    Height = 96,
                    Margin = new Thickness(3),
                    CornerRadius = new CornerRadius(4),
                    Background = Res("TileBrush"),
                    BorderBrush = current ? Res("AccentBrush") : Res("CardBorderBrush"),
                    BorderThickness = new Thickness(current ? 2 : 1),
                    Cursor = Cursors.Hand,
                    ToolTip = item.DisplayName,
                    Child = new StackPanel
                    {
                        Children =
                        {
                            img,
                            new TextBlock
                            {
                                Text = item.DisplayName,
                                FontSize = 12,
                                Foreground = Res("SubtextBrush"),
                                TextAlignment = TextAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis,
                                Margin = new Thickness(4, 0, 4, 0)
                            }
                        }
                    }
                };
                tile.MouseEnter += (_, _) => tile.Background = Res("TileHoverBrush");
                tile.MouseLeave += (_, _) => tile.Background = Res("TileBrush");
                tile.MouseLeftButtonUp += (_, _) =>
                {
                    result = new IconChoice(IconChoiceKind.Item, item.Id);
                    w!.DialogResult = true;
                };
                wrap.Children.Add(tile);
            }
            sections.Children.Add(wrap);
        }

        var emotes = Catalog.Of(WheelKind.Emote).ToList();
        Section("PickEmotes", emotes.Where(i => !i.IsCustom));
        Section("PickImages", emotes.Where(i => i.IsCustom && !i.IsClip));
        Section("PickSignals", Catalog.Of(WheelKind.Signal));

        var fromFile = new Button { Content = Loc.I["PickFromFile"], Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0) };
        var useDefault = new Button { Content = Loc.I["PickDefault"], Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Loc.I["Cancel"], Padding = new Thickness(16, 8, 16, 8), IsCancel = true };

        fromFile.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.I["PickFromFile"],
                Filter = Loc.I["ImageFilter"] + "|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp"
            };
            if (dlg.ShowDialog(w!) != true) return;
            result = new IconChoice(IconChoiceKind.File, dlg.FileName);
            w!.DialogResult = true;
        };
        useDefault.Click += (_, _) =>
        {
            result = new IconChoice(IconChoiceKind.Default, null);
            w!.DialogResult = true;
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { fromFile, useDefault, cancel }
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var header = new TextBlock
        {
            Text = Loc.I.F("PickIconDesc", soundName),
            Foreground = Res("SubtextBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };
        DockPanel.SetDock(header, Dock.Top);

        var root = new DockPanel { Margin = new Thickness(24, 18, 24, 20) };
        root.Children.Add(header);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = sections, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        w = new Window
        {
            Title = Loc.I["PickIcon"],
            Owner = owner,
            Width = 640,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = Res("WindowBrush"),
            FlowDirection = Loc.I.Flow,
            Content = root
        };

        TitleBar.Attach(w, Loc.I["PickIcon"]);
        return w.ShowDialog() == true ? result : null;
    }
}
