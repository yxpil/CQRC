namespace Cqrc;

/// <summary>
/// 掩码选择评分（ISO/IEC 18004 表 20）。
/// 模块数组中：true=深色，false=浅色，null 之外的固定区已包含。
/// </summary>
public static class MaskScoring
{
    /// <summary>
    /// 对给定模块矩阵（含固定图形与数据区，已应用某掩码）计算罚分。
    /// 分数越低越优。
    /// </summary>
    public static int LostPoint(bool[,] modules)
    {
        int count = GetCount(modules);
        return Level1(modules, count) + Level2(modules, count) + Level3(modules, count) + Level4(modules, count);
    }

    private static int GetCount(bool[,] m) => m.GetLength(0);

    /// <summary>规则 1：行/列连续同色 ≥5 个，每段 +3，每多 1 个 +1。</summary>
    private static int Level1(bool[,] m, int count)
    {
        int lost = 0;
        int[] buckets = new int[count + 1];

        for (int row = 0; row < count; row++)
        {
            bool prev = m[row, 0];
            int len = 0;
            for (int col = 0; col < count; col++)
            {
                if (m[row, col] == prev) len++;
                else
                {
                    if (len >= 5) buckets[len]++;
                    len = 1; prev = m[row, col];
                }
            }
            if (len >= 5) buckets[len]++;
        }
        for (int col = 0; col < count; col++)
        {
            bool prev = m[0, col];
            int len = 0;
            for (int row = 0; row < count; row++)
            {
                if (m[row, col] == prev) len++;
                else
                {
                    if (len >= 5) buckets[len]++;
                    len = 1; prev = m[row, col];
                }
            }
            if (len >= 5) buckets[len]++;
        }
        for (int len = 5; len <= count; len++)
            lost += buckets[len] * (len - 2);
        return lost;
    }

    /// <summary>
    /// 规则 2：2x2 同色块 +3。沿用 python-qrcode 的跳格优化（右上不同则跳过下一列），
    /// 以保持与其掩码选择逐位一致。
    /// </summary>
    private static int Level2(bool[,] m, int count)
    {
        int lost = 0;
        for (int row = 0; row < count - 1; row++)
        {
            for (int col = 0; col < count - 1; col++)
            {
                bool topRight = m[row, col + 1];
                if (topRight != m[row + 1, col + 1]) col++;          // 跳过下一列：abcd 与 abef 均不计分
                else if (topRight != m[row, col]) continue;
                else if (topRight != m[row + 1, col]) continue;
                else lost += 3;
            }
        }
        return lost;
    }

    // python-qrcode lost_point_level3 的两个 11 模块窗口（深色=1）：
    //   pattern1 = 10111010000（右侧带 4 格浅色）
    //   pattern2 = 00001011101（左侧带 4 格浅色）
    private static readonly bool[] Pattern1 = { true, false, true, true, true, false, true, false, false, false, false };
    private static readonly bool[] Pattern2 = { false, false, false, false, true, false, true, true, true, false, true };

    /// <summary>规则 3：1:1:3:1:1 图形 +40（含 python-qrcode 的 Horspool 跳格）。</summary>
    private static int Level3(bool[,] m, int count)
    {
        int lost = 0;
        for (int row = 0; row < count; row++)
            for (int col = 0; col <= count - 11; col++)
            {
                if (Matches(m, row, col, true)) lost += 40;
                if (m[row, col + 10]) col++; // 末位深色时两种图形至少偏移 2，可跳 1 格
            }
        for (int col = 0; col < count; col++)
            for (int row = 0; row <= count - 11; row++)
            {
                if (Matches(m, row, col, false)) lost += 40;
                if (m[row + 10, col]) row++;
            }
        return lost;
    }

    private static bool Matches(bool[,] m, int row, int col, bool horizontal)
    {
        bool V(int offset) => horizontal ? m[row, col + offset] : m[row + offset, col];
        for (int p = 0; p < 2; p++)
        {
            var pattern = p == 0 ? Pattern1 : Pattern2;
            bool ok = true;
            for (int i = 0; i < 11; i++)
                if (V(i) != pattern[i]) { ok = false; break; }
            if (ok) return true;
        }
        return false;
    }

    /// <summary>规则 4：深色占比偏离 50% 每 5% +10（浮点占比，与 python-qrcode 一致）。</summary>
    private static int Level4(bool[,] m, int count)
    {
        int dark = 0;
        for (int row = 0; row < count; row++)
            for (int col = 0; col < count; col++)
                if (m[row, col]) dark++;
        double percent = (double)dark / (count * count) * 100;
        return (int)(Math.Abs(percent - 50) / 5) * 10;
    }
}
