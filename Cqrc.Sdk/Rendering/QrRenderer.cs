using System.Text;
using System.IO.Compression;
using Cqrc;

namespace Cqrc.Rendering;

/// <summary>
/// 彩色渲染器：把 CqrcCode 的模块矩阵按 QrStyle 画到 PngRenderTarget。
/// 支持：纯色/线性渐变前景、模块圆角、整码圆角、中央 Logo 叠加。
/// 不依赖任何第三方图像库（Logo 需要 PNG 解析，见 PngDecoder）。
/// </summary>
public static class QrRenderer
{
    public static PngRenderTarget Render(CqrcCode code, int boxSize = 10)
    {
        var style = code.Style;
        bool[,] full = code.FullMatrix();
        int border = code.Border;
        int n = full.GetLength(0);
        int size = n * boxSize;

        var target = new PngRenderTarget(size, size);
        var bg = Rgba.Parse(style.BackgroundColor);
        target.FillAll(bg);

        Rgba fg1 = Rgba.Parse(style.ForegroundColor);
        Rgba fg2 = style.ForegroundColor2 is null ? fg1 : Rgba.Parse(style.ForegroundColor2);
        bool gradient = style.ForegroundColor2 != null;
        bool roundModules = style.ModuleRounding > 0.001;
        bool roundFrame = style.FrameRounding > 0.001;
        bool circularFinders = style.CircularFinders;

        int innerN = n - border * 2;
        int innerPx = innerN * boxSize;

        // 三个定位标记的左上角像素坐标（含白边偏移）
        int[] finderX = { border * boxSize, (n - border - 7) * boxSize, border * boxSize };
        int[] finderY = { border * boxSize, border * boxSize, (n - border - 7) * boxSize };

        // 逐像素绘制（质量优先，size 通常 ≤ 1000）
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 整码圆角：距四角超出半径的像素保持背景
                if (roundFrame)
                {
                    int radius = (int)(style.FrameRounding * size);
                    int cx = x < radius ? radius : (x >= size - radius ? size - 1 - radius : -1);
                    int cy = y < radius ? radius : (y >= size - radius ? size - 1 - radius : -1);
                    if (cx >= 0 && cy >= 0)
                    {
                        int dx = x - cx, dy = y - cy;
                        if (dx * dx + dy * dy > radius * radius) continue;
                    }
                }

                int row = y / boxSize, col = x / boxSize;
                if (row < 0 || row >= n || col < 0 || col >= n) continue;

                if (circularFinders)
                {
                    // 圆形定位区：7x7 区域只保留外圆环 + 中心圆（忽略原方形模块值）
                    bool inFinder = false, dark = false;
                    for (int f = 0; f < 3; f++)
                    {
                        int lx = x - finderX[f], ly = y - finderY[f];
                        if ((uint)lx >= 7u * boxSize || (uint)ly >= 7u * boxSize) continue;
                        inFinder = true;
                        double cx = lx - 3.5 * boxSize, cy = ly - 3.5 * boxSize;
                        double d2 = cx * cx + cy * cy;
                        double dot = 1.5 * boxSize, ringIn = 2.5 * boxSize, outer = 3.5 * boxSize;
                        dark = d2 <= dot * dot || (d2 >= ringIn * ringIn && d2 <= outer * outer);
                        break;
                    }
                    if (inFinder ? !dark : !full[row, col]) continue;
                }
                else if (!full[row, col]) continue;

                if (roundModules)
                {
                    int lx = x - col * boxSize, ly = y - row * boxSize;
                    int r = (int)(style.ModuleRounding * boxSize);
                    int cx = lx < r ? r : (lx >= boxSize - r ? boxSize - 1 - r : -1);
                    int cy = ly < r ? r : (ly >= boxSize - r ? boxSize - 1 - r : -1);
                    if (cx >= 0 && cy >= 0)
                    {
                        int dx = lx - cx, dy = ly - cy;
                        if (dx * dx + dy * dy > r * r) continue;
                    }
                }

                Rgba color = gradient
                    ? Interpolate(fg1, fg2, (x + y) / (2.0 * (size - 1)), style.GradientAngle)
                    : fg1;
                target.SetPixel(x, y, color.Blend(bg));
            }
        }

        // Logo 叠加
        if (style.Logo != null)
        {
            var logo = PngDecoder.Decode(style.Logo);
            if (logo != null)
            {
                int ratio = (int)(style.LogoRatio * n);
                int pxSize = ratio * boxSize;
                int x0 = (size - pxSize) / 2;
                int y0 = (size - pxSize) / 2;
                int pad = (int)(style.LogoPadding * size);
                DrawPaddedImage(target, logo, x0 - pad, y0 - pad, pxSize + pad * 2, bg);
            }
        }

        return target;
    }

    private static Rgba Interpolate(Rgba a, Rgba b, double t, double angleDeg)
    {
        // 角度只影响取 x/y 权重，这里用对角线近似（对角渐变）
        if (angleDeg >= 45 && angleDeg < 135) t = 1 - t;
        return new Rgba(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t),
            255);
    }

    private static void DrawPaddedImage(PngRenderTarget target, PngImage image, int x0, int y0, int size, Rgba bg)
    {
        // 白色垫层（圆角矩形内）
        Rgba white = new(255, 255, 255, 255);
        int r = size / 4;
        for (int y = y0; y < y0 + size; y++)
        {
            for (int x = x0; x < x0 + size; x++)
            {
                int cx = x < y0 + r ? x0 + r : (x >= x0 + size - r ? x0 + size - 1 - r : -1);
                int cy = y < y0 + r ? y0 + r : (y >= y0 + size - r ? y0 + size - 1 - r : -1);
                if (cx >= 0 && cy >= 0)
                {
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy > r * r) continue;
                }
                target.SetPixel(x, y, white);
            }
        }
        // 缩放绘制 Logo（最近邻，带透明混合）
        for (int y = y0 + 1; y < y0 + size - 1; y++)
        {
            for (int x = x0 + 1; x < x0 + size - 1; x++)
            {
                int ix = (x - x0) * image.Width / size;
                int iy = (y - y0) * image.Height / size;
                if (ix < 0 || iy < 0 || ix >= image.Width || iy >= image.Height) continue;
                var c = image.Pixels[iy * image.Width + ix];
                if (c.A > 0)
                    target.SetPixel(x, y, c);
            }
        }
    }
}

/// <summary>极简 PNG 解码（8-bit RGB/RGBA，用于读 Logo）。</summary>
public sealed class PngImage
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required Rgba[] Pixels { get; init; }
}

public static class PngDecoder
{
    public static PngImage? Decode(byte[] png)
    {
        try
        {
            if (png.Length < 8 || png[0] != 0x89 || png[1] != 0x50) return null;
            int pos = 8;
            int width = 0, height = 0, bitDepth = 0, colorType = 0;
            var idat = new MemoryStream();
            while (pos + 8 <= png.Length)
            {
                int len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
                var type = Encoding.ASCII.GetString(png, pos + 4, 4);
                if (type == "IHDR")
                {
                    var d = png.AsSpan(pos + 8, len);
                    width = d[0] << 24 | d[1] << 16 | d[2] << 8 | d[3];
                    height = d[4] << 24 | d[5] << 16 | d[6] << 8 | d[7];
                    bitDepth = d[8];
                    colorType = d[9];
                }
                else if (type == "IDAT")
                {
                    idat.Write(png, pos + 8, len);
                }
                pos += 12 + len;
                if (type == "IEND") break;
            }
            if (bitDepth != 8 || (colorType != 2 && colorType != 6)) return null;
            int channels = colorType == 2 ? 3 : 4;

            var raw = new MemoryStream();
            var zlib = idat.ToArray();
            // 完整 zlib 流（含 2 字节头 + Adler32 尾）解码
            using (var zlibStream = new System.IO.Compression.ZLibStream(
                       new MemoryStream(zlib), System.IO.Compression.CompressionMode.Decompress))
            {
                zlibStream.CopyTo(raw);
            }
            var rawBytes = raw.ToArray();

            int stride = width * channels;
            var pixels = new Rgba[width * height];
            var prev = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                int filter = rawBytes[y * (stride + 1)];
                var line = new byte[stride];
                for (int x = 0; x < stride; x++)
                {
                    int v = rawBytes[y * (stride + 1) + 1 + x];
                    int a = x >= channels ? line[x - channels] : 0;
                    int b = prev[x];
                    int c = x >= channels ? prev[x - channels] : 0;
                    line[x] = filter switch
                    {
                        0 => (byte)v,
                        1 => (byte)(v + a),
                        2 => (byte)(v + b),
                        3 => (byte)(v + (a + b) / 2),
                        4 => (byte)(v + Paeth(a, b, c)),
                        _ => (byte)v,
                    };
                }
                for (int x = 0; x < width; x++)
                {
                    int o = y * stride + x * channels;
                    pixels[y * width + x] = new Rgba(line[o], line[o + 1], line[o + 2],
                        channels == 4 ? line[o + 3] : 255);
                }
                Array.Copy(line, prev, stride);
            }
            return new PngImage { Width = width, Height = height, Pixels = pixels };
        }
        catch
        {
            return null;
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
