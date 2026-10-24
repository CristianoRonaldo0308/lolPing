using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LolPing.Net;

/// <summary>
/// Mã phòng dạng PING-XXXXX-XXXXX (10 ký tự, ~50 bit). Từ mã phòng dẫn xuất ra:
///  • RoomId (hex, gửi cho server/LAN để ghép người cùng phòng) — không suy ngược ra được mã;
///  • khoá AES-256-GCM (chỉ nằm trên máy) để mã hoá mọi tin nhắn.
/// PBKDF2 làm chậm việc dò mã từ RoomId; server không đọc được nội dung.
/// </summary>
public sealed class RoomCrypto
{
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"; // bỏ 0/1/I/O cho dễ đọc
    private const int CodeLength = 10;
    private const byte Version = 1;

    public string Code { get; }
    public string RoomId { get; }
    private readonly byte[] _key;
    private readonly byte[] _aad;

    private RoomCrypto(string code)
    {
        Code = code;
        var root = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes("lolping/room/v1"),
            150_000, HashAlgorithmName.SHA256, 32);
        RoomId = Convert.ToHexString(HKDF.Expand(HashAlgorithmName.SHA256, root, 16, Encoding.UTF8.GetBytes("room-id"))).ToLowerInvariant();
        _key = HKDF.Expand(HashAlgorithmName.SHA256, root, 32, Encoding.UTF8.GetBytes("message-key"));
        _aad = Encoding.UTF8.GetBytes(RoomId);
    }

    public static string NewCode()
    {
        var chars = new char[CodeLength];
        for (int i = 0; i < CodeLength; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return Format(new string(chars));
    }

    /// <summary>Chuẩn hoá mã người dùng nhập ("ping rrg7c 9jm5b", "PING-RRG7C-9JM5B"…).</summary>
    public static string? Normalize(string input)
    {
        var s = new string(input.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (s.StartsWith("PING", StringComparison.Ordinal) && s.Length == CodeLength + 4) s = s[4..];
        s = s.Replace('0', 'O').Replace('1', 'I'); // gõ nhầm số → không hợp lệ, báo lỗi bên dưới
        if (s.Length != CodeLength || s.Any(c => Alphabet.IndexOf(c) < 0)) return null;
        return Format(s);
    }

    private static string Format(string raw) => $"PING-{raw[..5]}-{raw[5..]}";

    public static RoomCrypto For(string normalizedCode) => new(normalizedCode);

    public byte[] Seal(byte[] plain)
    {
        var output = new byte[1 + 12 + plain.Length + 16];
        output[0] = Version;
        var nonce = output.AsSpan(1, 12);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, output.AsSpan(13, plain.Length), output.AsSpan(13 + plain.Length, 16), _aad);
        return output;
    }

    /// <returns>null nếu sai khoá, bị sửa đổi hoặc không đúng định dạng.</returns>
    public byte[]? Open(byte[] data)
    {
        if (data.Length < 1 + 12 + 16 || data[0] != Version) return null;
        int len = data.Length - 29;
        var plain = new byte[len];
        try
        {
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(data.AsSpan(1, 12), data.AsSpan(13, len), data.AsSpan(13 + len, 16), plain, _aad);
            return plain;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
