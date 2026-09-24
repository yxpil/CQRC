using System.Text;
using Cqrc;

// 诊断工具：打印每个用例的分段、8 种掩码罚分与最终矩阵，供跨语言（JS / python-qrcode）比对。
// 用法：dotnet run --project Cqrc.Demo -- [输出目录]
string outDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "cqrc-dump");
Directory.CreateDirectory(outDir);

(string name, string data, ErrorCorrectionLevel ec, int? version)[] cases =
{
    ("numeric", "1234567890123456", ErrorCorrectionLevel.L, null),
    ("hello", "Hello, CQRC! 彩色二维码", ErrorCorrectionLevel.M, null),
    ("url", "https://github.com/yxpil/CQRC", ErrorCorrectionLevel.Q, null),
    ("utf8", "你好，世界！こんにちは \U0001F3A8", ErrorCorrectionLevel.H, null),
};

foreach (var (name, data, ec, version) in cases)
{
    var chunks = QrDataChunk.SplitOptimal(data);
    var encoded = QrEncoder.Encode(chunks, ec, version);
    var matrix = QrMatrixBuilder.Build(encoded);
    var grid = matrix.ToArray();
    int n = grid.GetLength(0);

    var report = new StringBuilder();
    report.AppendLine($"# {name}: v{encoded.Version} ec={ec} mask={matrix.MaskPattern} n={n}");
    foreach (var c in chunks)
        report.AppendLine($"chunk {c.Mode} chars={c.CharCount} bytes={c.Data.Length} payload_bits={c.PayloadBits}");
    for (int mask = 0; mask < 8; mask++)
    {
        var probe = QrMatrixBuilder.BuildBlank(encoded.Version);
        QrMatrixBuilder.MapData(probe, encoded.CodeWords, mask);
        report.AppendLine($"mask_score {mask} {MaskScoring.LostPoint(probe.ToArray())}");
    }
    for (int r = 0; r < n; r++)
        report.AppendLine(new string(grid[r, 0] ? '1' : '0', 0) +
            string.Concat(Enumerable.Range(0, n).Select(c => grid[r, c] ? '1' : '0')));

    string path = Path.Combine(outDir, $"{name}.txt");
    File.WriteAllText(path, report.ToString());
    Console.WriteLine($"{name}: v{encoded.Version} mask{matrix.MaskPattern} -> {path}");
}
