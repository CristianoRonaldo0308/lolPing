using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LolPing.Net;

/// <summary>Một tin nhắn trong phòng (được mã hoá trước khi gửi). Trường không dùng thì để null.</summary>
public sealed class NetMessage
{
    public const string Hello = "hello";   // có mặt / nhịp tim, kèm tên và màu
    public const string Bye = "bye";       // rời phòng
    public const string PingT = "ping";    // tín hiệu / biểu cảm / âm thanh
    public const string Need = "need";     // xin file còn thiếu
    public const string Asset = "asset";   // nội dung file (âm thanh hoặc ảnh)

    public string T { get; set; } = "";
    public string Id { get; set; } = "";
    public string From { get; set; } = "";

    public string? Name { get; set; }
    public string? Color { get; set; }

    // ping
    public string? Item { get; set; }      // id mục có sẵn (danger, e_laugh…)
    public string? Sound { get; set; }     // mã băm file âm thanh riêng
    public string? Image { get; set; }     // mã băm file ảnh riêng
    public string? Icon { get; set; }      // biểu tượng có sẵn đại diện cho âm thanh riêng
    public string? Label { get; set; }     // tên âm thanh / ảnh riêng
    public int Kind { get; set; }
    public int Vol { get; set; } = 100;
    public int Screen { get; set; }
    public double X { get; set; }
    public double Y { get; set; }

    // need / asset
    public string? Hash { get; set; }
    public string? Ext { get; set; }
    public byte[]? Data { get; set; }      // JSON hoá thành base64

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string NewId() => Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 8);

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, Json);

    public static NetMessage? Parse(byte[] utf8)
    {
        try { return JsonSerializer.Deserialize<NetMessage>(utf8, Json); }
        catch { return null; }
    }
}
