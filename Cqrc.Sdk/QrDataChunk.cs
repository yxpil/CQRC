using System.Text;
using System.Text.RegularExpressions;

namespace Cqrc;

/// <summary>字母数字模式字符表（45 个字符）。</summary>
public static class Alphanumeric
{
    public const string Table = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";
    private static readonly Regex AlnumPattern = new("^[0-9A-Z $%*+\\-/:.]*\\Z");

    public static int IndexOf(char c)
    {
        int i = Table.IndexOf(c);
        if (i < 0) throw new ArgumentException($"字符 '{c}' 不属于字母数字模式", nameof(c));
        return i;
    }

    public static bool IsAlphanumeric(string s)
        => s.Length > 0 && AlnumPattern.IsMatch(s);
}

/// <summary>
/// QR 数据段：按模式（数字/字母数字/字节）编码的一段数据（ISO/IEC 18004 §7.4.4）。
/// </summary>
public sealed class QrDataChunk
{
    public QrMode Mode { get; }
    public byte[] Data { get; }
    public int CharCount { get; }

    public QrDataChunk(QrMode mode, byte[] data, int? charCount = null)
    {
        Mode = mode;
        Data = data;
        CharCount = charCount ?? (mode == QrMode.Byte ? data.Length : Encoding.UTF8.GetString(data).Length);
    }

    /// <summary>自动选择最紧凑模式：全数字→Numeric，全字母数字→Alphanumeric，否则→Byte(UTF-8)。</summary>
    public static QrDataChunk Auto(string text)
    {
        if (text.Length == 0) return new QrDataChunk(QrMode.Byte, Array.Empty<byte>());
        if (text.All(char.IsDigit))
            return new QrDataChunk(QrMode.Numeric, Encoding.UTF8.GetBytes(text));
        if (Alphanumeric.IsAlphanumeric(text))
            return new QrDataChunk(QrMode.Alphanumeric, Encoding.UTF8.GetBytes(text));
        return new QrDataChunk(QrMode.Byte, Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// python-qrcode 的 <c>optimize</c> 等价切分（optimal_data_chunks + _optimal_split）：
    /// 在 UTF-8 字节流上先取最长数字串（≥minimum），再对剩余段取最长字母数字串，其余为字节段。
    /// </summary>
    public static List<QrDataChunk> SplitOptimal(string text, int minimum = 4)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        // 短输入时 python 使用锚定正则：整串全数字/全字母数字才压缩，否则整体按字节处理。
        bool anchored = bytes.Length <= minimum;
        var chunks = new List<QrDataChunk>();

        foreach (var (matched, seg) in SplitRuns(bytes, b => b is >= (byte)'0' and <= (byte)'9', minimum, anchored))
        {
            if (matched) { chunks.Add(new QrDataChunk(QrMode.Numeric, seg)); continue; }
            foreach (var (isAlpha, sub) in SplitRuns(seg, IsAlphaByte, minimum, anchored))
                chunks.Add(new QrDataChunk(isAlpha ? QrMode.Alphanumeric : QrMode.Byte, sub));
        }
        return chunks;
    }

    private static bool IsAlphaByte(byte b) => b < 0x80 && Alphanumeric.Table.IndexOf((char)b) >= 0;

    /// <summary>左most-最长游标切分，等价于对 pattern 的 <c>re.search</c> 迭代；未命中的间隙按原序输出。</summary>
    private static List<(bool matched, byte[] segment)> SplitRuns(
        byte[] data, Func<byte, bool> predicate, int minimum, bool anchored)
    {
        var parts = new List<(bool, byte[])>();
        if (data.Length == 0) return parts;
        if (anchored)
            return new List<(bool, byte[])> { (data.All(predicate), data) };

        int cursor = 0, i = 0;
        while (i < data.Length)
        {
            int run = i;
            while (run < data.Length && predicate(data[run])) run++;
            if (run - i >= minimum)
            {
                if (i > cursor) parts.Add((false, data[cursor..i]));
                parts.Add((true, data[i..run]));
                cursor = i = run;
            }
            else
            {
                i = run > i ? run : i + 1; // 短游程内的起点同样无法构成命中，整体跳过
            }
        }
        if (cursor < data.Length) parts.Add((false, data[cursor..]));
        return parts;
    }

    /// <summary>按 ISO 格式写入缓冲：模式(4bit) + 长度(countBits) + 数据。</summary>
    public void Write(BitBuffer buffer, int version)
    {
        int modeBits = Mode switch
        {
            QrMode.Numeric => QrModeBits.Numeric,
            QrMode.Alphanumeric => QrModeBits.Alphanumeric,
            QrMode.Byte => QrModeBits.Byte,
            QrMode.Kanji => QrModeBits.Kanji,
            _ => throw new InvalidOperationException(),
        };
        buffer.Put(modeBits, 4);
        buffer.Put(CharCount, QrTables.LengthBits(Mode, version));

        switch (Mode)
        {
            case QrMode.Numeric:
            {
                var s = Encoding.UTF8.GetString(Data);
                for (int i = 0; i < CharCount; i += 3)
                {
                    int count = Math.Min(3, CharCount - i);
                    buffer.Put(int.Parse(s.Substring(i, count)), count switch { 1 => 4, 2 => 7, _ => 10 });
                }
                break;
            }
            case QrMode.Alphanumeric:
            {
                var s = Encoding.UTF8.GetString(Data);
                for (int i = 0; i < CharCount; i += 2)
                {
                    if (i + 1 < CharCount)
                        buffer.Put(Alphanumeric.IndexOf(s[i]) * 45 + Alphanumeric.IndexOf(s[i + 1]), 11);
                    else
                        buffer.Put(Alphanumeric.IndexOf(s[i]), 6);
                }
                break;
            }
            case QrMode.Byte:
            case QrMode.Kanji:
                for (int i = 0; i < Data.Length; i++)
                    buffer.Put(Data[i], 8);
                break;
        }
    }

    /// <summary>数据内容占用的比特数（不含模式/长度头）。</summary>
    public int PayloadBits => Mode switch
    {
        QrMode.Numeric => (CharCount / 3) * 10 + (CharCount % 3 == 1 ? 4 : CharCount % 3 == 2 ? 7 : 0),
        QrMode.Alphanumeric => (CharCount / 2) * 11 + (CharCount % 2) * 6,
        _ => Data.Length * 8,
    };

    public override string ToString() => $"QrDataChunk({Mode}, {CharCount} chars, {Data.Length} bytes)";
}
