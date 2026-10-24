using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace LolPing.Core;

/// <summary>Ghép tên file của người dùng với id trong Catalog (dùng chung cho gói âm thanh và gói biểu tượng).</summary>
public static class PackNames
{
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["caution"] = "danger",
        ["danger"] = "danger",
        ["onmyway"] = "omw",
        ["omw"] = "omw",
        ["push"] = "push",
        ["assistme"] = "assist",
        ["assist"] = "assist",
        ["allin"] = "allin",
        ["needvision"] = "needvision",
        ["enemyvision"] = "enemyvision",
        ["enemymissing"] = "missing",
        ["missing"] = "missing",
        ["generic"] = "generic",
        ["bait"] = "bait",
        ["visioncleared"] = "cleared",
        ["cleared"] = "cleared",
        ["hold"] = "hold",
        ["retreat"] = "retreat",
        ["target"] = "target",
    };

    // Hậu tố ngẫu nhiên do trang tải về thêm vào, ví dụ "all-in-oUOa8h7PVzFMnaHx0o8inRFC1k7TRC".
    private static readonly Regex HashSuffix = new(@"[-_][A-Za-z0-9]{16,}$", RegexOptions.Compiled);

    /// <summary>"Caution_ping_SFX" → danger, "all-in-oUOa8h…" → allin, "e_laugh" → e_laugh.</summary>
    public static string? Match(string fileNameWithoutExtension)
    {
        var name = HashSuffix.Replace(fileNameWithoutExtension, "").ToLowerInvariant();
        if (Catalog.Get(name) != null) return name;

        var sb = new StringBuilder();
        foreach (var ch in name.Replace("ping", "").Replace("sfx", ""))
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return Aliases.TryGetValue(sb.ToString(), out var id) ? id : null;
    }
}

/// <summary>
/// Sao lưu thư mục người dùng chọn vào dữ liệu của app (%AppData%\lolPing\packs\…),
/// để di chuyển hay xoá thư mục gốc không làm mất âm thanh/biểu tượng.
/// </summary>
public static class PackMirror
{
    private static readonly object Gate = new();

    /// <returns>Thư mục để nạp, hoặc null nếu không có gì.</returns>
    public static string? Prepare(string? source, string copyDir, string[] extensions)
    {
        lock (Gate) return PrepareCore(source, copyDir, extensions);
    }

    private static string? PrepareCore(string? source, string copyDir, string[] extensions)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                Clear(copyDir);
                return null;
            }

            if (Directory.Exists(source))
            {
                Clear(copyDir);
                Directory.CreateDirectory(copyDir);
                foreach (var file in Directory.EnumerateFiles(source))
                {
                    if (!extensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                    if (PackNames.Match(Path.GetFileNameWithoutExtension(file)) == null) continue;
                    File.Copy(file, Path.Combine(copyDir, Path.GetFileName(file)), true);
                }
                return copyDir;
            }

            // Thư mục gốc đã bị di chuyển/xoá: dùng bản sao lưu lần trước.
            Log.Write($"Pack folder '{source}' not found, using saved copy");
            return Directory.Exists(copyDir) && Directory.EnumerateFiles(copyDir).Any() ? copyDir : null;
        }
        catch (Exception ex)
        {
            Log.Error("Mirror pack " + source, ex);
            return Directory.Exists(copyDir) ? copyDir : null;
        }
    }

    private static void Clear(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir))
            try { File.Delete(f); } catch { }
    }
}

/// <summary>Giải mã ảnh bằng ImageSharp (PNG, JPG, GIF, WebP, BMP) — không cần codec WebP của Windows.</summary>
public static class ImageDecode
{
    public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    /// <summary>Đọc ảnh, thu nhỏ để cạnh dài nhất ≤ maxSide, trả về ảnh WPF đã đóng băng (dùng được từ mọi luồng).</summary>
    public static BitmapImage Load(string path, int maxSide = 256)
    {
        using var image = SixLabors.ImageSharp.Image.Load(path);
        if (Math.Max(image.Width, image.Height) > maxSide)
            image.Mutate(x => x.Resize(new ResizeOptions { Size = new SixLabors.ImageSharp.Size(maxSide, maxSide), Mode = ResizeMode.Max }));

        var ms = new MemoryStream();
        image.SaveAsPng(ms);
        ms.Position = 0;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = ms;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>(rộng, cao) mà không giải mã toàn bộ ảnh.</summary>
    public static (int Width, int Height) Identify(string path)
    {
        var info = SixLabors.ImageSharp.Image.Identify(path);
        return (info.Width, info.Height);
    }
}

/// <summary>
/// Gói biểu tượng của người dùng: một thư mục ảnh, ghép với tín hiệu/biểu cảm theo tên file.
/// Ảnh được đọc tại chỗ, không chép đi đâu. Mục không có ảnh dùng biểu tượng vẽ sẵn.
/// </summary>
public static class IconPack
{
    private static volatile Dictionary<string, ImageSource> _icons = new();

    public static int Count => _icons.Count;

    /// <summary>Báo khi nạp xong (luôn gọi trên luồng giao diện).</summary>
    public static event Action? Changed;

    public static ImageSource? TryGet(string id) => _icons.TryGetValue(id, out var img) ? img : null;

    public static void Load(string? folder)
    {
        var ui = System.Windows.Application.Current?.Dispatcher;
        Task.Run(() =>
        {
            var dir = PackMirror.Prepare(folder, AppPaths.IconPackCopy, ImageDecode.Extensions);
            if (dir == null)
            {
                _icons = new();
                ui?.InvokeAsync(() => Changed?.Invoke());
                return;
            }
            var result = new Dictionary<string, ImageSource>();
            foreach (var file in Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (!ImageDecode.Extensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                var id = PackNames.Match(Path.GetFileNameWithoutExtension(file));
                if (id == null || result.ContainsKey(id)) continue;
                try
                {
                    result[id] = ImageDecode.Load(file);
                }
                catch (Exception ex)
                {
                    Log.Error("Decode icon " + file, ex);
                }
            }
            _icons = result;
            Log.Write($"Icon pack '{folder}': {result.Count} icons ({string.Join(", ", result.Keys)})");
            ui?.InvokeAsync(() => Changed?.Invoke());
        });
    }
}
