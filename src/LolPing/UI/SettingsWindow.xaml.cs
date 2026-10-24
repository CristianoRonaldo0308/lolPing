using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LolPing.Core;
using LolPing.Input;
using LolPing.Net;

namespace LolPing.UI;

public partial class SettingsWindow : Window
{
    private static readonly string[] LabelColors =
        { "#4FC3F7", "#FF8A65", "#AED581", "#BA68C8", "#FFD54F", "#4DB6AC", "#F06292", "#E0E0E0" };

    private bool _navFromCode;
    private bool _scrollFromNav;

    private static AppSettings S => SettingsStore.Current;

    public SettingsWindow()
    {
        InitializeComponent();
        TitleBar.Attach(this, "lolPing");
        DataContext = S;

        foreach (var (code, native, _) in Loc.Languages)
            LanguageCombo.Items.Add(new ComboBoxItem { Content = native, Tag = code });
        LanguageCombo.SelectedValue = S.Language;

        BuildSwatches();
        S.PropertyChanged += OnSettingChanged;
        Loc.I.PropertyChanged += OnLanguage;
        LolPing.Audio.SoundPack.Changed += UpdateTexts;
        IconPack.Changed += UpdateTexts;
        UpdateTexts();
        Activated += (_, _) => UpdateTexts();
        App.Me.Room.Changed += UpdateRoom;
        UpdateRoom(); // hiện app vừa bị tạm dừng khi người dùng quay lại cửa sổ

        _navFromCode = true;
        Nav.SelectedIndex = 0;
        _navFromCode = false;
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.Enabled) or nameof(AppSettings.TriggerKey)
            or nameof(AppSettings.ToggleHotkey) or nameof(AppSettings.EmoteKey))
            UpdateTexts();
        if (e.PropertyName == nameof(AppSettings.LabelColor)) BuildSwatches();
        if (e.PropertyName is nameof(AppSettings.LabelColor) or nameof(AppSettings.PlayerName) or nameof(AppSettings.ServerUrl)
            or nameof(AppSettings.LanEnabled) or nameof(AppSettings.InternetEnabled))
            UpdateRoom();
    }

    private void OnLanguage(object? sender, PropertyChangedEventArgs e) => UpdateTexts();

    private void UpdateTexts()
    {
        bool on = S.Enabled;
        StatusDot.Fill = on ? new SolidColorBrush(Color.FromRgb(0x5C, 0xD6, 0x6B)) : new SolidColorBrush(Color.FromRgb(0x7A, 0x82, 0x96));
        StatusTitle.Text = Loc.I[on ? "StatusOnTitle" : "StatusOffTitle"];
        StatusDesc.Text = Loc.I.F("StatusDesc", S.TriggerKey, S.ToggleHotkey);

        SoundFolderRow.Description = string.IsNullOrWhiteSpace(S.SoundFolder)
            ? Loc.I["SoundFolderNone"]
            : Loc.I.F("SoundFolderLoaded", S.SoundFolder, LolPing.Audio.SoundPack.Count);
        ClearSoundFolderButton.Visibility = string.IsNullOrWhiteSpace(S.SoundFolder) ? Visibility.Collapsed : Visibility.Visible;

        var last = Fullscreen.LastPaused;
        bool canAllow = last != null && !Fullscreen.IsAllowed(last);
        FullscreenAllowRow.Description = canAllow ? Loc.I.F("FullscreenAllowDescLast", last!) : Loc.I["FullscreenAllowDesc"];
        AllowLastButton.Content = canAllow ? Loc.I.F("AllowApp", last!) : "";
        AllowLastButton.Visibility = canAllow ? Visibility.Visible : Visibility.Collapsed;

        IconFolderRow.Description = string.IsNullOrWhiteSpace(S.IconFolder)
            ? Loc.I["IconFolderNone"]
            : Loc.I.F("IconFolderLoaded", S.IconFolder, IconPack.Count);
        ClearIconFolderButton.Visibility = string.IsNullOrWhiteSpace(S.IconFolder) ? Visibility.Collapsed : Visibility.Visible;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        AboutRow.Description = Loc.I.F("AboutDesc", version?.ToString(3) ?? "0.1.0");

        // Mô tả phụ thuộc phím đang chọn cũng phải cập nhật trên các trình sửa vòng.
        foreach (var editor in FindChildren<WheelEditor>(Body)) if (editor.IsLoaded) editor.Refresh();
    }

    private void BuildSwatches()
    {
        Swatches.Children.Clear();
        foreach (var hex in LabelColors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            bool selected = string.Equals(S.LabelColor, hex, StringComparison.OrdinalIgnoreCase);
            var dot = new Ellipse
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(4, 0, 4, 0),
                Fill = new SolidColorBrush(color),
                Stroke = selected ? Brushes.White : Brushes.Transparent,
                StrokeThickness = 3,
                Cursor = Cursors.Hand,
                ToolTip = hex
            };
            dot.MouseLeftButtonUp += (_, _) => S.LabelColor = hex;
            Swatches.Children.Add(dot);
        }
    }

    // ───────────── Điều hướng: nhấp mục bên trái để cuộn tới phần tương ứng ─────────────

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_navFromCode || Nav.SelectedItem is not ListBoxItem { Tag: string name }) return;
        if (FindName(name) is not FrameworkElement target) return;

        var offset = target.TranslatePoint(new Point(0, 0), Body).Y - 12;
        _scrollFromNav = true;
        Scroller.ScrollToVerticalOffset(Math.Max(0, offset));
    }

    private void Scroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_scrollFromNav) { _scrollFromNav = false; return; }
        if (e.VerticalChange == 0) return;

        // Làm nổi mục của phần đang ở đầu khung nhìn.
        int index = 0;
        for (int i = 0; i < Nav.Items.Count; i++)
        {
            if (Nav.Items[i] is ListBoxItem { Tag: string name } && FindName(name) is FrameworkElement t &&
                t.TranslatePoint(new Point(0, 0), Body).Y <= Scroller.VerticalOffset + 80)
                index = i;
        }
        if (Scroller.VerticalOffset >= Scroller.ScrollableHeight - 2) index = Nav.Items.Count - 1;

        if (Nav.SelectedIndex != index)
        {
            _navFromCode = true;
            Nav.SelectedIndex = index;
            _navFromCode = false;
        }
    }

    // ───────────── Nút ─────────────

    private void TestPing_Click(object sender, RoutedEventArgs e)
    {
        var item = Catalog.Get(S.SignalWheel[Defaults.Center].ItemId) ?? Catalog.Get("generic");
        if (item != null) App.Me.Preview(item);
    }

    private void ChooseSoundFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.I["SoundFolder"] };
        if (!string.IsNullOrWhiteSpace(S.SoundFolder)) dlg.InitialDirectory = S.SoundFolder;
        if (dlg.ShowDialog(this) == true) S.SoundFolder = dlg.FolderName;
    }

    // ───────────── Phòng ─────────────

    private void UpdateRoom()
    {
        var room = App.Me.Room;
        bool inRoom = room.InRoom;
        RoomJoinCard.Visibility = inRoom ? Visibility.Collapsed : Visibility.Visible;
        RoomCard.Visibility = inRoom ? Visibility.Visible : Visibility.Collapsed;

        ServerRow.Description = string.IsNullOrWhiteSpace(S.ServerUrl) ? Loc.I["ServerUrlNone"] : Loc.I["ServerUrlDesc"];

        if (!inRoom) return;
        RoomCodeText.Text = room.Code;

        string lan = !S.LanEnabled ? Loc.I["LanOff"]
            : room.LanError != null ? Loc.I.F("LanError", room.LanError)
            : Loc.I.F("LanStatus", room.LanCount);
        string net = !S.InternetEnabled ? Loc.I["NetOff"]
            : string.IsNullOrWhiteSpace(S.ServerUrl) ? Loc.I["NetNoServer"]
            : room.RelayState switch
            {
                RelayState.Connected => Loc.I["NetConnected"],
                RelayState.Connecting => Loc.I["NetConnecting"],
                RelayState.Full => Loc.I["NetFull"],
                RelayState.Error => Loc.I.F("NetError", room.RelayError ?? ""),
                _ => Loc.I["NetOff"]
            };
        RoomStatus.Text = $"{lan}  ·  {net}";

        MemberList.Children.Clear();
        MemberList.Children.Add(MemberRow(RoomService.MyName + " " + Loc.I["You"], S.LabelColor, null));
        foreach (var p in room.Peers)
        {
            string via = p.ViaLan && p.ViaRelay ? "LAN + Internet" : p.ViaLan ? "LAN" : "Internet";
            MemberList.Children.Add(MemberRow(p.Name, p.Color, via));
        }
        if (room.Peers.Count == 0)
            MemberList.Children.Add(new TextBlock { Text = Loc.I["RoomWaiting"], Style = (Style)FindResource("Subtext"), Margin = new Thickness(0, 6, 0, 0) });
    }

    private static FrameworkElement MemberRow(string name, string color, string? via)
    {
        Brush dot;
        try { dot = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        catch { dot = Brushes.White; }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        row.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = dot, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        row.Children.Add(new TextBlock { Text = name, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
        if (via != null)
            row.Children.Add(new TextBlock
            {
                Text = "  ·  " + via,
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["SubtextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });
        return row;
    }

    private void CreateRoom_Click(object sender, RoutedEventArgs e)
    {
        JoinError.Visibility = Visibility.Collapsed;
        App.Me.Room.Create();
    }

    private void JoinRoom_Click(object sender, RoutedEventArgs e) => JoinFromBox();

    private void JoinBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) JoinFromBox();
    }

    private void JoinFromBox()
    {
        if (App.Me.Room.Join(JoinBox.Text, out var error))
        {
            JoinBox.Text = "";
            JoinError.Visibility = Visibility.Collapsed;
        }
        else
        {
            JoinError.Text = error;
            JoinError.Visibility = Visibility.Visible;
        }
    }

    private void CopyRoom_Click(object sender, RoutedEventArgs e)
    {
        var code = App.Me.Room.Code;
        if (code == null) return;
        // Lời mời kèm địa chỉ server để bạn bè dán một lần là vào được qua Internet.
        var invite = !string.IsNullOrWhiteSpace(S.ServerUrl) && S.InternetEnabled ? $"{code} @ {S.ServerUrl.Trim()}" : code;
        try { Clipboard.SetText(invite); } catch { }
    }

    private void LeaveRoom_Click(object sender, RoutedEventArgs e) => App.Me.Room.Leave();

    private void AllowLast_Click(object sender, RoutedEventArgs e)
    {
        if (Fullscreen.LastPaused is not { } name) return;
        var list = Fullscreen.ParseList(S.FullscreenAllowList).ToList();
        if (!list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
        S.FullscreenAllowList = string.Join(", ", list);
        UpdateTexts();
    }

    private void ChooseIconFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.I["IconFolder"] };
        if (!string.IsNullOrWhiteSpace(S.IconFolder)) dlg.InitialDirectory = S.IconFolder;
        if (dlg.ShowDialog(this) == true) S.IconFolder = dlg.FolderName;
    }

    private void ClearIconFolder_Click(object sender, RoutedEventArgs e) => S.IconFolder = "";

    private void ClearSoundFolder_Click(object sender, RoutedEventArgs e) => S.SoundFolder = "";

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.Ensure();
        Process.Start(new ProcessStartInfo(AppPaths.Root) { UseShellExecute = true });
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => App.Me.Quit();

    protected override void OnClosing(CancelEventArgs e)
    {
        // Nút X chỉ ẩn cửa sổ; ứng dụng tiếp tục chạy ở khay hệ thống.
        if (!App.Me.IsExiting)
        {
            e.Cancel = true;
            Hide();
            SettingsStore.SaveNow();
        }
        base.OnClosing(e);
    }

    private static System.Collections.Generic.IEnumerable<T> FindChildren<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var c in FindChildren<T>(child)) yield return c;
        }
    }
}
