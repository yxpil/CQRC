// CQRC JS 版自测：SHA-256 向量、RS 校验、结构检查、跨语言一致性
const assert = require("assert");
const fs = require("fs");
const path = require("path");
const { EC, CqrcCode, QrEncoder, ReedSolomon, sha256Hex, splitOptimal } = require("./cqrc.js");

let pass = 0, fail = 0;
function t(name, fn) {
  try { fn(); pass++; console.log("  ok  " + name); }
  catch (e) { fail++; console.log("FAIL  " + name + "\n      " + (e && e.message || e)); }
}

/* 1. SHA-256 已知向量（FIPS 180-4 附录 A） */
t("sha256('') = e3b0c442...", () => {
  assert.strictEqual(sha256Hex(""), "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
});
t("sha256('abc') = ba7816bf...", () => {
  assert.strictEqual(sha256Hex("abc"), "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
});
t("sha256 长消息 (896bit)", () => {
  assert.strictEqual(
    sha256Hex("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq"),
    "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1");
});

/* 2. Reed-Solomon 自洽性 */
t("RS verify: 随机块余式为 0", () => {
  const data = new Uint8Array(32);
  for (let i = 0; i < 32; i++) data[i] = (i * 37 + 11) & 0xFF;
  const ec = ReedSolomon.encode(data, 10);
  assert.strictEqual(ec.length, 10);
  assert.ok(ReedSolomon.verify(data, ec), "verify failed");
  // 破坏一个数据字节 → 余式非 0
  const bad = data.slice(); bad[0] ^= 1;
  assert.ok(!ReedSolomon.verify(bad, ec), "broken data should fail");
});

/* 3. 编码器基础 */
t("短 URL 自动选版本 / 元数据撑大版本", () => {
  const bare = new CqrcCode({ errorCorrection: EC.M, style: { includeContentHash: false } });
  bare.addData("https://example.com/CQRC-demo?x=2026").generate();
  assert.strictEqual(bare.actualVersion, 3, "31 字节 URL @M 应为 v3");

  const hashed = new CqrcCode({ errorCorrection: EC.M });
  hashed.addData("https://example.com/CQRC-demo?x=2026").generate();
  assert.ok(hashed.actualVersion > bare.actualVersion, "content_hash 段应抬高版本");
  assert.strictEqual(hashed.fullMatrix().size, hashed.actualVersion * 4 + 17 + hashed.border * 2);
});
t("长数据自动升版本", () => {
  const qr = new CqrcCode({ errorCorrection: EC.H });
  qr.addData("CQRC v1: 123456789012345678901234567890".repeat(8) + "END").generate();
  assert.ok(qr.actualVersion >= 9, "long data needs higher version, got " + qr.actualVersion);
});

/* 4. 结构检查（QR 2000 固定图形） */
t("定位角/分隔线/时序线/暗色模块", () => {
  const qr = new CqrcCode({ errorCorrection: EC.M });
  qr.addData("hello world").generate();
  const { size, get } = qr.fullMatrix();
  const b = qr.border, n = size - b * 2;
  assert.ok(get(b, b) && get(b, b + 6) && get(b + 6, b) && get(b + 6, b + 6), "finder corners");
  assert.ok(get(b + 3, b + 3), "finder center");
  assert.ok(!get(b + 1, b + 1), "finder ring white");
  assert.ok(!get(b, b + 7) && !get(b + 7, b), "separator white");
  assert.ok(!get(0, 0) && !get(size - 1, size - 1), "quiet zone white");
  for (let i = 8; i < n - 8; i++) // 时序线交替
    assert.ok(get(b + 6, b + i) === (i % 2 === 0) && get(b + i, b + 6) === (i % 2 === 0), `timing ${i}`);
  assert.ok(get(b + n - 8, b + 8), "dark module");
});

/* 5. 元数据段 */
t("metadata + content_hash 写入", () => {
  const qr = new CqrcCode({ errorCorrection: EC.M, style: { metadata: { brand: "CQRC" } } });
  qr.addData("test").generate();
  assert.ok(qr.style.metadata.content_hash === sha256Hex("test"), "content_hash");
});

/* 6. SVG 输出 */
t("SVG 含圆形定位区", () => {
  const qr = new CqrcCode({ errorCorrection: EC.H, style: { circularFinders: true } });
  qr.addData("https://example.com").generate();
  const svg = qr.toSvg(8);
  assert.ok(svg.startsWith("<svg"), "svg tag");
  assert.ok((svg.match(/<circle /g) || []).length >= 9, "3 corners x 3 circles");
});

/* 7. 跨语言一致性：与 C# 版（python-qrcode 黄金向量）逐模块/逐码字比对 */
const CASES = {
  "hello": ["Hello, CQRC! 彩色二维码", EC.M, null],
  "numeric": ["1234567890123456", EC.L, null],
  "alpha": ["ABC 123 $%*-./:", EC.M, null],
  "url": ["https://github.com/yxpil/CQRC", EC.Q, null],
  "utf8": ["你好，世界！こんにちは \u{1F3A8}", EC.H, null],
  "long-mixed": [Array(8).fill("CQRC v1: 1234567890123456789012345678901234567890").join("") + "END", EC.M, null],
  "fixed-v11": [Array(3).fill("https://example.com/a?b=1&c=2").join(""), EC.M, 11],
};
const vecFile = path.join(__dirname, "..", "Cqrc.Sdk.Tests", "vectors.json");
if (fs.existsSync(vecFile)) {
  const vectors = JSON.parse(fs.readFileSync(vecFile, "utf8"));
  for (const [name, [data, ec, fixedVersion]] of Object.entries(CASES)) {
    t(`C#/JS 矩阵+码字一致: ${name}`, () => {
      const qr = new CqrcCode({ errorCorrection: ec, version: fixedVersion, optimize: true, border: 0, style: { includeContentHash: false } });
      qr.addData(data).generate();
      const v = vectors[name];
      assert.strictEqual(qr.actualVersion, v.version, "version mismatch");
      assert.strictEqual(qr.maskPattern, v.mask_pattern, "mask mismatch");
      // 码字序列
      const { codeWords } = QrEncoder.encode(splitOptimal(data), ec, fixedVersion);
      assert.deepStrictEqual([...codeWords], v.code_words, "code words differ");
      // 完整矩阵
      const { size, get } = qr.fullMatrix(0);
      let diff = [];
      for (let r = 0; r < size; r++) for (let c = 0; c < size; c++)
        if (!!get(r, c) !== v.modules[r][c]) diff.push(`(${r},${c})`);
      assert.strictEqual(diff.length, 0, `${diff.length} modules differ: ${diff.slice(0, 8).join(",")}`);
    });
  }
} else {
  console.log("  skip 跨语言比对（Cqrc.Sdk.Tests/vectors.json 不存在，先跑 dotnet test 生成）");
}

/* 8. 输出样例文件 */
const out = path.join(__dirname, "samples");
fs.mkdirSync(out, { recursive: true });
const demo = new CqrcCode({ errorCorrection: EC.H, style: { circularFinders: true } });
demo.addData("https://example.com/CQRC-js-demo?x=2026").generate();
fs.writeFileSync(path.join(out, "qr_circle.svg"), demo.toSvg(10));
const grad = new CqrcCode({ errorCorrection: EC.H,
  style: { foregroundColor: "#0066CC", foregroundColor2: "#66CCFF", moduleRounding: 0.3 } });
grad.addData("https://example.com").generate();
fs.writeFileSync(path.join(out, "qr_gradient.svg"), grad.toSvg(10));

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail > 0 ? 1 : 0);
