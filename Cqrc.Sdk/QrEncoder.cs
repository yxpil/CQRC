namespace Cqrc;

/// <summary>
/// 核心编码器：数据段 → 比特流（模式+长度+数据 → 终止位 → 字节对齐 → 0xEC/0x11 填充）
/// → 按 RS_BLOCK_TABLE 分块做 Reed-Solomon 纠错 → 数据/纠错交错。
/// 输出即 ISO/IEC 18004 编码后的码字流。
/// </summary>
public static class QrEncoder
{
    public const int Pad0 = 0xEC;
    public const int Pad1 = 0x11;

    public sealed record Result(
        int Version,
        ErrorCorrectionLevel Level,
        IReadOnlyList<QrDataChunk> Chunks,
        byte[] CodeWords,
        List<RsBlock> Blocks,
        int PayloadBits);

    /// <summary>找能装下数据的最小版本（1..40）。</summary>
    public static int BestVersion(IReadOnlyList<QrDataChunk> chunks, ErrorCorrectionLevel level, int start = 1)
    {
        for (int v = start; v <= 40; v++)
        {
            int needed = chunks.Sum(c => 4 + QrTables.LengthBits(c.Mode, v) + c.PayloadBits);
            if (needed <= QrTables.DataBitCapacity(v, level)) return v;
        }
        throw new QrDataOverflowException($"数据过大：无法装入 QR 版本 40（纠错等级 {level}）");
    }

    /// <summary>完整编码：数据段 → 交错后的码字序列。</summary>
    public static Result Encode(IReadOnlyList<QrDataChunk> chunks, ErrorCorrectionLevel level, int? version = null)
    {
        if (chunks.Count == 0)
            throw new ArgumentException("数据不能为空", nameof(chunks));

        int v = version ?? BestVersion(chunks, level);
        QrTables.CheckVersion(v);
        if (version is int fixedVersion && fixedVersion < BestVersion(chunks, level))
            throw new QrDataOverflowException($"数据需要版本 {BestVersion(chunks, level)}，超出指定的版本 {fixedVersion}");

        int bitLimit = QrTables.DataBitCapacity(v, level);
        var buffer = new BitBuffer();
        foreach (var c in chunks)
            c.Write(buffer, v);
        int payloadBits = buffer.BitLength;

        // 1) 终止符：最多 4 个 0
        for (int i = 0; i < Math.Min(bitLimit - buffer.BitLength, 4); i++)
            buffer.PutBit(false);
        // 2) 字节对齐
        int rem = buffer.BitLength % 8;
        if (rem != 0)
            for (int i = rem; i < 8; i++)
                buffer.PutBit(false);
        // 3) 填充字节 0xEC/0x11 交替
        int fill = (bitLimit - buffer.BitLength) / 8;
        for (int i = 0; i < fill; i++)
            buffer.Put(i % 2 == 0 ? Pad0 : Pad1, 8);

        var bytes = buffer.ToBytes();
        if (bytes.Length != bitLimit / 8)
            throw new InvalidOperationException($"内部错误：缓冲 {bytes.Length} 字节 != 容量 {bitLimit / 8}");

        // 4) RS 分块 + 纠错 + 交错
        var blocks = QrTables.RsBlocks(v, level);
        var pairs = new List<(byte[], byte[])>(blocks.Count);
        int offset = 0;
        foreach (var b in blocks)
        {
            var data = bytes.AsSpan(offset, b.DataCount).ToArray();
            offset += b.DataCount;
            pairs.Add((data, ReedSolomon.Encode(data, b.TotalCount - b.DataCount)));
        }
        if (offset != bytes.Length)
            throw new InvalidOperationException("内部错误：RS 块总容量与数据不符");

        return new Result(v, level, chunks, ReedSolomon.Interleave(pairs), blocks, payloadBits);
    }
}

/// <summary>数据超出 QR 容量。</summary>
public sealed class QrDataOverflowException : Exception
{
    public QrDataOverflowException(string message) : base(message) { }
}
