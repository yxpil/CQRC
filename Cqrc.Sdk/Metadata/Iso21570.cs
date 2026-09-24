using System.IO;
using System.Security.Cryptography;

namespace Cqrc.Metadata;

/// <summary>
/// ISO/IEC 21570:2016 “QR Code with additional information” 扩展元数据。
/// 在标准 QR 的 4 个字节模式段之后，附加一个专用段：
///   0000 0000            保留段头（全零 1 字节，见 21570 第 6 节）
///   0000 1010 0001 0000  固定 2 字节
///   数据长度(2B)         后续元数据字节数（大端）
///   元数据...            本实现为 JSON 对象（UTF-8），键为 21570 扩展字段
/// 注意：21570 段必须位于最后，且总段数 ≤ 4。
/// </summary>
public static class Iso21570
{
    /// <summary>段头保留字节（21570 规定 00 00 1010 0001 0000）。</summary>
    private static readonly byte[] SegmentHeader = { 0x00, 0x00, 0xA1, 0x00 };

    /// <summary>构建 21570 元数据段的原始字节（不含模式/长度头，由 QrDataChunk 添加）。</summary>
    public static byte[] BuildSegment(Dictionary<string, string?> metadata)
    {
        if (metadata.Count == 0) return Array.Empty<byte>();
        var json = EncodeJson(metadata);
        var payload = new byte[SegmentHeader.Length + 2 + json.Length];
        SegmentHeader.CopyTo(payload, 0);
        int len = json.Length;
        payload[4] = (byte)(len >> 8);
        payload[5] = (byte)(len & 0xFF);
        json.CopyTo(payload, 6);
        return payload;
    }

    /// <summary>极简 JSON 序列化（字符串值，UTF-8），避免引入第三方依赖。</summary>
    private static byte[] EncodeJson(Dictionary<string, string?> dict)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('{');
        bool first = true;
        foreach (var (key, value) in dict)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"'); sb.Append(Escape(key)); sb.Append('"');
            sb.Append(':');
            if (value is null) sb.Append("null");
            else { sb.Append('"'); sb.Append(Escape(value)); sb.Append('"'); }
        }
        sb.Append('}');
        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Escape(string s) => s
        .Replace("\\", "\\\\").Replace("\"", "\\\"")
        .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    /// <summary>计算某段元数据加入后占用的总比特（含 4bit 模式 + 16bit 长度 + 数据）。</summary>
    public static int SegmentBits(byte[] segment) => 4 + 16 + segment.Length * 8;

    /// <summary>内容哈希（SHA-256 hex），可作为元数据中的 content_hash 字段。</summary>
    public static string ContentHash(string content)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
