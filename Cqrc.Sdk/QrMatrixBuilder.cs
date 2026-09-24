using System.Text;

namespace Cqrc;

/// <summary>
/// 模块矩阵构建：固定图形（三个定位角、对齐图形、时序线、暗色模块、
/// 版本信息、格式信息）+ 之字形数据放置 + 8 种掩码按 ISO 罚分选出最优。
/// </summary>
public static class QrMatrixBuilder
{
    private static readonly Dictionary<int, QrMatrix> _blankCache = new();

    /// <summary>构建某版本的“空白”矩阵（固定图形，数据区未填）。</summary>
    public static QrMatrix BuildBlank(int version)
    {
        lock (_blankCache)
        {
            if (_blankCache.TryGetValue(version, out var cached))
                return cached.Clone();
        }

        var m = new QrMatrix(version);
        int n = m.ModulesCount;

        // 三个定位角（含分隔线）
        DrawFinder(m, 0, 0);
        DrawFinder(m, n - 7, 0);
        DrawFinder(m, 0, n - 7);

        // 对齐图形（跳过与定位角重叠的 3 处）
        var pos = QrTables.AlignmentPositions[version - 1];
        for (int i = 0; i < pos.Length; i++)
            for (int j = 0; j < pos.Length; j++)
            {
                int r = pos[i], c = pos[j];
                if ((r < 9 && c < 9) || (r < 9 && c >= n - 9) || (r >= n - 9 && c < 9)) continue;
                DrawAlignment(m, r, c);
            }

        // 时序线
        for (int i = 8; i < n - 8; i++)
        {
            m.Set(i, 6, i % 2 == 0);
            m.Set(6, i, i % 2 == 0);
        }

        // 版本信息（版本 ≥7）
        if (version >= 7)
        {
            int bits = QrBch.VersionBits(version);
            for (int i = 0; i < 18; i++)
            {
                bool dark = ((bits >> i) & 1) == 1;
                m.Set(i / 3, i % 3 + n - 8 - 3, dark);
                m.Set(i % 3 + n - 8 - 3, i / 3, dark);
            }
        }

        // 格式信息区 + 暗色模块：先以浅色占位（评分阶段与 python-qrcode 的 test=True 一致），
        // 数据放置时跳过，最后由 DrawFormatBits 写入真实位。
        foreach (var (row, col) in FormatCells(n))
            m.Set(row, col, false);
        m.Set(n - 8, 8, false);

        var clone = m.Clone();
        lock (_blankCache) _blankCache[version] = clone;
        return m;
    }

    private static void DrawFinder(QrMatrix m, int topRow, int leftCol)
    {
        int n = m.ModulesCount;
        for (int r = -1; r <= 7; r++)
        {
            int row = topRow + r;
            if (row < 0 || row >= n) continue;
            for (int c = -1; c <= 7; c++)
            {
                int col = leftCol + c;
                if (col < 0 || col >= n) continue;
                bool dark =
                    (r >= 0 && r <= 6 && (c == 0 || c == 6)) ||
                    (c >= 0 && c <= 6 && (r == 0 || r == 6)) ||
                    (r >= 2 && r <= 4 && c >= 2 && c <= 4);
                m.Set(row, col, dark);
            }
        }
    }

    private static void DrawAlignment(QrMatrix m, int row, int col)
    {
        for (int r = -2; r <= 2; r++)
            for (int c = -2; c <= 2; c++)
                m.Set(row + r, col + c, Math.Max(Math.Abs(r), Math.Abs(c)) != 1);
    }

    /// <summary>
    /// 格式信息的 30 个模块位，前 15 个为竖直区（列 8），后 15 个为水平区（行 8），
    /// 顺序与 ISO 的 bit0..bit14 一致。
    /// </summary>
    private static IEnumerable<(int row, int col)> FormatCells(int n)
    {
        for (int i = 0; i < 15; i++)
            yield return i < 6 ? (i, 8) : i < 8 ? (i + 1, 8) : (n - 15 + i, 8);
        for (int i = 0; i < 15; i++)
            yield return i < 8 ? (8, n - 1 - i) : i < 9 ? (8, 7) : (8, 15 - i - 1);
    }

    /// <summary>写入格式信息（两处镜像）与暗色模块。</summary>
    public static void DrawFormatBits(QrMatrix m, int formatBits15)
    {
        int n = m.ModulesCount;
        int i = 0;
        foreach (var (row, col) in FormatCells(n))
        {
            bool dark = ((formatBits15 >> (i % 15)) & 1) == 1;
            m.Set(row, col, dark);
            i++;
        }
        m.Set(n - 8, 8, true);
    }

    /// <summary>之字形放置数据码字并应用掩码（只写数据区，固定图形跳过）。逻辑与 python-qrcode map_data 一致。</summary>
    public static void MapData(QrMatrix m, byte[] codeWords, int maskPattern)
    {
        int n = m.ModulesCount;
        var mask = MaskFunctions.Create(maskPattern);
        int byteIndex = 0, bitIndex = 7;
        bool upward = true;

        for (int col = n - 1; col > 0; col -= 2)
        {
            int c = col <= 6 ? col - 1 : col; // 跳过中间时序列（列 6）
            int row = upward ? n - 1 : 0;
            int inc = upward ? -1 : 1;
            while (true)
            {
                for (int cc = 0; cc < 2; cc++)
                {
                    int x = c - cc;
                    if (m.Get(row, x) is not null) continue; // 固定图形区
                    bool dark = false;
                    if (byteIndex < codeWords.Length)
                    {
                        dark = ((codeWords[byteIndex] >> bitIndex) & 1) == 1;
                        bitIndex--;
                        if (bitIndex == -1) { bitIndex = 7; byteIndex++; }
                    }
                    if (mask(row, x)) dark = !dark;
                    m.Set(row, x, dark);
                }
                row += inc;
                if (row < 0 || row >= n) { row -= inc; inc = -inc; break; }
            }
            upward = !upward;
        }
    }

    /// <summary>8 种掩码函数（ISO 18004 表 10）。</summary>
    public static class MaskFunctions
    {
        public static Func<int, int, bool> Create(int pattern) => pattern switch
        {
            0 => (i, j) => (i + j) % 2 == 0,
            1 => (i, _) => i % 2 == 0,
            2 => (_, j) => j % 3 == 0,
            3 => (i, j) => (i + j) % 3 == 0,
            4 => (i, j) => (i / 2 + j / 3) % 2 == 0,
            5 => (i, j) => (i * j) % 2 + (i * j) % 3 == 0,
            6 => (i, j) => ((i * j) % 2 + (i * j) % 3) % 2 == 0,
            7 => (i, j) => ((i * j) % 3 + (i + j) % 2) % 2 == 0,
            _ => throw new ArgumentOutOfRangeException(nameof(pattern), "掩码 0-7"),
        };
    }

    /// <summary>
    /// 完整构建：空白矩阵 → 试算 8 种掩码 → 按 ISO 罚分取最优 → 写格式信息。
    /// </summary>
    public static QrMatrix Build(QrEncoder.Result encoded)
    {
        int bestMask = 0;
        int bestScore = int.MaxValue;

        for (int mask = 0; mask < 8; mask++)
        {
            var m = BuildBlank(encoded.Version);
            MapData(m, encoded.CodeWords, mask);
            int score = MaskScoring.LostPoint(m.ToArray());
            if (score < bestScore)
            {
                bestScore = score;
                bestMask = mask;
            }
        }

        var final = BuildBlank(encoded.Version);
        MapData(final, encoded.CodeWords, bestMask);
        DrawFormatBits(final, QrBch.FormatBits(encoded.Level, bestMask));
        final.MaskPattern = bestMask;
        return final;
    }
}
