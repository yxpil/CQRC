namespace Cqrc;

/// <summary>
/// GF(2^8) 伽罗瓦域运算，本原多项式 x^8+x^4+x^3+x^2+1（0x11D）。
/// 表在首次使用时计算（算法与 python-qrcode 的 EXP/LOG 表一致），无第三方依赖。
/// </summary>
public static class Gf256
{
    private static readonly int[] ExpTable = ComputeExp();
    private static readonly int[] LogTable = ComputeLog(ExpTable);

    private static int[] ComputeExp()
    {
        var table = new int[512];
        int x = 1;
        for (int i = 0; i < 512; i++)
        {
            table[i] = x;
            x <<= 1;
            if ((x & 0x100) != 0) x ^= 0x11D;
        }
        return table;
    }

    private static int[] ComputeLog(int[] exp)
    {
        var table = new int[256];
        for (int i = 0; i < 512; i++) table[exp[i]] = i % 255;
        return table;
    }

    /// <summary>对数（调用方应保证 n≠0）。</summary>
    public static int GLog(int n) => LogTable[n];

    /// <summary>指数，自动 mod 255（可传大于 255 的值）。</summary>
    public static int GExp(int n) => ExpTable[n % 255];
}
