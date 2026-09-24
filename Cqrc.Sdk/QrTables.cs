namespace Cqrc;

/// <summary>
/// ISO/IEC 18004 静态表：RS 块（QrRsTable）、对齐图形位置、BCH 多项式、长度字段位数。
/// </summary>
public static class QrTables
{
    private static readonly Dictionary<ErrorCorrectionLevel, int> LevelOffset = new()
    {
        [ErrorCorrectionLevel.L] = 0,
        [ErrorCorrectionLevel.M] = 1,
        [ErrorCorrectionLevel.Q] = 2,
        [ErrorCorrectionLevel.H] = 3,
    };

    /// <summary>取版本+纠错等级对应的 RS 块列表。</summary>
    public static List<RsBlock> RsBlocks(int version, ErrorCorrectionLevel level)
    {
        CheckVersion(version);
        var row = QrRsTable.Rows[(version - 1) * 4 + LevelOffset[level]];
        var blocks = new List<RsBlock>();
        for (int i = 0; i < row.Length; i += 3)
        {
            int count = row[i], total = row[i + 1], data = row[i + 2];
            for (int k = 0; k < count; k++) blocks.Add(new RsBlock(total, data));
        }
        return blocks;
    }

    /// <summary>版本可容纳的数据比特上限。</summary>
    public static int DataBitCapacity(int version, ErrorCorrectionLevel level)
        => RsBlocks(version, level).Sum(b => b.DataCount * 8);

    /// <summary>版本信息 BCH(18,6) 生成多项式 G18 = 0x1F25。</summary>
    public const int G18 = (1 << 12) | (1 << 11) | (1 << 10) | (1 << 9) | (1 << 8) | (1 << 5) | (1 << 2) | (1 << 0);

    /// <summary>格式信息 BCH(15,5) 生成多项式 G15 = 0x7536。</summary>
    public const int G15 = (1 << 10) | (1 << 8) | (1 << 5) | (1 << 4) | (1 << 2) | (1 << 1) | (1 << 0);

    /// <summary>格式信息固定异或掩码 0x5412。</summary>
    public const int FormatMask = (1 << 14) | (1 << 12) | (1 << 10) | (1 << 4) | (1 << 1);

    /// <summary>对齐图形位置表（索引 0 = 版本 1）。</summary>
    public static readonly int[][] AlignmentPositions =
    {
        new int[0],
        new[]{6,18}, new[]{6,22}, new[]{6,26}, new[]{6,30}, new[]{6,34}, new[]{6,22,38}, new[]{6,24,42}, new[]{6,26,46}, new[]{6,28,50},
        new[]{6,30,54}, new[]{6,32,58}, new[]{6,34,62}, new[]{6,26,46,66}, new[]{6,26,48,70}, new[]{6,26,50,74}, new[]{6,30,54,78},
        new[]{6,30,56,82}, new[]{6,30,58,86}, new[]{6,34,62,90}, new[]{6,28,50,72,94}, new[]{6,26,50,74,98}, new[]{6,30,54,78,102},
        new[]{6,28,54,80,106}, new[]{6,32,58,84,110}, new[]{6,30,58,86,114}, new[]{6,34,62,90,118}, new[]{6,26,50,74,98,122},
        new[]{6,30,54,78,102,126}, new[]{6,26,52,78,104,130}, new[]{6,30,56,82,108,134}, new[]{6,34,60,86,112,138},
        new[]{6,30,58,86,114,142}, new[]{6,34,62,90,118,146}, new[]{6,30,54,78,102,126,150}, new[]{6,24,50,76,102,128,154},
        new[]{6,28,54,80,106,132,158}, new[]{6,32,58,84,110,136,162}, new[]{6,26,54,82,110,138,166}, new[]{6,30,58,86,114,142,170},
    };

    /// <summary>数据长度字段位数（随版本变化，ISO 18004 表 2）。</summary>
    public static int LengthBits(QrMode mode, int version)
    {
        CheckVersion(version);
        if (version < 10)
            return mode switch { QrMode.Numeric => 10, QrMode.Alphanumeric => 9, QrMode.Byte => 8, QrMode.Kanji => 8, _ => throw new ArgumentOutOfRangeException(nameof(mode)) };
        if (version < 27)
            return mode switch { QrMode.Numeric => 12, QrMode.Alphanumeric => 11, QrMode.Byte => 16, QrMode.Kanji => 10, _ => throw new ArgumentOutOfRangeException(nameof(mode)) };
        return mode switch { QrMode.Numeric => 14, QrMode.Alphanumeric => 13, QrMode.Byte => 16, QrMode.Kanji => 12, _ => throw new ArgumentOutOfRangeException(nameof(mode)) };
    }

    public static void CheckVersion(int version)
    {
        if (version is < 1 or > 40)
            throw new ArgumentOutOfRangeException(nameof(version), $"无效版本 {version}（QR 1-40）");
    }
}
