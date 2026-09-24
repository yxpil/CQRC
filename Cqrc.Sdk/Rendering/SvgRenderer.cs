using System.Text;
using Cqrc;

namespace Cqrc.Rendering;

/// <summary>
/// SVG 渲染器：矢量彩色二维码（可无限放大、可换品牌色）。
/// </summary>
public sealed class SvgRenderer
{
    public string Render(CqrcCode code, int boxSize = 10)
    {
        var style = code.Style;
        bool[,] full = code.FullMatrix();
        int n = full.GetLength(0);
        int border = code.Border;
        int size = n * boxSize;
        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 {size} {size}\">");

        // 渐变定义
        bool gradient = style.ForegroundColor2 != null;
        if (gradient)
        {
            sb.AppendLine($"  <defs><linearGradient id=\"fg\" x1=\"0%\" y1=\"0%\" x2=\"100%\" y2=\"100%\">");
            sb.AppendLine($"    <stop offset=\"0%\" stop-color=\"{style.ForegroundColor}\"/>");
            sb.AppendLine($"    <stop offset=\"100%\" stop-color=\"{style.ForegroundColor2}\"/>");
            sb.AppendLine("  </linearGradient></defs>");
        }

        sb.AppendLine($"  <rect width=\"{size}\" height=\"{size}\" fill=\"{style.BackgroundColor}\"/>");
        var color = gradient ? "url(#fg)" : style.ForegroundColor;
        sb.AppendLine($"  <g fill=\"{color}\">");
        // 圆形定位标记：三个 7x7 区域的外圆环 + 中心圆
        var corners = new (int, int)[]
        {
            (border, border),
            (n - border - 7, border),
            (border, n - border - 7),
        };
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                if (!full[r, c]) continue;
                if (style.CircularFinders)
                {
                    bool inFinder = false;
                    foreach (var (cr, cc) in corners)
                        if (Math.Abs(r - cr) < 7 && Math.Abs(c - cc) < 7) { inFinder = true; break; }
                    if (inFinder) continue; // 由圆形绘制
                }
                int x = c * boxSize, y = r * boxSize;
                if (style.ModuleRounding > 0.001)
                    sb.AppendLine($"    <rect x=\"{x}\" y=\"{y}\" width=\"{boxSize}\" height=\"{boxSize}\" rx=\"{(int)(style.ModuleRounding * boxSize)}\"/>");
                else
                    sb.AppendLine($"    <rect x=\"{x}\" y=\"{y}\" width=\"{boxSize}\" height=\"{boxSize}\"/>");
            }
        if (style.CircularFinders)
        {
            foreach (var (cr, cc) in corners)
            {
                double cx = (cc + 3.5) * boxSize, cy = (cr + 3.5) * boxSize;
                sb.AppendLine($"    <circle cx=\"{cx}\" cy=\"{cy}\" r=\"{3.5 * boxSize}\"/>");
                sb.AppendLine($"    <circle cx=\"{cx}\" cy=\"{cy}\" r=\"{2.5 * boxSize}\" fill=\"{style.BackgroundColor}\" stroke=\"none\"/>");
                sb.AppendLine($"    <circle cx=\"{cx}\" cy=\"{cy}\" r=\"{1.5 * boxSize}\" fill=\"{color}\"/>");
            }
        }
        sb.AppendLine("  </g>");
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    public void Save(CqrcCode code, string path, int boxSize = 10)
        => File.WriteAllText(path, Render(code, boxSize), Encoding.UTF8);
}
