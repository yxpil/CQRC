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

    /// <summary>规则 2：2x2 同色块 +3。</summary>
    private static int Level2(bool[,] m, int count)
    {
        int lost = 0;
        for (int row = 0; row < count - 1; row++)
        {
            for (int col = 0; col < count - 1; col++)
            {
                bool v = m[row, col];
                if (v == m[row, col + 1] && v == m[row + 1, col] && v == m[row + 1, col + 1])
                    lost += 3;
            }
        }
        return lost;
    }

    // 1:1:3:1:1 比例图形（前后 4 个浅色）+40。两个方向：
    //   000010111010000 与 000010111010000 的反色（含 111101000101111 形式）
    private static readonly bool[] PatternA = { true, false, true, true, true, false, true, false, false, false, false };
    private static readonly bool[] PatternB = { false, false, false, false, true, false, true, true, true, false, true };

    /// <summary>规则 3：finder 类 1:1:3:1:1 图形 +40。</summary>
    private static int Level3(bool[,] m, int count)
    {
        int lost = 0;
        for (int row = 0; row < count; row++)
            for (int col = 0; col <= count - 11; col++)
                lost += MatchPattern(m, row, col, horizontal: true) ? 40 : 0;
        for (int col = 0; col < count; col++)
            for (int row = 0; row <= count - 11; row++)
                lost += MatchPattern(m, row, col, horizontal: false) ? 40 : 0;
        return lost;
    }

    private static bool MatchPattern(bool[,] m, int row, int col, bool horizontal)
    {
        bool v(int r, int c) => horizontal ? m[r, c] : m[c, r];
        for (int p = 0; p < 2; p++)
        {
            var pattern = p == 0 ? PatternA : PatternB;
            bool ok = true;
            for (int i = 0; i < 11; i++)
            {
                int r = horizontal ? row : row + i;
                int c = horizontal ? col + i : col;
                if (v(r, c) != pattern[i]) { ok = false; break; }
            }
            if (ok) return true;
        }
        return false;
    }

    /// <summary>规则 4：深色占比偏离 50% 每 5% +10。</summary>
    private static int Level4(bool[,] m, int count)
    {
        int dark = 0;
        for (int row = 0; row < count; row++)
            for (int col = 0; col < count; col++)
                if (m[row, col]) dark++;
        int percent = dark * 100 / (count * count);
        return (int)Math.Abs(percent - 50) / 5 * 10;
    }
}
