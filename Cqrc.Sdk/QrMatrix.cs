namespace Cqrc;

/// <summary>
/// 模块矩阵。true=深色，false=浅色，null=未填充（数据区待写入）。
/// </summary>
public sealed class QrMatrix
{
    private readonly bool?[,] _modules;

    public int Version { get; }
    public int ModulesCount { get; }
    public int MaskPattern { get; set; }

    public QrMatrix(int version)
    {
        Version = version;
        ModulesCount = version * 4 + 17;
        _modules = new bool?[ModulesCount, ModulesCount];
    }

    public bool? Get(int row, int col) => _modules[row, col];
    public void Set(int row, int col, bool value) => _modules[row, col] = value;

    /// <summary>转为 bool 矩阵（null 视为浅色）。</summary>
    public bool[,] ToArray()
    {
        var result = new bool[ModulesCount, ModulesCount];
        for (int r = 0; r < ModulesCount; r++)
            for (int c = 0; c < ModulesCount; c++)
                result[r, c] = _modules[r, c] ?? false;
        return result;
    }

    internal QrMatrix Clone()
    {
        var m = new QrMatrix(Version);
        for (int r = 0; r < ModulesCount; r++)
            for (int c = 0; c < ModulesCount; c++)
                m._modules[r, c] = _modules[r, c];
        m.MaskPattern = MaskPattern;
        return m;
    }
}
