

namespace Cqrc;

/// <summary>BCH(15,5)/(18,6) 校验计算：格式信息与版本信息。</summary>
public static class QrBch
{
    /// <summary>
    /// 格式信息 15 bit：(EC 级别 2bit &lt;&lt; 3 | 掩码 3bit) 经 BCH 编码后异或掩码 0x5412。
    /// </summary>
    public static int FormatBits(ErrorCorrectionLevel level, int mask)
    {
        if (mask is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(mask), "掩码 0-7");
        int ecBits = level switch
        {
            ErrorCorrectionLevel.L => 0b01,
            ErrorCorrectionLevel.M => 0b00,
            ErrorCorrectionLevel.Q => 0b11,
            ErrorCorrectionLevel.H => 0b10,
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };
        int data = (ecBits << 3) | mask;
        return Bch15(data);
    }

    /// <summary>BCH(15,5)：生成多项式 G15 = x^10+x^8+x^5+x^4+x^2+x+1，结果 XOR 0x5412。</summary>
    private static int Bch15(int data)
    {
        int d = data << 10;
        while (BitLength(d) - BitLength(QrTables.G15) >= 0)
            d ^= QrTables.G15 << (BitLength(d) - BitLength(QrTables.G15));
        return ((data << 10) | d) ^ QrTables.FormatMask;
    }

    /// <summary>
    /// 版本信息 18 bit（版本 ≥7）：(version 6bit) 经 BCH(18,6) 编码，G18 = 0x1F25。
    /// </summary>
    public static int VersionBits(int version)
    {
        QrTables.CheckVersion(version);
        if (version < 7) return 0;
        int d = version << 12;
        while (BitLength(d) - BitLength(QrTables.G18) >= 0)
            d ^= QrTables.G18 << (BitLength(d) - BitLength(QrTables.G18));
        return (version << 12) | d;
    }

    internal static int BitLength(int x)
    {
        int n = 0;
        while (x != 0) { n++; x >>= 1; }
        return n;
    }
}
