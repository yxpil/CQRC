namespace Cqrc;

/// <summary>QR 错误纠正等级。H 可恢复约 30% 数据损坏（嵌入 Logo 时建议 H）。</summary>
public enum ErrorCorrectionLevel
{
    /// <summary>L：约 7% 可恢复。</summary>
    L = 1,

    /// <summary>M：约 15% 可恢复（默认）。</summary>
    M = 0,

    /// <summary>Q：约 25% 可恢复。</summary>
    Q = 3,

    /// <summary>H：约 30% 可恢复。</summary>
    H = 2,
}

/// <summary>QR 数据编码模式。</summary>
public enum QrMode
{
    /// <summary>数字模式，3 个字符 10 bit，最紧凑。</summary>
    Numeric,

    /// <summary>字母数字模式，2 个字符 11 bit。</summary>
    Alphanumeric,

    /// <summary>8-bit 字节模式（UTF-8）。</summary>
    Byte,

    /// <summary>汉字节模式（Shift-JIS，本 SDK 不做自动拆分，但支持显式传入）。</summary>
    Kanji,
}

/// <summary>数据段模式指示符。</summary>
public static class QrModeBits
{
    public const int Numeric = 0b0001;
    public const int Alphanumeric = 0b0010;
    public const int Byte = 0b0100;
    public const int Kanji = 0b1000;
}
