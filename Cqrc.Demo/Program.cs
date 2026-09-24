using System.Text;
using Cqrc;

class D
{
    static List<QrDataChunk> Split(string data)
    {
        var chunks = new List<QrDataChunk>();
        int i = 0;
        while (i < data.Length)
        {
            int j = i;
            while (j < data.Length && char.IsDigit(data[j])) j++;
            if (j - i >= 4)
            {
                chunks.Add(new QrDataChunk(QrMode.Numeric, Encoding.UTF8.GetBytes(data[i..j])));
                i = j; continue;
            }
            int k = i;
            while (k < data.Length && Alphanumeric.Table.Contains(data[k])) k++;
            if (k - i >= 4)
            {
                chunks.Add(new QrDataChunk(QrMode.Alphanumeric, Encoding.UTF8.GetBytes(data[i..k])));
                i = k; continue;
            }
            int l = i + 1;
            while (l < data.Length)
            {
                int m2 = l;
                while (m2 < data.Length && Alphanumeric.Table.Contains(data[m2])) m2++;
                if (m2 - l >= 4) break;
                int num = l;
                while (num < data.Length && char.IsDigit(data[num])) num++;
                if (num - l >= 4) break;
                l++;
            }
            chunks.Add(new QrDataChunk(QrMode.Byte, Encoding.UTF8.GetBytes(data[i..l])));
            i = l;
        }
        return chunks;
    }

    static void Main()
    {
        var data = string.Concat(Enumerable.Repeat("CQRC v1: 1234567890123456789012345678901234567890", 8)) + "END";
        var chunks = Split(data);
        foreach (var c in chunks)
            System.Console.WriteLine($"{c.Mode} chars={c.CharCount} bytes={c.Data.Length} payload={c.PayloadBits}");
        int needed = chunks.Sum(c => 4 + QrTables.LengthBits(c.Mode, 11) + c.PayloadBits);
        System.Console.WriteLine($"needed@v11={needed} cap={QrTables.DataBitCapacity(11, ErrorCorrectionLevel.M)}");
        System.Console.WriteLine($"BestVersion={QrEncoder.BestVersion(chunks, ErrorCorrectionLevel.M)}");
        foreach (var b in QrTables.RsBlocks(11, ErrorCorrectionLevel.M))
            System.Console.WriteLine($"block total={b.TotalCount} data={b.DataCount}");
    }
}
