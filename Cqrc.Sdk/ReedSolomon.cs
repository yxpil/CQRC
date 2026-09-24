namespace Cqrc;

/// <summary>
/// Reed-Solomon 纠错编码器（GF(256)，本原多项式 0x11D）。
/// 生成多项式 g(x) = ∏(x − α^i)，i=0..ecCount-1（首一多项式）。
/// </summary>
public static class ReedSolomon
{
    private static readonly Dictionary<int, GfPoly> _genPolyCache = new();

    /// <summary>取纠错生成多项式（带缓存）。</summary>
    public static GfPoly Generator(int ecCount)
    {
        if (_genPolyCache.TryGetValue(ecCount, out var cached)) return cached;
        var poly = new GfPoly([1], 0);
        for (int i = 0; i < ecCount; i++)
            poly = poly.Multiply(new GfPoly(new[] { 1, Gf256.GExp(i) }, 0));
        _genPolyCache[ecCount] = poly;
        return poly;
    }

    /// <summary>为 data 生成 ecCount 个纠错字节。</summary>
    public static byte[] Encode(byte[] data, int ecCount)
    {
        if (ecCount <= 0) return Array.Empty<byte>();
        var ints = data.Select(b => (int)b).ToArray();
        var gen = Generator(ecCount);
        var raw = new GfPoly(ints.AsSpan(), gen.Length - 1);
        var mod = raw.Mod(gen);
        var result = new byte[ecCount];
        int offset = mod.Length - ecCount;
        for (int i = 0; i < ecCount; i++)
            result[i] = (byte)mod[i + offset];
        return result;
    }

    /// <summary>
    /// 按 ISO 18004 交错：先交错所有 RS 块的数据字节，再交错纠错字节。
    /// </summary>
    public static byte[] Interleave(IReadOnlyList<(byte[] Data, byte[] Ec)> blocks)
    {
        if (blocks.Count == 0) return Array.Empty<byte>();
        int maxData = blocks.Max(b => b.Data.Length);
        int maxEc = blocks.Max(b => b.Ec.Length);

        var output = new List<byte>(maxData * blocks.Count + maxEc * blocks.Count);
        for (int i = 0; i < maxData; i++)
            foreach (var (data, _) in blocks)
                if (i < data.Length) output.Add(data[i]);
        for (int i = 0; i < maxEc; i++)
            foreach (var (_, ec) in blocks)
                if (i < ec.Length) output.Add(ec[i]);
        return output.ToArray();
    }

    /// <summary>验证：合成多项式 data+ec 能被生成多项式整除（余式为 0）。</summary>
    public static bool Verify(byte[] data, byte[] ec)
    {
        if (ec.Length == 0) return true;
        var all = data.Concat(ec).Select(b => (int)b).ToArray();
        var poly = new GfPoly(all, 0);
        return poly.Mod(Generator(ec.Length)).IsZero;
    }
}
