using System.Text;
using Cqrc;

using Cqrc.Metadata;

namespace Cqrc;

/// <summary>
/// CQRC 样式描述：颜色、圆角、Logo、背景图、元数据（ISO/IEC 21570）。
/// 这是“彩色二维码”的核心：除了黑白色块，还承载品牌色、Logo 与扩展信息。
/// </summary>
public sealed class QrStyle
{
    /// <summary>深色模块颜色（默认 #1A1A1A）。</summary>
    public string ForegroundColor { get; set; } = "#1A1A1A";

    /// <summary>浅色背景颜色（默认 #FFFFFF）。</summary>
    public string BackgroundColor { get; set; } = "#FFFFFF";

    /// <summary>深色渐变的第二种颜色（可选，渲染为线性渐变）。</summary>
    public string? ForegroundColor2 { get; set; }

    /// <summary>渐变方向角度（0=左→右，90=上→下）。</summary>
    public double GradientAngle { get; set; } = 0;

    /// <summary>模块圆角比例 0~0.5（0=直角）。</summary>
    public double ModuleRounding { get; set; } = 0;

    /// <summary>用圆形替代三个角的方形定位标记（圆形定位区）。</summary>
    public bool CircularFinders { get; set; } = false;

    /// <summary>中央 Logo（PNG/JPEG 字节）。覆盖中心约 12~15% 区域，建议 H 级纠错。</summary>
    public byte[]? Logo { get; set; }

    /// <summary>Logo 占 QR 宽度比例（0~1）。</summary>
    public double LogoRatio { get; set; } = 0.15;

    /// <summary>Logo 周围的白色垫层（像素，按 boxSize 比例）。</summary>
    public double LogoPadding { get; set; } = 0.03;

    /// <summary>整码圆角（占宽度比例 0~0.5），0 表示不圆角。</summary>
    public double FrameRounding { get; set; } = 0;

    /// <summary>ISO/IEC 21570 扩展元数据（JSON 键值，UTF-8）。</summary>
    public Dictionary<string, string?>? Metadata { get; set; }

    /// <summary>内容哈希（SHA-256），自动写入元数据 content_hash。</summary>
    public bool IncludeContentHash { get; set; } = true;
}

/// <summary>
/// CQRC：从零实现的彩色二维码。
/// 流程：数据段（自动选模式）→ RS 纠错 → 矩阵构建（8 掩码评分）
///       → 可选 ISO/IEC 21570 元数据段 → 按 QrStyle 渲染（PNG/SVG/PDF/位图）。
/// </summary>
public sealed class CqrcCode
{
    public ErrorCorrectionLevel ErrorCorrection { get; set; } = ErrorCorrectionLevel.M;
    public QrStyle Style { get; } = new();
    public int? Version { get; set; }

    private readonly List<string> _texts = new();

    /// <summary>追加要编码的文本（多段会顺序拼接为多个数据段）。</summary>
    public void AddData(string text)
    {
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("数据不能为空", nameof(text));
        _texts.Add(text);
    }

    /// <summary>编码后的模块矩阵（不含白边）。</summary>
    public QrMatrix? Matrix { get; private set; }

    /// <summary>实际选定的 QR 版本。</summary>
    public int ActualVersion { get; private set; }

    /// <summary>选定的掩码（0-7）。</summary>
    public int MaskPattern => Matrix.MaskPattern;

    /// <summary>完整生成：数据段 + 21570 段 + 纠错 + 矩阵。</summary>
    public QrMatrix Generate()
    {
        if (_texts.Count == 0) throw new InvalidOperationException("没有数据，先 AddData()");

        var chunks = new List<QrDataChunk>();
        string full = string.Concat(_texts);
        if (Style.IncludeContentHash && (Style.Metadata is null || !Style.Metadata.ContainsKey("content_hash")))
        {
            if (Style.Metadata is null) Style.Metadata = new Dictionary<string, string?>();
            Style.Metadata["content_hash"] = Iso21570.ContentHash(full);
        }
        foreach (var t in _texts)
            chunks.Add(QrDataChunk.Auto(t));

        // 21570 元数据段（必须最后）
        if (Style.Metadata is { Count: > 0 })
        {
            var segment = Iso21570.BuildSegment(Style.Metadata);
            chunks.Add(new QrDataChunk(QrMode.Byte, segment));
        }

        var encoded = QrEncoder.Encode(chunks, ErrorCorrection, Version);
        Matrix = QrMatrixBuilder.Build(encoded);
        ActualVersion = encoded.Version;
        return Matrix;
    }

    /// <summary>白边模块数（ISO 建议 ≥4）。</summary>
    public int Border { get; set; } = 4;

    /// <summary>含白边的完整矩阵（false=白）。</summary>
    public bool[,] FullMatrix()
    {
        var m = Generate().ToArray();
        int n = m.GetLength(0);
        int w = n + Border * 2;
        var full = new bool[w, w];
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                full[r + Border, c + Border] = m[r, c];
        return full;
    }
}
