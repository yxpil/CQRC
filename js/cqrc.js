/*!
 * CQRC — 彩色二维码 SDK（JavaScript 版，零依赖）
 * C# 版 Cqrc.Sdk 的纯 JS 移植：ISO/IEC 18004 (QR 2000) 编码 +
 * GF(256) Reed-Solomon 纠错 + ISO/IEC 21570 扩展元数据 + SVG/Canvas 渲染。
 *
 * 用法（浏览器 <script> 或 Node require）：
 *   const qr = new CqrcCode({ errorCorrection: Cqrc.EC.H, border: 4 });
 *   qr.style.circularFinders = true;              // 圆形定位标记区
 *   qr.addData("https://example.com");
 *   qr.generate();
 *   document.getElementById("c").src = "data:image/svg+xml," + encodeURIComponent(qr.toSvg(12));
 *   qr.toCanvas(document.getElementById("cv"), 12);
 */
(function (root, factory) {
  const api = factory();
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  if (root) { root.Cqrc = api; root.CqrcCode = api.CqrcCode; }
})(typeof self !== "undefined" ? self : this, function () {
  "use strict";

  /* ================= 错误等级 ================= */
  const EC = Object.freeze({ L: 1, M: 0, Q: 3, H: 2 });

  /* ================= 位缓冲（MSB-first） ================= */
  class BitBuffer {
    constructor() { this._bytes = []; this.bitLength = 0; }
    put(value, bits) {
      for (let i = bits - 1; i >= 0; i--) this.putBit(((value >> i) & 1) === 1);
    }
    putBit(bit) {
      const bi = this.bitLength >> 3;
      if (bi >= this._bytes.length) this._bytes.push(0);
      if (bit) this._bytes[bi] |= 0x80 >> (this.bitLength & 7);
      this.bitLength++;
    }
    toBytes() { return Uint8Array.from(this._bytes); }
  }

  /* ================= GF(2^8)，本原多项式 0x11D ================= */
  const Gf256 = (() => {
    const exp = new Int32Array(512), log = new Int32Array(256);
    let x = 1;
    for (let i = 0; i < 512; i++) { exp[i] = x; x <<= 1; if (x & 0x100) x ^= 0x11D; }
    for (let i = 0; i < 512; i++) log[exp[i]] = i % 255;
    return { gexp: (n) => exp[n % 255], glog: (n) => log[n] };
  })();

  class GfPoly {
    constructor(arr, shift = 0) {
      let off = 0; while (off < arr.length && arr[off] === 0) off++;
      let core = Array.prototype.slice.call(arr, off);
      if (shift !== 0) core = core.concat(new Array(shift).fill(0));
      this._num = core;
    }
    at(i) { return i < this._num.length ? this._num[i] : 0; }
    get length() { return this._num.length; }
    get isZero() { return this._num.length === 0; }
    multiply(other) {
      const r = new Array(this.length + other.length - 1).fill(0);
      for (let i = 0; i < this.length; i++) {
        if (this._num[i] === 0) continue;
        for (let j = 0; j < other.length; j++) {
          if (other.at(j) === 0) continue;
          r[i + j] ^= Gf256.gexp(Gf256.glog(this._num[i]) + Gf256.glog(other.at(j)));
        }
      }
      return new GfPoly(r, 0);
    }
    mod(other) {
      let num = this._num;
      if (num.length === 0 || num.length < other.length) return new GfPoly(num, 0);
      const ratio = Gf256.glog(num[0]) - Gf256.glog(other.at(0));
      const r = new Array(num.length);
      for (let i = 0; i < other.length; i++)
        r[i] = num[i] ^ Gf256.gexp(Gf256.glog(other.at(i)) + ratio);
      for (let i = other.length; i < num.length; i++) r[i] = num[i];
      return new GfPoly(r, 0).mod(other);
    }
  }

  const ReedSolomon = (() => {
    const cache = new Map();
    function generator(ecCount) {
      if (cache.has(ecCount)) return cache.get(ecCount);
      let poly = new GfPoly([1], 0);
      for (let i = 0; i < ecCount; i++) poly = poly.multiply(new GfPoly([1, Gf256.gexp(i)], 0));
      cache.set(ecCount, poly);
      return poly;
    }
    return {
      encode(data, ecCount) {
        if (ecCount <= 0) return new Uint8Array(0);
        const raw = new GfPoly(Array.from(data), generator(ecCount).length - 1);
        const m = raw.mod(generator(ecCount));
        const out = new Uint8Array(ecCount);
        const off = m.length - ecCount;
        for (let i = 0; i < ecCount; i++) out[i] = m._num[i + off];
        return out;
      },
      interleave(blocks) { // blocks: [{data, ec}]
        const maxD = Math.max(...blocks.map(b => b.data.length));
        const maxE = Math.max(...blocks.map(b => b.ec.length));
        const out = [];
        for (let i = 0; i < maxD; i++) for (const b of blocks) if (i < b.data.length) out.push(b.data[i]);
        for (let i = 0; i < maxE; i++) for (const b of blocks) if (i < b.ec.length) out.push(b.ec[i]);
        return Uint8Array.from(out);
      },
      verify(data, ec) {
        if (ec.length === 0) return true;
        const poly = new GfPoly([...data, ...ec], 0);
        return poly.mod(generator(ec.length)).isZero;
      }
    };
  })();

  /* ================= ISO 18004 静态表 ================= */
  const RS_TABLE = [
    [1,26,19],[1,26,16],[1,26,13],[1,26,9],
    [1,44,34],[1,44,28],[1,44,22],[1,44,16],
    [1,70,55],[1,70,44],[2,35,17],[2,35,13],
    [1,100,80],[2,50,32],[2,50,24],[4,25,9],
    [1,134,108],[2,67,43],[2,33,15,2,34,16],[2,33,11,2,34,12],
    [2,86,68],[4,43,27],[4,43,19],[4,43,15],
    [2,98,78],[4,49,31],[2,32,14,4,33,15],[4,39,13,1,40,14],
    [2,121,97],[2,60,38,2,61,39],[4,40,18,2,41,19],[4,40,14,2,41,15],
    [2,146,116],[3,58,36,2,59,37],[4,36,16,4,37,17],[4,36,12,4,37,13],
    [2,86,68,2,87,69],[4,69,43,1,70,44],[6,43,19,2,44,20],[6,43,15,2,44,16],
    [4,101,81],[1,80,50,4,81,51],[4,50,22,4,51,23],[3,36,12,8,37,13],
    [2,116,92,2,117,93],[6,58,36,2,59,37],[4,46,20,6,47,21],[7,42,14,4,43,15],
    [4,133,107],[8,59,37,1,60,38],[8,44,20,4,45,21],[12,33,11,4,34,12],
    [3,145,115,1,146,116],[4,64,40,5,65,41],[11,36,16,5,37,17],[11,36,12,5,37,13],
    [5,109,87,1,110,88],[5,65,41,5,66,42],[5,54,24,7,55,25],[11,36,12,7,37,13],
    [5,122,98,1,123,99],[7,73,45,3,74,46],[15,43,19,2,44,20],[3,45,15,13,46,16],
    [1,135,107,5,136,108],[10,74,46,1,75,47],[1,50,22,15,51,23],[2,42,14,17,43,15],
    [5,150,120,1,151,121],[9,69,43,4,70,44],[17,50,22,1,51,23],[2,42,14,19,43,15],
    [3,141,113,4,142,114],[3,70,44,11,71,45],[17,47,21,4,48,22],[9,39,13,16,40,14],
    [3,135,107,5,136,108],[3,67,41,13,68,42],[15,54,24,5,55,25],[15,43,15,10,44,16],
    [4,144,116,4,145,117],[17,68,42],[17,50,22,6,51,23],[19,46,16,6,47,17],
    [2,139,111,7,140,112],[17,74,46],[7,54,24,16,55,25],[34,37,13],
    [4,151,121,5,152,122],[4,75,47,14,76,48],[11,54,24,14,55,25],[16,45,15,14,46,16],
    [6,147,117,4,148,118],[6,73,45,14,74,46],[11,54,24,16,55,25],[30,46,16,2,47,17],
    [8,132,106,4,133,107],[8,75,47,13,76,48],[7,54,24,22,55,25],[22,45,15,13,46,16],
    [10,142,114,2,143,115],[19,74,46,4,75,47],[28,50,22,6,51,23],[33,46,16,4,47,17],
    [8,152,122,4,153,123],[22,73,45,3,74,46],[8,53,23,26,54,24],[12,45,15,28,46,16],
    [3,147,117,10,148,118],[3,73,45,23,74,46],[4,54,24,31,55,25],[11,45,15,31,46,16],
    [7,146,116,7,147,117],[21,73,45,7,74,46],[1,53,23,37,54,24],[19,45,15,26,46,16],
    [5,145,115,10,146,116],[19,75,47,10,76,48],[15,54,24,25,55,25],[23,45,15,25,46,16],
    [13,145,115,3,146,116],[2,74,46,29,75,47],[42,54,24,1,55,25],[23,45,15,28,46,16],
    [17,145,115],[10,74,46,23,75,47],[10,54,24,35,55,25],[19,45,15,35,46,16],
    [17,145,115,1,146,116],[14,74,46,21,75,47],[29,54,24,19,55,25],[11,45,15,46,46,16],
    [13,145,115,6,146,116],[14,74,46,23,75,47],[44,54,24,7,55,25],[59,46,16,1,47,17],
    [12,151,121,7,152,122],[12,75,47,26,76,48],[39,54,24,14,55,25],[22,45,15,41,46,16],
    [6,151,121,14,152,122],[6,75,47,34,76,48],[46,54,24,10,55,25],[2,45,15,64,46,16],
    [17,152,122,4,153,123],[29,74,46,14,75,47],[49,54,24,10,55,25],[24,45,15,46,46,16],
    [4,152,122,18,153,123],[13,74,46,32,75,47],[48,54,24,14,55,25],[42,45,15,32,46,16],
    [20,147,117,4,148,118],[40,75,47,7,76,48],[43,54,24,22,55,25],[10,45,15,67,46,16],
    [19,148,118,6,149,119],[18,75,47,31,76,48],[34,54,24,34,55,25],[20,45,15,61,46,16],
  ];

  const ALIGN = [
    [],
    [6,18],[6,22],[6,26],[6,30],[6,34],[6,22,38],[6,24,42],[6,26,46],[6,28,50],
    [6,30,54],[6,32,58],[6,34,62],[6,26,46,66],[6,26,48,70],[6,26,50,74],[6,30,54,78],
    [6,30,56,82],[6,30,58,86],[6,34,62,90],[6,28,50,72,94],[6,26,50,74,98],[6,30,54,78,102],
    [6,28,54,80,106],[6,32,58,84,110],[6,30,58,86,114],[6,34,62,90,118],[6,26,50,74,98,122],
    [6,30,54,78,102,126],[6,26,52,78,104,130],[6,30,56,82,108,134],[6,34,60,86,112,138],
    [6,30,58,86,114,142],[6,34,62,90,118,146],[6,30,54,78,102,126,150],[6,24,50,76,102,128,154],
    [6,28,54,80,106,132,158],[6,32,58,84,110,136,162],[6,26,54,82,110,138,166],[6,30,58,86,114,142,170],
  ];

  const G15 = (1 << 10) | (1 << 8) | (1 << 5) | (1 << 4) | (1 << 2) | (1 << 1) | (1 << 0);
  const G18 = (1 << 12) | (1 << 11) | (1 << 10) | (1 << 9) | (1 << 8) | (1 << 5) | (1 << 2) | (1 << 0);
  const FORMAT_MASK = (1 << 14) | (1 << 12) | (1 << 10) | (1 << 4) | (1 << 1);
  const LEVEL_OFFSET = { 1: 0, 0: 1, 3: 2, 2: 3 }; // L/M/Q/H
  const MODE_BITS = { 0: 0b0001, 1: 0b0010, 2: 0b0100, 3: 0b1000 }; // Numeric/Alphanumeric/Byte/Kanji

  function checkVersion(v) {
    if (v < 1 || v > 40) throw new RangeError(`无效版本 ${v}（QR 1-40）`);
  }
  function rsBlocks(version, level) {
    checkVersion(version);
    const row = RS_TABLE[(version - 1) * 4 + LEVEL_OFFSET[level]];
    const blocks = [];
    for (let i = 0; i < row.length; i += 3)
      for (let k = 0; k < row[i]; k++) blocks.push({ total: row[i + 1], data: row[i + 2] });
    return blocks;
  }
  function dataBitCapacity(version, level) {
    return rsBlocks(version, level).reduce((s, b) => s + b.data * 8, 0);
  }
  function lengthBits(mode, version) {
    checkVersion(version);
    if (version < 10) return [10, 9, 8, 8][mode];
    if (version < 27) return [12, 11, 16, 10][mode];
    return [14, 13, 16, 12][mode];
  }
  function bitLen(x) { let n = 0; while (x !== 0) { n++; x >>= 1; } return n; }

  /* ================= BCH：格式/版本信息 ================= */
  const QrBch = {
    formatBits(level, mask) {
      if (mask < 0 || mask > 7) throw new RangeError("掩码 0-7");
      const ecBits = { 1: 0b01, 0: 0b00, 3: 0b11, 2: 0b10 }[level];
      const data = (ecBits << 3) | mask;
      let d = data << 10;
      while (bitLen(d) - bitLen(G15) >= 0) d ^= G15 << (bitLen(d) - bitLen(G15));
      return ((data << 10) | d) ^ FORMAT_MASK;
    },
    versionBits(version) {
      checkVersion(version);
      if (version < 7) return 0;
      let d = version << 12;
      while (bitLen(d) - bitLen(G18) >= 0) d ^= G18 << (bitLen(d) - bitLen(G18));
      return (version << 12) | d;
    }
  };

  /* ================= 数据段（自动选模式） ================= */
  const ALNUM_TABLE = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";
  const ALNUM_RE = /^[0-9A-Z $%*+\-/:.]+$/;

  class QrDataChunk {
    constructor(mode, data, charCount) {
      this.mode = mode;
      this.data = Uint8Array.from(data);
      this.charCount = charCount != null ? charCount
        : (mode === 2 ? this.data.length : decodeUtf8(this.data).length);
    }
    static auto(text) {
      if (text.length === 0) return new QrDataChunk(2, []);
      if (/^[0-9]+$/.test(text)) return new QrDataChunk(0, utf8Bytes(text));
      if (ALNUM_RE.test(text)) return new QrDataChunk(1, utf8Bytes(text));
      return new QrDataChunk(2, utf8Bytes(text));
    }
    get payloadBits() {
      if (this.mode === 0) {
        const c = this.charCount;
        return Math.floor(c / 3) * 10 + (c % 3 === 1 ? 4 : c % 3 === 2 ? 7 : 0);
      }
      if (this.mode === 1) return Math.floor(this.charCount / 2) * 11 + (this.charCount % 2) * 6;
      return this.data.length * 8;
    }
    write(buffer, version) {
      buffer.put(MODE_BITS[this.mode], 4);
      buffer.put(this.charCount, lengthBits(this.mode, version));
      if (this.mode === 0) {
        const s = decodeUtf8(this.data);
        for (let i = 0; i < this.charCount; i += 3) {
          const n = Math.min(3, this.charCount - i);
          buffer.put(parseInt(s.substring(i, i + n), 10), n === 1 ? 4 : n === 2 ? 7 : 10);
        }
      } else if (this.mode === 1) {
        const s = decodeUtf8(this.data);
        for (let i = 0; i < this.charCount; i += 2) {
          if (i + 1 < this.charCount)
            buffer.put(ALNUM_TABLE.indexOf(s[i]) * 45 + ALNUM_TABLE.indexOf(s[i + 1]), 11);
          else
            buffer.put(ALNUM_TABLE.indexOf(s[i]), 6);
        }
      } else {
        for (const b of this.data) buffer.put(b, 8);
      }
    }
  }

  /* ================= 核心编码器 ================= */
  const QrEncoder = {
    bestVersion(chunks, level, start = 1) {
      for (let v = start; v <= 40; v++) {
        const needed = chunks.reduce((s, c) => s + 4 + lengthBits(c.mode, v) + c.payloadBits, 0);
        if (needed <= dataBitCapacity(v, level)) return v;
      }
      throw new Error(`数据过大：无法装入 QR 版本 40（纠错等级 ${level}）`);
    },
    encode(chunks, level, version) {
      if (chunks.length === 0) throw new Error("数据不能为空");
      const v = version != null ? version : QrEncoder.bestVersion(chunks, level);
      checkVersion(v);
      if (version != null && version < QrEncoder.bestVersion(chunks, level))
        throw new Error(`数据需要版本 ${QrEncoder.bestVersion(chunks, level)}，超出指定的版本 ${version}`);

      const bitLimit = dataBitCapacity(v, level);
      const buffer = new BitBuffer();
      for (const c of chunks) c.write(buffer, v);
      const payloadBits = buffer.bitLength;

      for (let i = 0; i < Math.min(bitLimit - buffer.bitLength, 4); i++) buffer.putBit(false);
      const rem = buffer.bitLength % 8;
      if (rem !== 0) for (let i = rem; i < 8; i++) buffer.putBit(false);
      const fill = (bitLimit - buffer.bitLength) / 8;
      for (let i = 0; i < fill; i++) buffer.put(i % 2 === 0 ? 0xEC : 0x11, 8);

      const bytes = buffer.toBytes();
      if (bytes.length !== bitLimit / 8) throw new Error("内部错误：缓冲字节数与容量不符");

      const blocks = rsBlocks(v, level);
      const pairs = [];
      let off = 0;
      for (const b of blocks) {
        const data = bytes.slice(off, off + b.data);
        off += b.data;
        pairs.push({ data, ec: ReedSolomon.encode(data, b.total - b.data) });
      }
      if (off !== bytes.length) throw new Error("内部错误：RS 块总容量与数据不符");
      return { version: v, level, chunks, codeWords: ReedSolomon.interleave(pairs), blocks, payloadBits };
    }
  };

  /* ================= 矩阵构建 ================= */
  /* 8 种掩码函数（ISO 18004 表 10），(行, 列) → 是否取反 */
  const MASKS = [
    (i, j) => (i + j) % 2 === 0,
    (i) => i % 2 === 0,
    (_, j) => j % 3 === 0,
    (i, j) => (i + j) % 3 === 0,
    (i, j) => (Math.floor(i / 2) + Math.floor(j / 3)) % 2 === 0,
    (i, j) => (i * j) % 2 + (i * j) % 3 === 0,
    (i, j) => ((i * j) % 2 + (i * j) % 3) % 2 === 0,
    (i, j) => ((i * j) % 3 + (i + j) % 2) % 2 === 0,
  ];

  const _blankCache = new Map();
  function buildBlank(version) {
    if (_blankCache.has(version)) return cloneMatrix(_blankCache.get(version));
    const n = version * 4 + 17;
    const m = { n, version, maskPattern: 0, modules: new Array(n * n).fill(null) };
    const set = (r, c, v) => { m.modules[r * n + c] = v; };

    const finder = (top, left) => {
      for (let r = -1; r <= 7; r++) for (let c = -1; c <= 7; c++) {
        const row = top + r, col = left + c;
        if (row < 0 || row >= n || col < 0 || col >= n) continue;
        set(row, col,
          (r >= 0 && r <= 6 && (c === 0 || c === 6)) ||
          (c >= 0 && c <= 6 && (r === 0 || r === 6)) ||
          (r >= 2 && r <= 4 && c >= 2 && c <= 4));
      }
    };
    finder(0, 0); finder(n - 7, 0); finder(0, n - 7);

    const pos = ALIGN[version - 1];
    for (const r of pos) for (const c of pos) {
      if ((r < 9 && c < 9) || (r < 9 && c >= n - 9) || (r >= n - 9 && c < 9)) continue;
      for (let dr = -2; dr <= 2; dr++) for (let dc = -2; dc <= 2; dc++)
        set(r + dr, c + dc, Math.max(Math.abs(dr), Math.abs(dc)) !== 1);
    }

    for (let i = 8; i < n - 8; i++) { set(i, 6, i % 2 === 0); set(6, i, i % 2 === 0); }
    if (version >= 7) {
      const bits = QrBch.versionBits(version);
      for (let i = 0; i < 18; i++) {
        const dark = ((bits >> i) & 1) === 1;
        set(Math.floor(i / 3), (i % 3) + n - 8 - 3, dark);
        set((i % 3) + n - 8 - 3, Math.floor(i / 3), dark);
      }
    }
    set(n - 8, 8, false);
    // 格式信息区（30 格）以浅色占位：数据放置跳过，评分阶段与 python-qrcode 的 test=True 一致
    for (const [r, c] of formatCells(n)) set(r, c, false);

    const clone = cloneMatrix(m);
    _blankCache.set(version, clone);
    return m;
  }

  function cloneMatrix(m) {
    const c = { n: m.n, version: m.version, maskPattern: m.maskPattern, modules: m.modules.slice() };
    return c;
  }

  function mapData(m, codeWords, maskPattern) {
    const n = m.n, mask = MASKS[maskPattern];
    let byteIndex = 0, bitIndex = 7, upward = true;
    for (let col = n - 1; col > 0; col -= 2) {
      const c = col <= 6 ? col - 1 : col; // 跳过中间时序列（列 6）
      let row = upward ? n - 1 : 0, inc = upward ? -1 : 1;
      while (true) {
        for (let cc = 0; cc < 2; cc++) {
          const x = c - cc;
          if (m.modules[row * n + x] !== null) continue;
          let dark = false;
          if (byteIndex < codeWords.length) {
            dark = ((codeWords[byteIndex] >> bitIndex) & 1) === 1;
            bitIndex--; if (bitIndex === -1) { bitIndex = 7; byteIndex++; }
          }
          if (mask(row, x)) dark = !dark;
          m.modules[row * n + x] = dark;
        }
        row += inc;
        if (row < 0 || row >= n) { row -= inc; inc = -inc; break; }
      }
      upward = !upward;
    }
  }

  // python-qrcode lost_point_level3 的两个 11 模块窗口（1=深色）：
  //   pattern1 = 10111010000（右侧带 4 格浅色）
  //   pattern2 = 00001011101（左侧带 4 格浅色）
  const PATTERN1 = [1, 0, 1, 1, 1, 0, 1, 0, 0, 0, 0];
  const PATTERN2 = [0, 0, 0, 0, 1, 0, 1, 1, 1, 0, 1];

  function l3Match(m, row, col, horizontal) {
    const n = m.n;
    const V = (off) => horizontal ? m.modules[row * n + col + off] : m.modules[(row + off) * n + col];
    const b = [];
    for (let i = 0; i < 11; i++) b.push(V(i) ? 1 : 0);
    return PATTERN1.every((v, i) => b[i] === v) || PATTERN2.every((v, i) => b[i] === v);
  }

  function lostPoints(m) {
    const n = m.n;
    let lost = 0;
    const buckets = new Array(n + 1).fill(0);
    const runScore = (get, total) => {
      let prev = get(0), len = 0;
      for (let i = 0; i < total; i++) {
        if (get(i) === prev) len++;
        else { if (len >= 5) buckets[len]++; len = 1; prev = get(i); }
      }
      if (len >= 5) buckets[len]++;
    };
    for (let r = 0; r < n; r++) runScore(c => m.modules[r * n + c], n);
    for (let c = 0; c < n; c++) runScore(r => m.modules[r * n + c], n);
    for (let len = 5; len <= n; len++) lost += buckets[len] * (len - 2);

    // 规则 2：沿用 python-qrcode 的跳格优化（右上不同则跳过下一列）
    for (let r = 0; r < n - 1; r++) {
      for (let c = 0; c < n - 1; c++) {
        const at = (rr, cc) => m.modules[rr * n + cc];
        const topRight = at(r, c + 1);
        if (topRight !== at(r + 1, c + 1)) c++;
        else if (topRight !== at(r, c)) continue;
        else if (topRight !== at(r + 1, c)) continue;
        else lost += 3;
      }
    }

    // 规则 3：含 python-qrcode 的 Horspool 跳格
    for (let r = 0; r < n; r++)
      for (let c = 0; c <= n - 11; c++) {
        if (l3Match(m, r, c, true)) lost += 40;
        if (m.modules[r * n + c + 10]) c++;
      }
    for (let c = 0; c < n; c++)
      for (let r = 0; r <= n - 11; r++) {
        if (l3Match(m, r, c, false)) lost += 40;
        if (m.modules[(r + 10) * n + c]) r++;
      }

    // 规则 4：浮点占比，与 python-qrcode 一致
    let dark = 0;
    for (const v of m.modules) if (v) dark++;
    lost += Math.floor(Math.abs(dark / (n * n) * 100 - 50) / 5) * 10;
    return lost;
  }

  /* 格式信息的 30 个模块位：前 15 个竖直区（列 8），后 15 个水平区（行 8），顺序对应 bit0..bit14 */
  function formatCells(n) {
    const cells = [];
    for (let i = 0; i < 15; i++)
      cells.push(i < 6 ? [i, 8] : i < 8 ? [i + 1, 8] : [n - 15 + i, 8]);
    for (let i = 0; i < 15; i++)
      cells.push(i < 8 ? [8, n - 1 - i] : i < 9 ? [8, 7] : [8, 15 - i - 1]);
    return cells;
  }

  function drawFormatBits(m, formatBits15) {
    const n = m.n;
    const cells = formatCells(n);
    for (let k = 0; k < cells.length; k++)
      m.modules[cells[k][0] * n + cells[k][1]] = ((formatBits15 >> (k % 15)) & 1) === 1;
    m.modules[(n - 8) * n + 8] = true; // 暗色模块
  }

  function buildMatrix(encoded) {
    let bestMask = 0, bestScore = Infinity;
    for (let mask = 0; mask < 8; mask++) {
      const m = buildBlank(encoded.version);
      mapData(m, encoded.codeWords, mask);
      const score = lostPoints(m);
      if (score < bestScore) { bestScore = score; bestMask = mask; }
    }
    const final = buildBlank(encoded.version);
    mapData(final, encoded.codeWords, bestMask);
    drawFormatBits(final, QrBch.formatBits(encoded.level, bestMask));
    final.maskPattern = bestMask;
    return final;
  }

  /* ================= ISO/IEC 21570 元数据 ================= */
  const SHA256_K = new Uint32Array([
    0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
    0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
    0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
    0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
    0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
    0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
    0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
    0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2]);
  const rotR = (x, n) => ((x >>> n) | (x << (32 - n))) >>> 0;

  function sha256Hex(str) {
    const msg = new TextEncoder().encode(str);
    const bitLen = msg.length * 8;
    const padded = new Uint8Array((((msg.length + 8) >> 6) + 1) << 6);
    padded.set(msg); padded[msg.length] = 0x80;
    new DataView(padded.buffer).setBigUint64(padded.length - 8, BigInt(bitLen), false);
    const h = new Uint32Array([0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19]);
    const w = new Uint32Array(64);
    for (let off = 0; off < padded.length; off += 64) {
      for (let i = 0; i < 16; i++) w[i] = (padded[off + i * 4] << 24) | (padded[off + i * 4 + 1] << 16) | (padded[off + i * 4 + 2] << 8) | padded[off + i * 4 + 3];
      for (let i = 16; i < 64; i++) {
        const s0 = rotR(w[i - 15], 7) ^ rotR(w[i - 15], 18) ^ (w[i - 15] >>> 3);
        const s1 = rotR(w[i - 2], 17) ^ rotR(w[i - 2], 19) ^ (w[i - 2] >>> 10);
        w[i] = (w[i - 16] + s0 + w[i - 7] + s1) >>> 0;
      }
      let [a, b, c, d, e, f, g, hh] = h;
      for (let i = 0; i < 64; i++) {
        const S1 = rotR(e, 6) ^ rotR(e, 11) ^ rotR(e, 25);
        const ch = (e & f) ^ (~e & g);
        const t1 = (hh + S1 + ch + SHA256_K[i] + w[i]) >>> 0;
        const S0 = rotR(a, 2) ^ rotR(a, 13) ^ rotR(a, 22);
        const maj = (a & b) ^ (a & c) ^ (b & c);
        const t2 = (S0 + maj) >>> 0;
        hh = g; g = f; f = e; e = (d + t1) >>> 0; d = c; c = b; b = a; a = (t1 + t2) >>> 0;
      }
      h[0] = (h[0] + a) >>> 0; h[1] = (h[1] + b) >>> 0; h[2] = (h[2] + c) >>> 0; h[3] = (h[3] + d) >>> 0;
      h[4] = (h[4] + e) >>> 0; h[5] = (h[5] + f) >>> 0; h[6] = (h[6] + g) >>> 0; h[7] = (h[7] + hh) >>> 0;
    }
    return Array.from(h, x => x.toString(16).padStart(8, "0")).join("");
  }

  function jsonEscape(s) {
    return String(s)
      .replace(/\\/g, "\\\\").replace(/"/g, '\\"')
      .replace(/\n/g, "\\n").replace(/\r/g, "\\r").replace(/\t/g, "\\t");
  }
  // 注意：键按字典序排序以保证跨语言（C#/JS）输出一致
  function encodeJson(dict) {
    const keys = Object.keys(dict).sort();
    const parts = keys.map(k => `"${jsonEscape(k)}":${dict[k] == null ? "null" : `"${jsonEscape(dict[k])}"`}`);
    return "{" + parts.join(",") + "}";
  }

  const Iso21570 = {
    SEGMENT_HEADER: [0x00, 0x00, 0xA1, 0x00],
    buildSegment(metadata) {
      const keys = Object.keys(metadata);
      if (keys.length === 0) return new Uint8Array(0);
      const json = new TextEncoder().encode(encodeJson(metadata));
      const out = new Uint8Array(4 + 2 + json.length);
      out.set(Iso21570.SEGMENT_HEADER, 0);
      out[4] = json.length >> 8; out[5] = json.length & 0xFF;
      out.set(json, 6);
      return out;
    },
    contentHash(content) { return sha256Hex(content); }
  };

  /* ================= UTF-8 工具 ================= */
  function utf8Bytes(s) { return new TextEncoder().encode(s); }
  function decodeUtf8(bytes) { return new TextDecoder().decode(bytes); }

  /* python-qrcode 的 optimize=4 等价切分（optimal_data_chunks + _optimal_split）：
     在 UTF-8 字节流上先取最长数字串（≥minimum），再对剩余段取最长字母数字串，其余为字节段。 */
  function splitOptimal(text, minimum = 4) {
    const bytes = utf8Bytes(text);
    const anchored = bytes.length <= minimum; // 短输入时 python 用锚定正则
    const isDigit = (b) => b >= 0x30 && b <= 0x39;
    const isAlnum = (b) => b < 0x80 && ALNUM_TABLE.indexOf(String.fromCharCode(b)) >= 0;
    const chunks = [];
    for (const [matched, seg] of splitRuns(bytes, isDigit, minimum, anchored)) {
      if (matched) { chunks.push(new QrDataChunk(0, seg)); continue; }
      for (const [alpha, sub] of splitRuns(seg, isAlnum, minimum, anchored))
        chunks.push(new QrDataChunk(alpha ? 1 : 2, sub));
    }
    return chunks;
  }

  /* 左most-最长游标切分，等价于对 /\d{4,}/ 的迭代 re.search；未命中的间隙按原序输出 */
  function splitRuns(data, predicate, minimum, anchored) {
    if (data.length === 0) return [];
    if (anchored) {
      const all = data.every(predicate);
      return [[all, data]];
    }
    const parts = [];
    let cursor = 0, i = 0;
    while (i < data.length) {
      let run = i;
      while (run < data.length && predicate(data[run])) run++;
      if (run - i >= minimum) {
        if (i > cursor) parts.push([false, data.slice(cursor, i)]);
        parts.push([true, data.slice(i, run)]);
        cursor = i = run;
      } else {
        i = run > i ? run : i + 1; // 短游程内的起点同样无法构成命中，整体跳过
      }
    }
    if (cursor < data.length) parts.push([false, data.slice(cursor)]);
    return parts;
  }

  /* ================= 颜色工具 ================= */
  function parseColor(str, fallback) {
    const f = fallback || { r: 0, g: 0, b: 0, a: 1 };
    if (typeof str !== "string") return f;
    const s = str.trim();
    if (s[0] === "#") {
      let hex = s.slice(1);
      if (hex.length === 3) hex = hex.split("").map(c => c + c).join("");
      if (hex.length >= 6) {
        const n = parseInt(hex.slice(0, 6), 16);
        return { r: (n >> 16) & 255, g: (n >> 8) & 255, b: n & 255, a: 1 };
      }
    }
    const m = s.match(/rgba?\(([^)]+)\)/);
    if (m) {
      const p = m[1].split(/[, ]+/).map(parseFloat);
      return { r: p[0] || 0, g: p[1] || 0, b: p[2] || 0, a: p.length > 3 ? p[3] : 1 };
    }
    return f;
  }
  const blend = (fg, bg) => ({
    r: Math.round(fg.r * fg.a + bg.r * (1 - fg.a)),
    g: Math.round(fg.g * fg.a + bg.g * (1 - fg.a)),
    b: Math.round(fg.b * fg.a + bg.b * (1 - fg.a)), a: 255
  });

  /* ================= 主类 ================= */
  class CqrcCode {
    constructor(options = {}) {
      this.errorCorrection = options.errorCorrection != null ? options.errorCorrection : EC.M;
      this.border = options.border != null ? options.border : 4;
      this.version = options.version != null ? options.version : null;
      this.optimize = !!options.optimize; // 按 python-qrcode optimize=4 规则切分数据段
      this.style = Object.assign({
        foregroundColor: "#1A1A1A",
        backgroundColor: "#FFFFFF",
        foregroundColor2: null,
        gradientAngle: 0,
        moduleRounding: 0,
        circularFinders: false,   // 用圆形替代三个角的方形定位标记
        logo: null, logoRatio: 0.15, logoPadding: 0.03,
        frameRounding: 0,
        metadata: null,
        includeContentHash: true
      }, options.style || {});
      this._texts = [];
      this._matrix = null;
    }
    addData(text) {
      if (!text) throw new Error("数据不能为空");
      this._texts.push(text);
      return this;
    }
    get actualVersion() { return this._matrix ? this._matrix.version : null; }
    get maskPattern() { return this._matrix ? this._matrix.maskPattern : null; }
    generate() {
      if (this._texts.length === 0) throw new Error("没有数据，先 addData()");
      const st = this.style;
      const full = this._texts.join("");
      if (st.includeContentHash && (!st.metadata || !("content_hash" in st.metadata))) {
        if (!st.metadata) st.metadata = {};
        st.metadata.content_hash = Iso21570.contentHash(full);
      }
      const chunks = this.optimize ? splitOptimal(full) : this._texts.map(t => QrDataChunk.auto(t));
      if (st.metadata && Object.keys(st.metadata).length > 0)
        chunks.push(new QrDataChunk(2, Iso21570.buildSegment(st.metadata)));
      const encoded = QrEncoder.encode(chunks, this.errorCorrection, this.version);
      this._matrix = buildMatrix(encoded);
      return this._matrix;
    }
    /** 完整矩阵（含白边）。border=0 时返回裸矩阵。true=深色 */
    fullMatrix(border) {
      const m = this.generate();
      const b = border != null ? border : this.border;
      const n = m.n, w = n + b * 2;
      const full = new Array(w * w).fill(false);
      for (let r = 0; r < n; r++) for (let c = 0; c < n; c++)
        if (m.modules[r * n + c]) full[(r + b) * w + (c + b)] = true;
      return { size: w, get: (r, c) => full[r * w + c] };
    }
    toSvg(boxSize = 10) { return renderSvg(this, boxSize); }
    toCanvas(canvas, boxSize = 10) { return renderCanvas(this, canvas, boxSize); }
    toDataUrl(boxSize = 10) { return "data:image/svg+xml;utf8," + encodeURIComponent(this.toSvg(boxSize)); }
  }

  /* ================= SVG 渲染 ================= */
  function renderSvg(code, boxSize) {
    const st = code.style;
    const full = code.fullMatrix(code.border);
    const n = full.size, border = code.border, size = n * boxSize;
    const out = [];
    out.push(`<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${size} ${size}">`);
    const gradient = st.foregroundColor2 != null;
    if (gradient) {
      out.push(`  <defs><linearGradient id="fg" x1="0%" y1="0%" x2="100%" y2="100%">`);
      out.push(`    <stop offset="0%" stop-color="${st.foregroundColor}"/>`);
      out.push(`    <stop offset="100%" stop-color="${st.foregroundColor2}"/>`);
      out.push(`  </linearGradient></defs>`);
    }
    out.push(`  <rect width="${size}" height="${size}" fill="${st.backgroundColor}"/>`);
    const color = gradient ? "url(#fg)" : st.foregroundColor;
    out.push(`  <g fill="${color}">`);
    const corners = [[border, border], [n - border - 7, border], [border, n - border - 7]];
    for (let r = 0; r < n; r++) for (let c = 0; c < n; c++) {
      if (!full.get(r, c)) continue;
      if (st.circularFinders) {
        const inFinder = corners.some(([cr, cc]) =>
          r >= cr && r < cr + 7 && c >= cc && c < cc + 7);
        if (inFinder) continue;
      }
      const x = c * boxSize, y = r * boxSize;
      const rx = st.moduleRounding > 0.001 ? ` rx="${Math.floor(st.moduleRounding * boxSize)}"` : "";
      out.push(`    <rect x="${x}" y="${y}" width="${boxSize}" height="${boxSize}"${rx}/>`);
    }
    if (st.circularFinders) {
      for (const [cr, cc] of corners) {
        const cx = (cc + 3.5) * boxSize, cy = (cr + 3.5) * boxSize;
        out.push(`    <circle cx="${cx}" cy="${cy}" r="${3.5 * boxSize}"/>`);
        out.push(`    <circle cx="${cx}" cy="${cy}" r="${2.5 * boxSize}" fill="${st.backgroundColor}"/>`);
        out.push(`    <circle cx="${cx}" cy="${cy}" r="${1.5 * boxSize}" fill="${color}"/>`);
      }
    }
    out.push(`  </g>`);
    out.push(`</svg>`);
    return out.join("\n");
  }

  /* ================= Canvas 渲染 ================= */
  function renderCanvas(code, canvas, boxSize) {
    const st = code.style;
    const full = code.fullMatrix(code.border);
    const n = full.size, border = code.border, size = n * boxSize;
    canvas.width = size; canvas.height = size;
    const ctx = canvas.getContext("2d");
    const img = ctx.createImageData(size, size);
    const data = img.data;

    const bg = parseColor(st.backgroundColor, { r: 255, g: 255, b: 255, a: 1 });
    const fg1 = parseColor(st.foregroundColor, { r: 26, g: 26, b: 26, a: 1 });
    const fg2 = st.foregroundColor2 != null ? parseColor(st.foregroundColor2) : null;
    const roundFrame = st.frameRounding > 0.001;
    const frameRadius = Math.floor(st.frameRounding * size);
    const roundModules = st.moduleRounding > 0.001;
    const modRadius = Math.floor(st.moduleRounding * boxSize);
    const corners = [[border * boxSize, border * boxSize],
      [(n - border - 7) * boxSize, border * boxSize],
      [border * boxSize, (n - border - 7) * boxSize]];
    const dot = 1.5 * boxSize, ringIn = 2.5 * boxSize, outer = 3.5 * boxSize;

    for (let y = 0; y < size; y++) {
      for (let x = 0; x < size; x++) {
        let fill = null;
        if (roundFrame) {
          const cx = x < frameRadius ? frameRadius : (x >= size - frameRadius ? size - 1 - frameRadius : -1);
          const cy = y < frameRadius ? frameRadius : (y >= size - frameRadius ? size - 1 - frameRadius : -1);
          if (cx >= 0 && cy >= 0) {
            const dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy > frameRadius * frameRadius) continue;
          }
        }
        const row = Math.floor(y / boxSize), col = Math.floor(x / boxSize);
        if (st.circularFinders) {
          let inFinder = false, dark = false;
          for (const [fx, fy] of corners) {
            const lx = x - fx, ly = y - fy;
            if (lx < 0 || lx >= 7 * boxSize || ly < 0 || ly >= 7 * boxSize) continue;
            inFinder = true;
            const cx = lx - 3.5 * boxSize, cy = ly - 3.5 * boxSize;
            const d2 = cx * cx + cy * cy;
            dark = d2 <= dot * dot || (d2 >= ringIn * ringIn && d2 <= outer * outer);
            break;
          }
          fill = (inFinder ? dark : full.get(row, col)) ? 1 : 0;
        } else {
          fill = full.get(row, col) ? 1 : 0;
        }
        if (!fill) continue;
        if (roundModules) {
          const lx = x - col * boxSize, ly = y - row * boxSize;
          const cx = lx < modRadius ? modRadius : (lx >= boxSize - modRadius ? boxSize - 1 - modRadius : -1);
          const cy = ly < modRadius ? modRadius : (ly >= boxSize - modRadius ? boxSize - 1 - modRadius : -1);
          if (cx >= 0 && cy >= 0) {
            const dx = lx - cx, dy = ly - cy;
            if (dx * dx + dy * dy > modRadius * modRadius) continue;
          }
        }
        let color;
        if (fg2) {
          let t = (x + y) / (2 * (size - 1));
          if (st.gradientAngle >= 45 && st.gradientAngle < 135) t = 1 - t;
          color = {
            r: Math.round(fg1.r + (fg2.r - fg1.r) * t),
            g: Math.round(fg1.g + (fg2.g - fg1.g) * t),
            b: Math.round(fg1.b + (fg2.b - fg1.b) * t), a: 1
          };
        } else color = fg1;
        const c = blend(color, bg);
        const p = (y * size + x) * 4;
        data[p] = c.r; data[p + 1] = c.g; data[p + 2] = c.b; data[p + 3] = 255;
      }
    }
    ctx.putImageData(img, 0, 0);

    // Logo 叠加
    if (st.logo) {
      const pad = Math.floor(st.logoPadding * size);
      const ratio = Math.floor(st.logoRatio * n);
      const px = ratio * boxSize;
      const x0 = (size - px) / 2 - pad, y0 = (size - px) / 2 - pad;
      const total = px + pad * 2;
      ctx.save();
      ctx.beginPath();
      const rr = total / 4;
      ctx.moveTo(x0 + rr, y0);
      ctx.arcTo(x0 + total, y0, x0 + total, y0 + total, rr);
      ctx.arcTo(x0 + total, y0 + total, x0, y0 + total, rr);
      ctx.arcTo(x0, y0 + total, x0, y0, rr);
      ctx.arcTo(x0, y0, x0 + total, y0, rr);
      ctx.closePath();
      ctx.fillStyle = "#FFFFFF";
      ctx.fill();
      ctx.clip();
      const img2 = st.logo instanceof HTMLImageElement ? st.logo : null;
      if (img2 && img2.complete) ctx.drawImage(img2, x0 + 1, y0 + 1, total - 2, total - 2);
      ctx.restore();
    }
    return canvas;
  }

  return {
    EC, CqrcCode, CqrcDataChunk: QrDataChunk, QrEncoder, ReedSolomon, sha256Hex, Iso21570,
    splitOptimal, QrMatrixBuilder: { buildBlank, mapData, buildMatrix, lostPoints }, parseColor
  };
});
