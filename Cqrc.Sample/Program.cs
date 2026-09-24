using Cqrc;
using Cqrc.Rendering;

string outDir = @"C:\Users\User\Desktop\CQRC\Cqrc.Sample";

void Make(string name, bool circular)
{
    var code = new CqrcCode { ErrorCorrection = ErrorCorrectionLevel.H };
    code.Style.CircularFinders = circular;
    code.AddData("https://example.com/CQRC-demo?x=2026");
    code.Generate();

    QrRenderer.Render(code, 12).Save($@"{outDir}\qr_{name}.png");
    new SvgRenderer().Save(code, $@"{outDir}\qr_{name}.svg", 12);
    new PdfRenderer().Save(code, $@"{outDir}\qr_{name}.pdf", 12);
    Console.WriteLine($"{name} done (v{code.ActualVersion})");
}

Make("square", false);
Make("circle", true);
