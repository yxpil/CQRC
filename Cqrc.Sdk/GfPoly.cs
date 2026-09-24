namespace Cqrc;

/// <summary>GF(2^8) 多项式（最高次在前，自动去掉前导零）。</summary>
public sealed class GfPoly
{
    private readonly int[] _num;

    public GfPoly(ReadOnlySpan<int> num, int shift = 0)
    {
        int offset = 0;
        while (offset < num.Length && num[offset] == 0) offset++;
        var core = num.Slice(offset).ToArray();
        if (shift != 0)
        {
            var padded = new int[core.Length + shift];
            core.CopyTo(padded, 0);
            _num = padded;
        }
        else
        {
            _num = core;
        }
    }

    public int this[int i] => i < _num.Length ? _num[i] : 0;
    public int Length => _num.Length;
    public int[] Coefficients => (int[])_num.Clone();
    public bool IsZero => _num.Length == 0;

    public GfPoly Multiply(GfPoly other)
    {
        var result = new int[Length + other.Length - 1];
        for (int i = 0; i < Length; i++)
        {
            if (_num[i] == 0) continue;
            for (int j = 0; j < other.Length; j++)
            {
                if (other[j] == 0) continue;
                result[i + j] ^= Gf256.GExp(Gf256.GLog(_num[i]) + Gf256.GLog(other[j]));
            }
        }
        return new GfPoly(result, 0);
    }

    /// <summary>多项式取模（Reed-Solomon 求余式用）。</summary>
    public GfPoly Mod(GfPoly other)
    {
        // 去前导零
        int off = 0;
        while (off < _num.Length && _num[off] == 0) off++;
        var num = _num[off..];
        if (num.Length == 0) return new GfPoly(Array.Empty<int>(), 0);
        if (num.Length < other.Length) return new GfPoly(num, 0);

        int ratio = Gf256.GLog(num[0]) - Gf256.GLog(other[0]);
        var result = new int[num.Length];
        for (int i = 0; i < other.Length; i++)
            result[i] = num[i] ^ Gf256.GExp(Gf256.GLog(other[i]) + ratio);
        for (int i = other.Length; i < num.Length; i++)
            result[i] = num[i];
        return new GfPoly(result, 0).Mod(other);
    }

    public override string ToString() => IsZero ? "0" : string.Join(", ", _num);
}
