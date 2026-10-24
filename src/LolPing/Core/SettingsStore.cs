using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace LolPing.Core;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "lolPing");

    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Images => Path.Combine(Root, "images");
    public static string Sounds => Path.Combine(Root, "sounds");
    public static string SoundPackCopy => Path.Combine(Root, "packs", "sounds");
    public static string IconPackCopy => Path.Combine(Root, "packs", "icons");
    public static string Log => Path.Combine(Root, "log.txt");

    public static void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Images);
        Directory.CreateDirectory(Sounds);
    }
}

public static class Log
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(AppPaths.Log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { /* ghi log không bao giờ được làm sập ứng dụng */ }
    }

    public static void Error(string where, Exception ex) => Write($"{where}: {ex}");
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static DispatcherTimer? _saveTimer;

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        AppPaths.Ensure();
        try
        {
            if (File.Exists(AppPaths.Settings))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.Settings), Json) ?? new();
        }
        catch (Exception ex)
        {
            Log.Error("Load settings", ex);
            try { File.Copy(AppPaths.Settings, AppPaths.Settings + ".broken", true); } catch { }
            Current = new AppSettings();
        }

        Current.Normalize();
        Current.PropertyChanged += (_, _) => ScheduleSave();
    }

    /// <summary>Gom nhiều thay đổi liên tiếp (kéo thanh trượt) thành một lần ghi.</summary>
    public static void ScheduleSave()
    {
        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveTimer.Tick += (_, _) => { _saveTimer!.Stop(); SaveNow(); };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public static void SaveNow()
    {
        try
        {
            AppPaths.Ensure();
            var tmp = AppPaths.Settings + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
            File.Move(tmp, AppPaths.Settings, true);
        }
        catch (Exception ex)
        {
            Log.Error("Save settings", ex);
        }
    }
}

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "lolPing";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true)
                            ?? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled && Environment.ProcessPath is { } exe)
                key.SetValue(Name, $"\"{exe}\" --minimized");
            else
                key.DeleteValue(Name, false);
        }
        catch (Exception ex)
        {
            Log.Error("AutoStart", ex);
        }
    }
}
