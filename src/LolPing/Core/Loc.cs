using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace LolPing.Core;

/// <summary>
/// Bảng chuỗi giao diện. Mỗi ngôn ngữ là một file Lang/&lt;mã&gt;.json nhúng trong exe.
/// Thêm ngôn ngữ mới: tạo file json cùng khoá rồi thêm vào <see cref="Languages"/>.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();

    /// <summary>(mã, tên hiển thị bằng chính ngôn ngữ đó, viết từ phải sang trái?)</summary>
    public static readonly (string Code, string Native, bool Rtl)[] Languages =
    {
        ("vi", "Tiếng Việt", false),
        ("en", "English", false),
    };

    private Dictionary<string, string> _strings = new();
    private readonly Dictionary<string, string> _fallback;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Lang { get; private set; } = "en";
    public FlowDirection Flow { get; private set; } = FlowDirection.LeftToRight;

    private Loc()
    {
        _fallback = Read("en");
        _strings = _fallback;
    }

    public string this[string key] =>
        _strings.TryGetValue(key, out var v) ? v :
        _fallback.TryGetValue(key, out v) ? v : key;

    public string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, this[key], args);

    public static string Detect()
    {
        var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Languages.Any(l => l.Code == two) ? two : "en";
    }

    public void Load(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang) || Languages.All(l => l.Code != lang)) lang = Detect();
        _strings = lang == "en" ? _fallback : Read(lang);
        Lang = lang;
        Flow = Languages.First(l => l.Code == lang).Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lang)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Flow)));
    }

    private static Dictionary<string, string> Read(string lang)
    {
        try
        {
            using Stream? s = typeof(Loc).Assembly.GetManifestResourceStream($"Lang.{lang}.json");
            if (s == null) return new();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? new();
        }
        catch (Exception ex)
        {
            Log.Error($"Read language {lang}", ex);
            return new();
        }
    }
}

/// <summary>Dùng trong XAML: Text="{c:Tr KeyName}" — tự cập nhật khi đổi ngôn ngữ.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }
    public TrExtension(string key) { Key = key; }

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Loc.I, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
