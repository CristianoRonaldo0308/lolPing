using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LolPing.Core;

namespace LolPing.Net;

/// <summary>
/// File âm thanh/ảnh trao đổi trong phòng, định danh bằng mã băm SHA-256 (32 ký tự hex đầu).
/// File nhận được lưu ở %AppData%\lolPing\cache để lần sau không phải tải lại.
/// </summary>
public static class AssetCache
{
    public const long MaxAssetBytes = 3L * 1024 * 1024;
    private const long MaxCacheBytes = 300L * 1024 * 1024;

    private static readonly string[] SoundExt = { ".mp3", ".ogg", ".wav", ".m4a", ".wma", ".aac", ".flac" };
    private static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    public static string Dir => Path.Combine(AppPaths.Root, "cache");

    /// <summary>mã băm → đường dẫn (file của mình hoặc file đã nhận).</summary>
    private static readonly ConcurrentDictionary<string, string> Known = new();
    private static readonly ConcurrentDictionary<string, (DateTime Stamp, string Hash)> HashOfPath = new();

    public static void Init()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var files = new DirectoryInfo(Dir).GetFiles().OrderByDescending(f => f.LastAccessTimeUtc).ToList();
            long total = 0;
            foreach (var f in files)
            {
                total += f.Length;
                if (total > MaxCacheBytes) { try { f.Delete(); } catch { } continue; }
                Known[Path.GetFileNameWithoutExtension(f.Name)] = f.FullName;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Asset cache init", ex);
        }
    }

    public static bool IsAllowedExt(string ext) =>
        SoundExt.Contains(ext.ToLowerInvariant()) || ImageExt.Contains(ext.ToLowerInvariant());

    /// <summary>Mã băm của một file của mình (có lưu tạm theo thời điểm sửa), đồng thời ghi nhớ để trả lời "need".</summary>
    public static string? HashOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxAssetBytes) return null;
            if (HashOfPath.TryGetValue(path, out var c) && c.Stamp == info.LastWriteTimeUtc)
            {
                Known[c.Hash] = path;
                return c.Hash;
            }
            using var fs = info.OpenRead();
            var hash = Convert.ToHexString(SHA256.HashData(fs))[..32].ToLowerInvariant();
            HashOfPath[path] = (info.LastWriteTimeUtc, hash);
            Known[hash] = path;
            return hash;
        }
        catch (Exception ex)
        {
            Log.Error("Hash " + path, ex);
            return null;
        }
    }

    public static string? PathOf(string? hash) =>
        hash != null && Known.TryGetValue(hash, out var p) && File.Exists(p) ? p : null;

    /// <summary>Lưu file nhận được sau khi kiểm tra mã băm và định dạng.</summary>
    public static string? Store(string hash, string ext, byte[] data)
    {
        try
        {
            ext = ext.ToLowerInvariant();
            if (!IsAllowedExt(ext) || data.Length == 0 || data.Length > MaxAssetBytes) return null;
            if (hash.Length != 32 || !hash.All(Uri.IsHexDigit)) return null;
            var actual = Convert.ToHexString(SHA256.HashData(data))[..32].ToLowerInvariant();
            if (actual != hash) return null;

            var path = Path.Combine(Dir, hash + ext);
            if (!File.Exists(path)) File.WriteAllBytes(path, data);
            Known[hash] = path;
            return path;
        }
        catch (Exception ex)
        {
            Log.Error("Store asset", ex);
            return null;
        }
    }

    public static NetMessage? MakeAsset(string hash)
    {
        var path = PathOf(hash);
        if (path == null) return null;
        try
        {
            var data = File.ReadAllBytes(path);
            if (data.Length > MaxAssetBytes) return null;
            return new NetMessage { T = NetMessage.Asset, Hash = hash, Ext = Path.GetExtension(path), Data = data };
        }
        catch (Exception ex)
        {
            Log.Error("Read asset", ex);
            return null;
        }
    }

    public static IEnumerable<string> Missing(params string?[] hashes) =>
        hashes.Where(h => h != null && PathOf(h) == null).Select(h => h!);
}
