# CQRC · 彩色二维码 SDK

从零实现的彩色（Colored / Stylized）QR Code 生成库，**不依赖任何第三方组件**。
同时提供 **C#**（`Cqrc.Sdk`，net8.0 / net10.0）与 **纯 JavaScript**（`js/cqrc.js`，零依赖，浏览器与 Node 通用）两份等价实现。

覆盖标准：QR 2000（ISO/IEC 18004）编码 + GF(2⁸) Reed-Solomon 纠错 + ISO/IEC 21570 扩展元数据。

## 特性

| 能力 | 说明 |
| --- | --- |
| 完整 QR 2000 编码 | 数字 / 字母数字 / 字节（UTF-8）三种模式，版本 1–40，L·M·Q·H 四档纠错 |
| 数据段优化 | `Optimize` / `optimize` 开关，按最长数字串→字母数字串→字节贪心切分，与 python-qrcode `optimize=4` 逐位一致 |
| 掩码自动选择 | 8 种掩码全量试算，按 ISO 罚分取优 |
| 彩色与造型 | 纯色 / 线性渐变前景、模块圆角、整码圆角、**圆形定位标记区**（`CircularFinders`）、中央 Logo 叠加 |
| ISO/IEC 21570 元数据 | 在数据段之后追加扩展段（品牌、内容哈希等 JSON 键值），可自动写入 `content_hash`（SHA-256） |
| 多格式输出 | C#：PNG（手写 zlib/CRC）、SVG、PDF（矢量单页）、裸位图；JS：SVG、Canvas / DataURL |
| 跨语言一致性 | C# 与 JS 对同一输入产出**逐模块完全相同**的矩阵，均有自动化测试保证 |

## 仓库结构

```
Cqrc.Sdk/          C# 类库（无第三方依赖）
  QrEncoder.cs       比特流 → RS 分块 → 交错码字
  QrMatrixBuilder.cs 固定图形 + 之字形数据放置 + 掩码选择
  MaskScoring.cs     ISO 罚分（规则 1–4）
  QrTables.cs        RS 块表 / 对齐图形坐标 / 长度位宽
  Gf256.cs GfPoly.cs ReedSolomon.cs   GF(2⁸) 与 RS 运算
  QrBch.cs           格式信息 / 版本信息的 BCH 编码
  Metadata/Iso21570.cs
  Rendering/         QrRenderer(PNG) · SvgRenderer · PdfRenderer
Cqrc.Sample/       生成 PNG / SVG / PDF 示例（输出在 Cqrc.Sample/qr_*.*)
Cqrc.Demo/         诊断工具：打印分段、8 种掩码罚分与矩阵文本
Cqrc.Sdk.Tests/    xUnit：与 python-qrcode 黄金向量 vectors.json 逐模块比对
js/cqrc.js         纯 JS 等价实现（UMD，零依赖）
js/test.js         Node 自测：SHA-256 / RS / 结构 / 与同一份黄金向量比对
```

## 快速开始

### C#

```csharp
using Cqrc;
using Cqrc.Rendering;

var code = new CqrcCode { ErrorCorrection = ErrorCorrectionLevel.H };
code.Style.ForegroundColor  = "#0066CC";
code.Style.ForegroundColor2 = "#66CCFF";   // 给出第二色即启用线性渐变
code.Style.ModuleRounding   = 0.3;         // 模块圆角
code.Style.CircularFinders  = true;        // 圆形定位标记区
code.Style.Metadata = new Dictionary<string, string?> { ["brand"] = "CQRC" };

code.AddData("https://example.com/CQRC-demo?x=2026");
code.Generate();                            // → code.ActualVersion / code.MaskPattern

QrRenderer.Render(code, 12).Save("qr.png");
new SvgRenderer().Save(code, "qr.svg", 12);
new PdfRenderer().Save(code, "qr.pdf", 12);
```

### JavaScript

```html
<canvas id="cv"></canvas>
<script src="cqrc.js"></script>
<script>
  const qr = new CqrcCode({ errorCorrection: Cqrc.EC.H, border: 4 });
  qr.style.circularFinders = true;
  qr.style.foregroundColor = "#0066CC";
  qr.addData("https://example.com/CQRC-demo?x=2026");
  qr.generate();

  qr.toCanvas(document.getElementById("cv"), 12);
  const img = new Image();
  img.src = qr.toDataUrl(12);   // SVG DataURL，适合直接放进 <img>
</script>
```

Node：

```js
const { EC, CqrcCode } = require("./js/cqrc.js");
const qr = new CqrcCode({ errorCorrection: EC.Q, optimize: true });
qr.addData("ABC 123 https://example.com");
qr.generate();
require("fs").writeFileSync("qr.svg", qr.toSvg(10));
```

## 样式参数

| C# `QrStyle` | JS `style` | 默认 | 说明 |
| --- | --- | --- | --- |
| `ForegroundColor` | `foregroundColor` | `#1A1A1A` | 深色模块颜色，支持 `#RGB` / `#RRGGBB` / `rgb()` / `rgba()` |
| `ForegroundColor2` | `foregroundColor2` | `null` | 渐变第二色，非空即启用线性渐变 |
| `GradientAngle` | `gradientAngle` | `0` | 渐变方向（90 为纵向） |
| `BackgroundColor` | `backgroundColor` | `#FFFFFF` | 浅色底色 |
| `ModuleRounding` | `moduleRounding` | `0` | 单模块圆角比例 0–0.5 |
| `FrameRounding` | `frameRounding` | `0` | 整码外框圆角比例 0–0.5 |
| `CircularFinders` | `circularFinders` | `false` | 用同心圆替换三个方形定位标记 |
| `Logo` / `LogoRatio` / `LogoPadding` | `logo` / `logoRatio` / `logoPadding` | — / 0.15 / 0.03 | 中央 Logo（PNG 字节），建议配合 H 级纠错 |
| `Metadata` | `metadata` | `null` | ISO/IEC 21570 扩展元数据键值 |
| `IncludeContentHash` | `includeContentHash` | `true` | 自动把内容的 SHA-256 写入 `content_hash` |

> 圆形定位标记只改变**视觉**，模块矩阵本身仍是标准 QR 的定位图形，扫码器按图像识别定位角，实测可被 jsqr、手机相机正常识读。

## 构建与测试

前置：.NET SDK 8 或 10、Node.js。

```bash
dotnet test Cqrc.Sdk.Tests/Cqrc.Sdk.Tests.csproj   # 15 项：C# 与 python-qrcode 黄金向量逐模块比对
node js/test.js                                    # 16 项：SHA-256 / RS / 结构 / 同一份黄金向量
dotnet run --project Cqrc.Sample                   # 重新生成 Cqrc.Sample/qr_{square,circle}.{png,svg,pdf}
dotnet run --project Cqrc.Demo -- <输出目录>        # 导出掩码罚分与矩阵文本，便于排查跨语言差异
```

`Cqrc.Sdk.Tests/vectors.json` 是黄金向量，由 **python-qrcode 8.2**（`optimize=4`，`border=0`）生成，包含 7 个用例的版本、掩码、码字序列与完整模块矩阵。C# 与 JS 都必须逐位命中它，因此三份实现互为交叉验证。新增用例时按下面方式重新导出：

```bash
pip install qrcode
python - <<'PY'
import json, qrcode
from qrcode import util
from qrcode.constants import ERROR_CORRECT_M
q = qrcode.QRCode(error_correction=ERROR_CORRECT_M, border=0)
q.add_data("Hello, CQRC! 彩色二维码", optimize=4)
mask = q.best_mask_pattern(); q.makeImpl(False, mask)
print(json.dumps({"version": q.version, "mask_pattern": mask,
                  "modules": [[bool(x) for x in r] for r in q.modules],
                  "code_words": list(util.create_data(q.version, ERROR_CORRECT_M, q.data_list))}))
PY
```

## 与 python-qrcode 的位级一致性

为了让三家输出完全一致，掩码评分刻意沿用了 python-qrcode 的若干**实现细节**（并非 ISO 原文写法）：

- 规则 2：检测到右上角异色时跳过下一列（其 `iter()/next()` 加速写法）。
- 规则 3：只匹配 `10111010000` 与 `00001011101` 两种 11 格窗口，并在末位为深色时跳一格（Horspool 加速）。
- 规则 4：按浮点占比计算偏离度。
- 掩码试算阶段格式信息区与暗色模块均为浅色（对应 `setup_type_info(test=True)`）。

这些差异会影响最终选出的掩码，改动任何一条都会让黄金向量失配。

## 已知限制

- 不支持汉字（Kanji）模式的自动编码，`QrMode.Kanji` 仅留位。
- ISO/IEC 21570 元数据段按本仓库注释的字节布局写入，常见扫码器只把该段当作普通字节数据读出，不做 21570 语义解析。
- JS 版 `logo` 仅接受已加载完成的 `HTMLImageElement`；PNG 解析与 Logo 叠加在 C# 版中实现得更完整。
