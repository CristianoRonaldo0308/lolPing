using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LolPing.Core;

namespace LolPing.Audio;

/// <summary>
/// Kho âm thanh: file được chép vào %AppData%\lolPing\sounds và giải mã sẵn vào bộ nhớ
/// để bấm là phát ngay. Mỗi âm là một mục biểu cảm (id "clip:…") kéo được vào vòng chọn.
/// </summary>
public static class ClipStore
{
    public const int MaxClips = 200;
    public const long MaxBytes = 20L * 1024 * 1024;
    public const int MaxSeconds = 30;
    public static readonly string[] Extensions = { ".mp3", ".ogg", ".wav", ".m4a", ".wma", ".aac", ".flac" };

    private static readonly ConcurrentDictionary<string, float[]> Cache = new();

    private static AppSettings S => SettingsStore.Current;

    public static SoundClip? Find(string id) => S.Clips.FirstOrDefault(c => c.Id == id);

    /// <summary>Âm đã giải mã; giải mã ngay nếu chưa có trong bộ nhớ.</summary>
    public static float[]? Get(string id)
    {
        if (Cache.TryGetValue(id, out var data)) return data;
        var clip = Find(id);
        if (clip == null) return null;
        try
        {
            data = SoundPack.Decode(Path.Combine(AppPaths.Sounds, clip.SoundFile), MaxSeconds);
            Cache[id] = data;
            return data;
        }
        catch (Exception ex)
        {
            Log.Error("Decode clip " + clip.SoundFile, ex);
            return null;
        }
    }

    /// <summary>Giải mã trước toàn bộ kho ở nền khi khởi động.</summary>
    public static void Preload()
    {
        var ids = S.Clips.Select(c => c.Id).ToList();
        Task.Run(() => { foreach (var id in ids) Get(id); });
    }

    public static List<string> Import(IEnumerable<string> paths)
    {
        var errors = new List<string>();
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            if (S.Clips.Count >= MaxClips) { errors.Add(Loc.I.F("ErrClipLimit", MaxClips)); break; }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (!Extensions.Contains(ext)) { errors.Add(Loc.I.F("ErrClipFormat", name)); continue; }

            try
            {
                if (new FileInfo(path).Length > MaxBytes) { errors.Add(Loc.I.F("ErrClipSize", name)); continue; }

                var data = SoundPack.Decode(path, MaxSeconds); // giải mã thử: file hỏng thì báo ngay
                if (data.Length == 0) { errors.Add(Loc.I.F("ErrClipDecode", name)); continue; }

                var guid = Guid.NewGuid().ToString("N");
                var file = guid + ext;
                File.Copy(path, Path.Combine(AppPaths.Sounds, file));
                var clip = new SoundClip { Id = ItemDef.ClipIdPrefix + guid, Name = Path.GetFileNameWithoutExtension(path), SoundFile = file };
                S.Clips.Add(clip);
                Cache[clip.Id] = data;
            }
            catch (Exception ex)
            {
                Log.Error("Import clip " + path, ex);
                errors.Add(Loc.I.F("ErrClipDecode", name));
            }
        }
        Changed();
        return errors;
    }

    public static void Rename(string id, string name)
    {
        if (Find(id) is not { } clip || string.IsNullOrWhiteSpace(name)) return;
        clip.Name = name.Trim();
        Changed();
    }

    public static void SetVolume(string id, int percent)
    {
        if (Find(id) is not { } clip) return;
        clip.Volume = Math.Clamp(percent, 10, 200);
        Changed();
    }

    /// <summary>Gắn ảnh cho âm (ảnh được chép vào thư mục images).</summary>
    public static string? SetImage(string id, string imagePath)
    {
        if (Find(id) is not { } clip) return null;
        try
        {
            var ext = Path.GetExtension(imagePath).ToLowerInvariant();
            if (!ImageDecode.Extensions.Contains(ext)) return Loc.I.F("ErrImageFormat", Path.GetFileName(imagePath));
            ImageDecode.Identify(imagePath);
            var file = "clip_" + Guid.NewGuid().ToString("N") + ext;
            File.Copy(imagePath, Path.Combine(AppPaths.Images, file));
            DeleteImage(clip);
            clip.ImageFile = file;
            clip.IconId = null;
            Changed();
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Clip image " + imagePath, ex);
            return Loc.I.F("ErrImageDecode", Path.GetFileName(imagePath));
        }
    }

    /// <summary>Bỏ ảnh/biểu tượng, quay về thẻ màu mặc định.</summary>
    public static void ClearImage(string id)
    {
        if (Find(id) is not { } clip) return;
        DeleteImage(clip);
        clip.ImageFile = null;
        clip.IconId = null;
        Changed();
    }

    /// <summary>Dùng biểu tượng của một mục có sẵn (biểu cảm, tín hiệu hoặc ảnh riêng) làm hình đại diện.</summary>
    public static void SetIcon(string id, string iconId)
    {
        if (Find(id) is not { } clip || Catalog.Get(iconId) is not { IsClip: false }) return;
        DeleteImage(clip);
        clip.ImageFile = null;
        clip.IconId = iconId;
        Changed();
    }

    public static void Remove(string id)
    {
        if (Find(id) is not { } clip) return;
        S.Clips.Remove(clip);
        foreach (var slot in S.EmoteWheel.Where(w => w.ItemId == id)) slot.ItemId = null;
        DeleteImage(clip);
        try { File.Delete(Path.Combine(AppPaths.Sounds, clip.SoundFile)); } catch { }
        Cache.TryRemove(id, out _);
        Changed();
    }

    private static void DeleteImage(SoundClip clip)
    {
        if (clip.ImageFile == null) return;
        try { File.Delete(Path.Combine(AppPaths.Images, clip.ImageFile)); } catch { }
    }

    private static void Changed()
    {
        Catalog.ReloadCustom();
        SettingsStore.ScheduleSave();
    }
}
