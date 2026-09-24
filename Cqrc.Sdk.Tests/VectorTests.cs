using System.IO;
using System.Text.Json;
using Cqrc;

namespace Cqrc.Sdk.Tests;

/// <summary>
/// 与 python-qrcode 生成的黄金向量（vectors.json）逐字节对比：
/// 码字序列 + 完整模块矩阵 + 掩码选择。
/// </summary>
public class VectorTests
{
    private static string VectorsPath =>
        Path.Combine(AppContext.BaseDirectory, "vectors.json");

    private static Dictionary<string, JsonElement> LoadVectors()
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(VectorsPath))!;

    private static (string Data, ErrorCorrectionLevel Ec, int? Version) CaseParams(string name) => name switch
    {
        "hello" => ("Hello, CQRC! 彩色二维码", ErrorCorrectionLevel.M, null),
        "numeric" => ("1234567890123456", ErrorCorrectionLevel.L, null),
        "alpha" => ("ABC 123 $%*-./:", ErrorCorrectionLevel.M, null),
        "url" => ("https://github.com/yxpil/CQRC", ErrorCorrectionLevel.Q, null),
        "utf8" => ("你好，世界！こんにちは \U0001F3A8", ErrorCorrectionLevel.H, null),
        "long-mixed" => (string.Concat(Enumerable.Repeat("CQRC v1: 1234567890123456789012345678901234567890", 8)) + "END", ErrorCorrectionLevel.M, null),
        "fixed-v11" => (string.Concat(Enumerable.Repeat("https://example.com/a?b=1&c=2", 3)), ErrorCorrectionLevel.M, 11),
        _ => throw new ArgumentException(name),
    };

    [Theory]
    [InlineData("hello")]
    [InlineData("numeric")]
    [InlineData("alpha")]
    [InlineData("url")]
    [InlineData("utf8")]
    [InlineData("long-mixed")]
    [InlineData("fixed-v11")]
    public void CodeWordsMatchPythonQrcode(string name)
    {
        var vectors = LoadVectors();
        var (data, ec, version) = CaseParams(name);
        var result = QrEncoder.Encode(SplitLikePython(data), ec, version);

        var expected = vectors[name].GetProperty("code_words").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        Assert.Equal(expected, result.CodeWords.Select(b => (int)b).ToArray());
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("numeric")]
    [InlineData("alpha")]
    [InlineData("url")]
    [InlineData("utf8")]
    [InlineData("long-mixed")]
    [InlineData("fixed-v11")]
    public void MatrixMatchesPythonQrcode(string name)
    {
        var vectors = LoadVectors();
        var (data, ec, version) = CaseParams(name);
        var encoded = QrEncoder.Encode(SplitLikePython(data), ec, version);
        var matrix = QrMatrixBuilder.Build(encoded);
        var m = matrix.ToArray();

        var caseEl = vectors[name];
        int n = m.GetLength(0);
        Assert.Equal(caseEl.GetProperty("version").GetInt32(), encoded.Version);
        Assert.Equal(caseEl.GetProperty("mask_pattern").GetInt32(), matrix.MaskPattern);

        var rows = caseEl.GetProperty("modules").EnumerateArray().ToList();
        var diff = new List<(int, int)>();
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                if (m[r, c] != rows[r].EnumerateArray().ElementAt(c).GetBoolean())
                    diff.Add((r, c));
        Assert.True(diff.Count == 0, $"矩阵有 {diff.Count} 处不同：{string.Join(",", diff.Take(10))}");
    }

    /// <summary>python-qrcode <c>optimize=4</c> 的等价切分（实现见 <see cref="QrDataChunk.SplitOptimal"/>）。</summary>
    private static List<QrDataChunk> SplitLikePython(string data) => QrDataChunk.SplitOptimal(data);
}
