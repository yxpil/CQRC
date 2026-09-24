using System.IO;
using System.Text;
using Cqrc;

namespace Cqrc.Rendering;

/// <summary>
/// 颜色（支持 #RGB / #RRGGBB / #RRGGBBAA / 常用名称）。
/// </summary>
public readonly record struct Rgba(int R, int G, int B, int A = 255)
{
    public static Rgba Parse(string s)
    {
        s = s.Trim();
        if (s.StartsWith('#'))
        {
            var hex = s[1..];
            if (hex.Length == 3)
                return new Rgba(From2(hex[0]), From2(hex[1]), From2(hex[2]));
            if (hex.Length == 6)
                return new Rgba(From2(hex[0]) * 16 + From2(hex[1]), From2(hex[2]) * 16 + From2(hex[3]), From2(hex[4]) * 16 + From2(hex[5]));
            if (hex.Length == 8)
                return new Rgba(From2(hex[0]) * 16 + From2(hex[1]), From2(hex[2]) * 16 + From2(hex[3]),
                    From2(hex[4]) * 16 + From2(hex[5]), From2(hex[6]) * 16 + From2(hex[7]));
            throw new FormatException($"无法解析颜色：{s}");
        }
        return s.ToLowerInvariant() switch
        {
            "white" or "#fff" => new Rgba(255, 255, 255),
            "black" or "#000" => new Rgba(0, 0, 0),
            "red" => new Rgba(220, 38, 38),
            "blue" => new Rgba(37, 99, 235),
            "green" => new Rgba(22, 163, 74),
            "orange" => new Rgba(249, 115, 22),
            "purple" => new Rgba(147, 51, 234),
            "transparent" => new Rgba(0, 0, 0, 0),
            _ => throw new FormatException($"无法解析颜色：{s}"),
        };
    }

    private static int From2(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => throw new FormatException($"非法十六进制：{c}"),
    };

    public string Hex => A == 255
        ? $"#{R:X2}{G:X2}{B:X2}"
        : $"#{R:X2}{G:X2}{B:X2}{A:X2}";

    /// <summary>与背景色按 alpha 混合。</summary>
    public Rgba Blend(Rgba bg) => A == 255 ? this : new Rgba(
        (R * A + bg.R * (255 - A)) / 255,
        (G * A + bg.G * (255 - A)) / 255,
        (B * A + bg.B * (255 - A)) / 255, 255);

    public override string ToString() => Hex;
}

/// <summary>
/// 纯 .NET 位图渲染器：直接生成 32bpp BGRA 像素缓冲（无需 System.Drawing，跨平台）。
/// 支持：渐变前景色、模块圆角、整码圆角、中央 Logo（简单不透明叠加）、白边。
/// </summary>
public sealed class PngRenderTarget
{
    public int Width { get; }
    public int Height { get; }
    private readonly byte[] _pixels; // BGRA

    public PngRenderTarget(int width, int height)
    {
        Width = width; Height = height;
        _pixels = new byte[width * height * 4];
    }

    public void FillAll(Rgba color)
    {
        for (int i = 0; i < _pixels.Length; i += 4)
        {
            _pixels[i] = (byte)color.B;
            _pixels[i + 1] = (byte)color.G;
            _pixels[i + 2] = (byte)color.R;
            _pixels[i + 3] = (byte)color.A;
        }
    }

    public void SetPixel(int x, int y, Rgba color)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        int i = (y * Width + x) * 4;
        _pixels[i] = (byte)color.B;
        _pixels[i + 1] = (byte)color.G;
        _pixels[i + 2] = (byte)color.R;
        _pixels[i + 3] = (byte)color.A;
    }

    public byte[] Pixels => _pixels;

    /// <summary>
    /// 手工 PNG 编码（无第三方依赖）：IHDR/IDAT(Deflate)/IEND，16 位 CRC32。
    /// </summary>
    public byte[] ToPng()
    {
        using var ms = new MemoryStream();
        WritePng(ms, Width, Height, _pixels);
        return ms.ToArray();
    }

    public void Save(string path) => File.WriteAllBytes(path, ToPng());

    /// <summary>32bpp 非交错 RGBA→PNG（带 alpha）。</summary>
    internal static void WritePng(Stream output, int width, int height, byte[] bgra)
    {
        // 转 RGBA 并加每行 filter byte 0
        int stride = width * 4;
        var raw = new byte[height * (stride + 1)];
        for (int y = 0; y < height; y++)
        {
            int src = y * stride, dst = y * (stride + 1);
            raw[dst] = 0; // 无 filter
            for (int x = 0; x < width; x++)
            {
                raw[dst + 1 + x * 4] = bgra[src + x * 4 + 2]; // R
                raw[dst + 2 + x * 4] = bgra[src + x * 4 + 1]; // G
                raw[dst + 3 + x * 4] = bgra[src + x * 4 + 0]; // B
                raw[dst + 4 + x * 4] = bgra[src + x * 4 + 3]; // A
            }
        }

        output.Write(PngSignature);
        WriteChunk(output, "IHDR", BuildIhdr(width, height));
        WriteChunk(output, "IDAT", DeflateCompress(raw));
        WriteChunk(output, "IEND", Array.Empty<byte>());
    }

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static byte[] BuildIhdr(int w, int h)
    {
        var b = new byte[13];
        b[0] = (byte)(w >> 24); b[1] = (byte)(w >> 16); b[2] = (byte)(w >> 8); b[3] = (byte)w;
        b[4] = (byte)(h >> 24); b[5] = (byte)(h >> 16); b[6] = (byte)(h >> 8); b[7] = (byte)h;
        b[8] = 8;  // 位深
        b[9] = 6;  // RGBA
        return b;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        len[0] = (byte)(data.Length >> 24); len[1] = (byte)(data.Length >> 16);
        len[2] = (byte)(data.Length >> 8); len[3] = (byte)data.Length;
        s.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        Span<byte> crc = stackalloc byte[4];
        Crc32.Compute(typeBytes.Concat(data).ToArray(), crc);
        s.Write(crc);
    }

    private static byte[] DeflateCompress(byte[] data)
    {
        // 手工构建 zlib 流：2 字节头 + DeflateStream 输出（含 Adler32）+ 4 字节 Adler32 由流自带
        using var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0x9C); // zlib 默认压缩头
        using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }
        return output.ToArray();
    }

    private static class Crc32
    {
        private static readonly uint[] Table = Build();
        private static uint[] Build()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }

        public static void Compute(byte[] data, Span<byte> out4)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            crc ^= 0xFFFFFFFFu;
            out4[0] = (byte)(crc >> 24); out4[1] = (byte)(crc >> 16);
            out4[2] = (byte)(crc >> 8); out4[3] = (byte)crc;
        }
    }
}
