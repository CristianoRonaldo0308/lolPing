using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LolPing.Core;

public sealed class ItemDef
{
    public string Id { get; init; } = "";
    public WheelKind Kind { get; init; }
    public string? NameKey { get; init; }
    public string? CustomName { get; init; }
    public Color Accent { get; init; } = Colors.White;
    public string Sound { get; init; } = "pop";
    internal Func<ImageSource> Factory { get; init; } = Icons.Generic;

    private ImageSource? _image;

    public bool IsCustom => CustomName != null;
    public bool IsClip => Id.StartsWith(ClipIdPrefix, StringComparison.Ordinal);
    public const string ClipIdPrefix = "clip:";
    public string DisplayName => CustomName ?? Loc.I[NameKey ?? Id];
    public ImageSource Image => IconPack.TryGet(Id) ?? (_image ??= Factory());
}

/// <summary>Danh mục tín hiệu và biểu cảm có sẵn + ảnh riêng của người dùng.</summary>
public static class Catalog
{
    private static readonly List<ItemDef> Builtins = new()
    {
        Sig("danger", "ItemDanger", 0xE53950, Icons.Danger),
        Sig("omw", "ItemOmw", 0x3D8BFF, Icons.OnMyWay),
        Sig("push", "ItemPush", 0x4CC36A, Icons.Push),
        Sig("assist", "ItemAssist", 0x5DD35D, Icons.Assist),
        Sig("allin", "ItemAllIn", 0xF2C230, Icons.AllIn),
        Sig("needvision", "ItemNeedVision", 0x36C9A8, Icons.NeedVision),
        Sig("enemyvision", "ItemEnemyVision", 0xE8559B, Icons.EnemyVision),
        Sig("missing", "ItemMissing", 0xF5C842, Icons.Missing),
        Sig("generic", "ItemGeneric", 0x3D8BFF, Icons.Generic),
        Sig("bait", "ItemBait", 0xF59E2A, Icons.Bait),
        Sig("cleared", "ItemCleared", 0x5AA9FF, Icons.Cleared),
        Sig("hold", "ItemHold", 0x8FA3C7, Icons.Hold),
        Sig("retreat", "ItemRetreat", 0xFF7A45, Icons.Retreat),
        Sig("target", "ItemTarget", 0xFF4D5E, Icons.Target),

        Emo("e_smile", "EmoSmile", Icons.Smile, "pop"),
        Emo("e_laugh", "EmoLaugh", Icons.Laugh, "laugh"),
        Emo("e_love", "EmoLove", Icons.Love, "love"),
        Emo("e_gg", "EmoGg", Icons.Gg, "gg"),
        Emo("e_wow", "EmoWow", Icons.Wow, "wow"),
        Emo("e_cool", "EmoCool", Icons.Cool, "pop"),
        Emo("e_sad", "EmoSad", Icons.Sad, "sad"),
        Emo("e_cry", "EmoCry", Icons.Cry, "sad"),
        Emo("e_angry", "EmoAngry", Icons.Angry, "angry"),
        Emo("e_sweat", "EmoSweat", Icons.Sweat, "pop"),
        Emo("e_sleepy", "EmoSleepy", Icons.Sleepy, "sleepy"),
    };

    private static List<ItemDef> _custom = new();

    // Mỗi tín hiệu dùng âm thanh trùng với id của nó (xem Audio/Sounds.cs).
    private static ItemDef Sig(string id, string key, uint color, Func<ImageSource> f) =>
        new() { Id = id, Kind = WheelKind.Signal, NameKey = key, Accent = Icons.C(color), Sound = id, Factory = f };

    private static ItemDef Emo(string id, string key, Func<ImageSource> f, string sound) =>
        new() { Id = id, Kind = WheelKind.Emote, NameKey = key, Accent = Icons.C(0xFFCC3D), Sound = sound, Factory = f };

    public static IEnumerable<ItemDef> Of(WheelKind kind) =>
        Builtins.Where(b => b.Kind == kind).Concat(kind == WheelKind.Emote ? _custom : Enumerable.Empty<ItemDef>());

    public static ItemDef? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return Builtins.FirstOrDefault(b => b.Id == id) ?? _custom.FirstOrDefault(c => c.Id == id);
    }

    public static void ReloadCustom()
    {
        var clips = SettingsStore.Current.Clips.Select(c => new ItemDef
        {
            Id = c.Id,
            Kind = WheelKind.Emote,
            CustomName = c.Name,
            Accent = Colors.White,
            Sound = c.Id,
            Factory = () =>
                c.ImageFile != null ? ImageStore.Load(c.ImageFile)
                : Get(c.IconId) is { IsClip: false } icon ? icon.Image
                : Icons.ClipCard(c.Name)
        });

        _custom = SettingsStore.Current.CustomImages
            .Select(ci => new ItemDef
            {
                Id = ci.Id,
                Kind = WheelKind.Emote,
                CustomName = ci.Name,
                Accent = Colors.White,
                Sound = "pop",
                Factory = () => ImageStore.Load(ci.FileName)
            })
            .Concat(clips)
            .ToList();
    }
}

/// <summary>Ảnh riêng: chép vào %AppData%\lolPing\images, chỉ dùng trên máy này.</summary>
public static class ImageStore
{
    public const int MaxImages = 24;
    public const long MaxBytes = 8L * 1024 * 1024;
    public const int MaxSide = 8192;
    private static readonly string[] Allowed = { ".png", ".jpg", ".jpeg", ".gif", ".webp" }; // giải mã bằng ImageSharp

    public static List<string> Import(IEnumerable<string> paths)
    {
        var errors = new List<string>();
        var s = SettingsStore.Current;

        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            if (s.CustomImages.Count >= MaxImages)
            {
                errors.Add(Loc.I.F("ErrImageLimit", MaxImages));
                break;
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (!Allowed.Contains(ext)) { errors.Add(Loc.I.F("ErrImageFormat", name)); continue; }

            try
            {
                if (new FileInfo(path).Length > MaxBytes) { errors.Add(Loc.I.F("ErrImageSize", name)); continue; }

                var (w, h) = ImageDecode.Identify(path);
                if (Math.Max(w, h) > MaxSide)
                {
                    errors.Add(Loc.I.F("ErrImageDims", name, MaxSide));
                    continue;
                }

                var id = Guid.NewGuid().ToString("N");
                var file = id + ext;
                File.Copy(path, Path.Combine(AppPaths.Images, file));
                s.CustomImages.Add(new CustomImage { Id = "img:" + id, Name = Path.GetFileNameWithoutExtension(path), FileName = file });
            }
            catch (Exception ex)
            {
                Log.Error("Import image " + path, ex);
                errors.Add(Loc.I.F("ErrImageDecode", name));
            }
        }

        Catalog.ReloadCustom();
        SettingsStore.ScheduleSave();
        return errors;
    }

    public static void Remove(string id)
    {
        var s = SettingsStore.Current;
        var img = s.CustomImages.FirstOrDefault(c => c.Id == id);
        if (img == null) return;
        s.CustomImages.Remove(img);
        foreach (var slot in s.EmoteWheel.Where(w => w.ItemId == id)) slot.ItemId = null;
        try { File.Delete(Path.Combine(AppPaths.Images, img.FileName)); } catch { }
        Catalog.ReloadCustom();
        SettingsStore.ScheduleSave();
    }

    public static ImageSource Load(string fileName)
    {
        try
        {
            return ImageDecode.Load(Path.Combine(AppPaths.Images, fileName));
        }
        catch (Exception ex)
        {
            Log.Error("Load image " + fileName, ex);
            return Icons.Generic();
        }
    }
}
