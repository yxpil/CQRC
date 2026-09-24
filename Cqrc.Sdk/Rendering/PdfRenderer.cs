using System.Text;
using Cqrc;

namespace Cqrc.Rendering;

/// <summary>
/// PDF 渲染器：输出单页 PDF，二维码为矢量矩形（可任意缩放印刷）。
/// 手写最小 PDF 结构（xref + 对象），无第三方依赖。
/// </summary>
public sealed class PdfRenderer
{
    public void Save(CqrcCode code, string path, int boxSize = 10)
        => File.WriteAllBytes(path, Render(code, boxSize));

    public byte[] Render(CqrcCode code, int boxSize = 10)
    {
        bool[,] full = code.FullMatrix();
        int n = full.GetLength(0);
        int border = code.Border;
        int size = n * boxSize;
        var fg = Rgba.Parse(code.Style.ForegroundColor);
        var bg = Rgba.Parse(code.Style.BackgroundColor);
        bool circularFinders = code.Style.CircularFinders;
        var corners = new (int, int)[]
        {
            (border, border),
            (n - border - 7, border),
            (border, n - border - 7),
        };
        bool InFinder(int r, int c)
        {
            foreach (var (cr, cc) in corners)
                if (Math.Abs(r - cr) < 7 && Math.Abs(c - cc) < 7) return true;
            return false;
        }

        var stream = new StringBuilder();
        stream.AppendLine($"1 0 0 {size} 0 0 cm");
        stream.AppendLine($"0 {bg.G / 255.0:0.00} {bg.B / 255.0:0.00} rg");
        stream.AppendLine($"0 0 {size} {size} re f");
        stream.AppendLine($"1 0 0 -1 0 0 cm"); // 翻转 y 轴
        stream.AppendLine($"0 {fg.G / 255.0:0.00} {fg.B / 255.0:0.00} rg");
        var rects = new StringBuilder();
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                if (!full[r, c]) continue;
                if (circularFinders && InFinder(r, c)) continue; // 由圆形替代
                int x = c * boxSize, y = r * boxSize;
                rects.Append($"{x} {y} {boxSize} {boxSize} re f\n");
            }
        stream.Append(rects.ToString());
        if (circularFinders)
        {
            // 圆形定位区：外圆（前景）+ 中间圆（背景）+ 中心圆（前景），贝塞尔近似
            void Circle(int cx, int cy, int r, bool bgFill)
            {
                if (bgFill)
                    stream.AppendLine($"0 {bg.G / 255.0:0.00} {bg.B / 255.0:0.00} rg");
                else
                    stream.AppendLine($"0 {fg.G / 255.0:0.00} {fg.B / 255.0:0.00} rg");
                double k = 0.5523 * r;
                stream.AppendLine($"{cx + r} {cy} m");
                stream.AppendLine($"{cx + r} {cy + k:0.00} {cx + k:0.00} {cy + r} {cx} {cy + r} c");
                stream.AppendLine($"{cx - k:0.00} {cy + r} {cx - r} {cy + k:0.00} {cx - r} {cy} c");
                stream.AppendLine($"{cx - r} {cy - k:0.00} {cx - k:0.00} {cy - r} {cx} {cy - r} c");
                stream.AppendLine($"{cx + k:0.00} {cy - r} {cx + r} {cy - k:0.00} {cx + r} {cy} c");
                stream.AppendLine("h f");
            }
            foreach (var (cr, cc) in corners)
            {
                int cx = (cc + 3) * boxSize + boxSize / 2;
                int cy = (cr + 3) * boxSize + boxSize / 2;
                Circle(cx, cy, 3 * boxSize / 2 + boxSize / 2, false);
                Circle(cx, cy, 2 * boxSize + boxSize / 2, true);
                Circle(cx, cy, boxSize + boxSize / 2, false);
            }
        }
        var streamBytes = Encoding.ASCII.GetBytes(stream.ToString());

        // 组装 PDF
        var pdf = new MemoryStream();
        void WriteAscii(string s) { var b = Encoding.ASCII.GetBytes(s); pdf.Write(b); }

        WriteAscii("%PDF-1.4\n");
        int obj1Start = (int)pdf.Length;
        WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        int obj2Start = (int)pdf.Length;
        WriteAscii($"2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        int obj3Start = (int)pdf.Length;
        WriteAscii($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {size} {size}] " +
                   "/Contents 4 0 R /Resources << >> >>\nendobj\n");
        int obj4Start = (int)pdf.Length;
        WriteAscii($"4 0 obj\n<< /Length {streamBytes.Length} >>\nstream\n");
        pdf.Write(streamBytes);
        WriteAscii("\nendstream\nendobj\n");

        long xrefPos = pdf.Length;
        WriteAscii("xref\n0 5\n");
        WriteAscii("0000000000 65535 f \n");
        WriteAscii($"{obj1Start:D10} 00000 n \n");
        WriteAscii($"{obj2Start:D10} 00000 n \n");
        WriteAscii($"{obj3Start:D10} 00000 n \n");
        WriteAscii($"{obj4Start:D10} 00000 n \n");
        WriteAscii("trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n");
        WriteAscii($"{xrefPos}\n");
        WriteAscii("%%EOF");
        return pdf.ToArray();
    }
}
