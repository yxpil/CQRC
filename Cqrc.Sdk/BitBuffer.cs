namespace Cqrc;

/// <summary>位缓冲：MSB-first 位流。</summary>
public sealed class BitBuffer
{
    private readonly List<int> _bytes = new();

    public int BitLength { get; private set; }

    public void Put(int value, int bits)
    {
        for (int i = bits - 1; i >= 0; i--)
            PutBit(((value >> i) & 1) == 1);
    }

    public void PutBit(bool bit)
    {
        int byteIndex = BitLength / 8;
        if (byteIndex >= _bytes.Count) _bytes.Add(0);
        if (bit) _bytes[byteIndex] |= 0x80 >> (BitLength % 8);
        BitLength++;
    }

    public byte[] ToBytes()
    {
        var result = new byte[_bytes.Count];
        for (int i = 0; i < result.Length; i++) result[i] = (byte)_bytes[i];
        return result;
    }
}
