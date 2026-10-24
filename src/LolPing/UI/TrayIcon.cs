using System;
using System.ComponentModel;
using System.Windows;
using WinForms = System.Windows.Forms;
using LolPing.Core;

namespace LolPing.UI;

/// <summary>Biểu tượng ở khay hệ thống: nhấp để mở Cài đặt, menu chuột phải để bật/tắt hoặc thoát.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _open = new();
    private readonly WinForms.ToolStripMenuItem _toggle = new() { CheckOnClick = false };
    private readonly WinForms.ToolStripMenuItem _quit = new();

    public TrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(_open);
        menu.Items.Add(_toggle);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_quit);

        _open.Click += (_, _) => App.Me.ShowSettings();
        _toggle.Click += (_, _) => App.Me.ToggleEnabled();
        _quit.Click += (_, _) => App.Me.Quit();

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) App.Me.ShowSettings();
        };

        Loc.I.PropertyChanged += OnChanged;
        SettingsStore.Current.PropertyChanged += OnChanged;
        Update();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        bool on = SettingsStore.Current.Enabled;
        _open.Text = Loc.I["TrayOpen"];
        _toggle.Text = Loc.I["TrayEnabled"] + "    " + SettingsStore.Current.ToggleHotkey;
        _toggle.Checked = on;
        _quit.Text = Loc.I["TrayQuit"];
        _icon.Text = "lolPing — " + (on ? Loc.I["StatusOn"] : Loc.I["StatusOff"]);
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var res = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (res != null)
                return new System.Drawing.Icon(res.Stream, WinForms.SystemInformation.SmallIconSize);
        }
        catch (Exception ex)
        {
            Log.Error("Tray icon", ex);
        }
        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        Loc.I.PropertyChanged -= OnChanged;
        SettingsStore.Current.PropertyChanged -= OnChanged;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
