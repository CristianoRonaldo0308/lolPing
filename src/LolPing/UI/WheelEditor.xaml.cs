using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using LolPing.Audio;
using LolPing.Core;
using LolPing.Overlay;

namespace LolPing.UI;

/// <summary>
/// Trình chỉnh vòng chọn: kéo một tín hiệu/biểu cảm vào ô, hoặc nhấp chọn rồi nhấp vào ô.
/// Kéo một ô sang ô khác để đổi chỗ, kéo ra danh sách để làm trống. Chuột phải vào ô để gán ứng dụng/phím tắt.
/// </summary>
public partial class WheelEditor : UserControl
{
    private const string ItemFormat = "lolping-item";
    private const string SlotFormat = "lolping-slot";
    private const double WheelRadius = 210;

    private readonly WheelView _wheel = new();
    private string? _pending;
    private Point _downPos;
    private int _downSlot = -1;
    private bool _dragging;
    private bool _initialized;

    public WheelKind Kind { get; set; }

    private static AppSettings S => SettingsStore.Current;
    private List<WheelSlot> Slots => S.Wheel(Kind);

    public WheelEditor()
    {
        InitializeComponent();
        Loaded += (_, _) => Init();
    }

    private void Init()
    {
        if (_initialized) return;
        _initialized = true;

        WheelHost.Content = _wheel;
        _wheel.AllowDrop = true;
        _wheel.DragOver += Wheel_DragOver;
        _wheel.DragLeave += (_, _) => _wheel.Hover = -1;
        _wheel.Drop += Wheel_Drop;
        _wheel.MouseLeftButtonDown += Wheel_MouseDown;
        _wheel.MouseMove += Wheel_MouseMove;
        _wheel.MouseLeftButtonUp += Wheel_MouseUp;
        _wheel.MouseLeave += (_, _) => _wheel.Hover = -1;
        _wheel.MouseRightButtonUp += Wheel_RightClick;
        _wheel.Cursor = Cursors.Hand;

        PaletteFrame.DragOver += Palette_DragOver;
        PaletteFrame.Drop += Palette_Drop;

        CustomBar.Visibility = Kind == WheelKind.Emote ? Visibility.Visible : Visibility.Collapsed;
        FilterBar.Visibility = CustomBar.Visibility;
        Loc.I.PropertyChanged += OnLanguage;
        Refresh();
    }

    private void OnLanguage(object? sender, PropertyChangedEventArgs e) => Refresh();

    public void Refresh()
    {
        bool sig = Kind == WheelKind.Signal;
        TitleText.Text = Loc.I[sig ? "EditSignalTitle" : "EditEmoteTitle"];
        DescText.Text = Loc.I.F(sig ? "EditSignalDesc" : "EditEmoteDesc", S.TriggerKey, S.EmoteKey);
        PaletteTitle.Text = Loc.I[sig ? "AllSignals" : "AllEmotes"];
        ImageCount.Text = Loc.I.F("LibraryCount", S.Clips.Count, ClipStore.MaxClips, S.CustomImages.Count, ImageStore.MaxImages);

        _wheel.Build(WheelRadius, Slots, editor: true);
        BuildPalette();
    }

    private void Changed()
    {
        SettingsStore.ScheduleSave();
        Refresh();
    }

    // ───────────── Danh sách tất cả mục ─────────────

    private void BuildPalette()
    {
        Palette.Children.Clear();
        string query = Kind == WheelKind.Emote ? SearchBox.Text.Trim() : "";
        string filter = Kind == WheelKind.Emote && FilterCombo.SelectedItem is ComboBoxItem { Tag: string t } ? t : "all";

        foreach (var item in Catalog.Of(Kind))
        {
            bool show = filter switch
            {
                "clip" => item.IsClip,
                "image" => item.IsCustom && !item.IsClip,
                "builtin" => !item.IsCustom,
                _ => true
            };
            if (show && query.Length > 0 && item.DisplayName.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0) show = false;
            if (show) Palette.Children.Add(MakeTile(item));
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_initialized) BuildPalette();
    }

    private Border MakeTile(ItemDef item)
    {
        int slot = Slots.FindIndex(s => s.ItemId == item.Id);
        string? dir = slot < 0 ? null : Loc.I[slot == Defaults.Center ? "DirCenter" : "Dir" + slot];

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var img = new Image { Source = item.Image, Width = 46, Height = 46, Margin = new Thickness(0, 14, 0, 4) };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        grid.Children.Add(img);

        var name = new TextBlock
        {
            Text = item.DisplayName,
            FontSize = 14,
            Foreground = (Brush)FindResource("TextBrush"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 40,
            Margin = new Thickness(6, 0, 6, 10)
        };
        Grid.SetRow(name, 1);
        grid.Children.Add(name);

        if (item.IsClip)
        {
            int vol = ClipStore.Find(item.Id)?.Volume ?? 100;
            grid.Children.Add(new TextBlock
            {
                Text = "\uE767" + (vol != 100 ? $" {vol}%" : ""),
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 12,
                Foreground = (Brush)FindResource("AccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8, 6, 0, 0)
            });

            var pick = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = (Brush)FindResource("CardBrush"),
                BorderBrush = (Brush)FindResource("CardBorderBrush"),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                ToolTip = Loc.I["PickIcon"],
                Child = new TextBlock
                {
                    Text = "\uE70F",
                    FontFamily = (FontFamily)FindResource("IconFont"),
                    FontSize = 13,
                    Foreground = (Brush)FindResource("TextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            // Chặn sự kiện để nhấp nút không bị tính là nhấp/kéo thẻ.
            pick.MouseLeftButtonDown += (_, e) => e.Handled = true;
            pick.MouseLeftButtonUp += (_, e) => { e.Handled = true; PickIconFor(item.Id); };
            grid.Children.Add(pick);
        }

        if (dir != null)
        {
            var badge = new TextBlock
            {
                Text = dir,
                FontSize = 12,
                Foreground = (Brush)FindResource("SubtextBrush"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 8, 0)
            };
            grid.Children.Add(badge);
        }

        bool selected = _pending == item.Id;
        var tile = new Border
        {
            Width = 150,
            Height = 116,
            Margin = new Thickness(3),
            CornerRadius = new CornerRadius(4),
            Background = (Brush)FindResource("TileBrush"),
            BorderBrush = selected ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(selected ? 2 : 1),
            Cursor = Cursors.Hand,
            Child = grid,
            Tag = item.Id,
            ToolTip = item.DisplayName
        };

        Point down = default;
        bool pressed = false;
        tile.MouseEnter += (_, _) => tile.Background = (Brush)FindResource("TileHoverBrush");
        tile.MouseLeave += (_, _) => tile.Background = (Brush)FindResource("TileBrush");
        tile.MouseLeftButtonDown += (_, e) => { down = e.GetPosition(this); pressed = true; };
        tile.MouseMove += (_, e) =>
        {
            if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
            var d = e.GetPosition(this) - down;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            pressed = false;
            DragDrop.DoDragDrop(tile, new DataObject(ItemFormat, item.Id), DragDropEffects.Copy);
        };
        tile.MouseLeftButtonUp += (_, _) =>
        {
            if (!pressed) return;
            pressed = false;
            // Nhấp: xem trước và chọn để đặt vào ô tiếp theo được nhấp.
            _pending = _pending == item.Id ? null : item.Id;
            App.Me.Preview(item);
            BuildPalette();
        };

        if (item.IsClip)
        {
            tile.ContextMenu = ClipMenu(item);
        }
        else if (item.IsCustom)
        {
            var remove = new MenuItem { Header = Loc.I["RemoveImage"] };
            remove.Click += (_, _) => { ImageStore.Remove(item.Id); Changed(); };
            tile.ContextMenu = new ContextMenu { Items = { remove } };
        }

        return tile;
    }

    private ContextMenu ClipMenu(ItemDef item)
    {
        var clip = ClipStore.Find(item.Id);
        var menu = new ContextMenu();

        var play = new MenuItem { Header = Loc.I["PlayClip"] };
        play.Click += (_, _) => App.Me.Preview(item);
        menu.Items.Add(play);

        var rename = new MenuItem { Header = Loc.I["RenameClip"] };
        rename.Click += (_, _) =>
        {
            var owner = Window.GetWindow(this);
            if (owner != null && TextPrompt.Ask(owner, Loc.I["RenameClip"], item.DisplayName) is { Length: > 0 } name)
            {
                ClipStore.Rename(item.Id, name);
                Changed();
            }
        };
        menu.Items.Add(rename);

        var icon = new MenuItem { Header = Loc.I["PickIcon"] };
        icon.Click += (_, _) => PickIconFor(item.Id);
        menu.Items.Add(icon);

        var volume = new MenuItem { Header = Loc.I["ClipVolume"] };
        foreach (var v in new[] { 25, 50, 75, 100, 125, 150, 200 })
        {
            var mi = new MenuItem { Header = v + "%", IsCheckable = true, IsChecked = clip?.Volume == v };
            mi.Click += (_, _) => { ClipStore.SetVolume(item.Id, v); Changed(); };
            volume.Items.Add(mi);
        }
        menu.Items.Add(volume);

        menu.Items.Add(new Separator());
        var delete = new MenuItem { Header = Loc.I["DeleteClip"] };
        delete.Click += (_, _) =>
        {
            var owner = Window.GetWindow(this);
            if (owner == null) return;
            if (MessageBox.Show(owner, Loc.I.F("DeleteClipConfirm", item.DisplayName), "lolPing",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            ClipStore.Remove(item.Id);
            Changed();
        };
        menu.Items.Add(delete);
        return menu;
    }

    /// <summary>Mở hộp thoại chọn hình đại diện cho một âm trong kho.</summary>
    private void PickIconFor(string clipId)
    {
        var owner = Window.GetWindow(this);
        var clip = ClipStore.Find(clipId);
        if (owner == null || clip == null) return;

        var choice = IconPicker.Ask(owner, clip.Name, clip.IconId);
        if (choice == null) return;
        switch (choice.Kind)
        {
            case IconChoiceKind.Item:
                ClipStore.SetIcon(clipId, choice.Value!);
                break;
            case IconChoiceKind.File:
                if (ClipStore.SetImage(clipId, choice.Value!) is { } err) ShowErrors(new List<string> { err });
                break;
            case IconChoiceKind.Default:
                ClipStore.ClearImage(clipId);
                break;
        }
        Changed();
    }

    private void Palette_DragOver(object sender, DragEventArgs e)
    {
        bool files = Kind == WheelKind.Emote && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = e.Data.GetDataPresent(SlotFormat) ? DragDropEffects.Move : files ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Palette_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(SlotFormat) is int idx)
        {
            Slots[idx].ItemId = null; // kéo ô ra ngoài = làm trống
            Changed();
        }
        else if (Kind == WheelKind.Emote && e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            Import(files);
        }
    }

    // ───────────── Vòng chọn ─────────────

    private void Wheel_DragOver(object sender, DragEventArgs e)
    {
        int idx = _wheel.HitSlot(e.GetPosition(_wheel));
        bool ok = idx >= 0 && (e.Data.GetDataPresent(ItemFormat) || e.Data.GetDataPresent(SlotFormat));
        e.Effects = ok ? (e.Data.GetDataPresent(SlotFormat) ? DragDropEffects.Move : DragDropEffects.Copy) : DragDropEffects.None;
        _wheel.Hover = ok ? idx : -1;
        e.Handled = true;
    }

    private void Wheel_Drop(object sender, DragEventArgs e)
    {
        int idx = _wheel.HitSlot(e.GetPosition(_wheel));
        _wheel.Hover = -1;
        if (idx < 0) return;

        if (e.Data.GetData(ItemFormat) is string id)
        {
            Slots[idx].ItemId = id;
            Changed();
        }
        else if (e.Data.GetData(SlotFormat) is int from && from != idx)
        {
            (Slots[from], Slots[idx]) = (Slots[idx], Slots[from]); // đổi chỗ cả mục lẫn hành động
            Changed();
        }
    }

    private void Wheel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _downPos = e.GetPosition(_wheel);
        _downSlot = _wheel.HitSlot(_downPos);
        _dragging = false;
    }

    private void Wheel_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(_wheel);
        _wheel.Hover = _wheel.HitSlot(p);

        if (e.LeftButton != MouseButtonState.Pressed || _downSlot < 0 || _dragging) return;
        if (string.IsNullOrEmpty(Slots[_downSlot].ItemId) && !Slots[_downSlot].HasAction) return;
        var d = p - _downPos;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragging = true;
        DragDrop.DoDragDrop(_wheel, new DataObject(SlotFormat, _downSlot), DragDropEffects.Move);
        _downSlot = -1;
    }

    private void Wheel_MouseUp(object sender, MouseButtonEventArgs e)
    {
        int idx = _wheel.HitSlot(e.GetPosition(_wheel));
        if (_dragging || idx < 0 || idx != _downSlot) { _downSlot = -1; return; }
        _downSlot = -1;

        if (_pending != null)
        {
            Slots[idx].ItemId = _pending;
            _pending = null;
            Changed();
        }
        else if (Catalog.Get(Slots[idx].ItemId) is { } item)
        {
            App.Me.Preview(item);
        }
    }

    private void Wheel_RightClick(object sender, MouseButtonEventArgs e)
    {
        int idx = _wheel.HitSlot(e.GetPosition(_wheel));
        if (idx < 0) return;
        var slot = Slots[idx];

        var menu = new ContextMenu();
        var app = new MenuItem { Header = Loc.I["AssignApp"] };
        app.Click += (_, _) => AssignApp(slot);
        menu.Items.Add(app);

        var key = new MenuItem { Header = Loc.I["AssignHotkey"] };
        key.Click += (_, _) => AssignHotkey(slot);
        menu.Items.Add(key);

        if (slot.HasAction)
        {
            var clear = new MenuItem { Header = Loc.I.F("RemoveAction", slot.ActionLabel ?? "") };
            clear.Click += (_, _) => { slot.ClearAction(); Changed(); };
            menu.Items.Add(clear);
        }
        if (!string.IsNullOrEmpty(slot.ItemId))
        {
            menu.Items.Add(new Separator());
            var empty = new MenuItem { Header = Loc.I["ClearSlot"] };
            empty.Click += (_, _) => { slot.ItemId = null; Changed(); };
            menu.Items.Add(empty);
        }

        menu.PlacementTarget = _wheel;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void EnsureItem(WheelSlot slot)
    {
        if (string.IsNullOrEmpty(slot.ItemId))
            slot.ItemId = Kind == WheelKind.Signal ? "generic" : "e_smile";
    }

    private void AssignApp(WheelSlot slot)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.I["AssignApp"],
            Filter = Loc.I["AppFilter"] + "|*.exe;*.lnk;*.bat;*.cmd;*.url|" + Loc.I["AllFiles"] + "|*.*"
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        slot.ClearAction();
        slot.ActionKind = SlotActionKind.LaunchApp;
        slot.AppPath = dlg.FileName;
        EnsureItem(slot);
        Changed();
    }

    private void AssignHotkey(WheelSlot slot)
    {
        var owner = Window.GetWindow(this);
        if (owner == null) return;
        var hk = HotkeyBox.Ask(owner, slot.Keys);
        if (hk == null) return;
        slot.ClearAction();
        slot.ActionKind = SlotActionKind.Hotkey;
        slot.Keys = hk;
        EnsureItem(slot);
        Changed();
    }

    // ───────────── Nút ─────────────

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (Kind == WheelKind.Signal) S.SignalWheel = Defaults.SignalWheel();
        else S.EmoteWheel = Defaults.EmoteWheel();
        _pending = null;
        Changed();
    }

    private void AddSound_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.I["AddSound"],
            Filter = Loc.I["SoundFilter"] + "|" + string.Join(";", ClipStore.Extensions.Select(x => "*" + x)),
            Multiselect = true
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) Import(dlg.FileNames);
    }

    private void StopSounds_Click(object sender, RoutedEventArgs e) => App.Me.Sound.StopAll();

    private void AddImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.I["AddImage"],
            Filter = Loc.I["ImageFilter"] + "|*.png;*.jpg;*.jpeg;*.gif;*.webp",
            Multiselect = true
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) Import(dlg.FileNames);
    }

    /// <summary>Nhập file: âm thanh vào kho âm thanh, ảnh vào ảnh riêng (kéo-thả lẫn lộn cũng được).</summary>
    private void Import(IEnumerable<string> files)
    {
        var list = files.ToList();
        bool IsSound(string f) => ClipStore.Extensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant());

        var errors = new List<string>();
        var sounds = list.Where(IsSound).ToList();
        var images = list.Where(f => !IsSound(f)).ToList();
        if (sounds.Count > 0) errors.AddRange(ClipStore.Import(sounds));
        if (images.Count > 0) errors.AddRange(ImageStore.Import(images));

        if (sounds.Count > 0 && images.Count == 0) SelectFilter("clip");
        Changed();
        ShowErrors(errors);
        if (sounds.Count == 1 && S.Clips.Count > 0 && errors.Count == 0)
            PickIconFor(S.Clips[^1].Id);
    }

    private void SelectFilter(string tag)
    {
        foreach (ComboBoxItem it in FilterCombo.Items)
            if (Equals(it.Tag, tag)) { FilterCombo.SelectedItem = it; break; }
    }

    private void ShowErrors(List<string> errors)
    {
        if (errors.Count == 0) return;
        MessageBox.Show(Window.GetWindow(this)!, string.Join(Environment.NewLine, errors.Distinct()), "lolPing",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
