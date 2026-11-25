using System.Text;

#pragma warning disable CA5394 // The data of the benchmarks is not security sensitive

namespace Meziantou.Framework.Compression.Benchmarks;

internal static class BenchmarkData
{
    public const int Size = 2 * 1024 * 1024;

    private static readonly string[] Words =
    [
        "the", "of", "and", "to", "in", "a", "is", "that", "for", "it", "as", "was", "with", "be", "by", "on", "not", "he", "this", "are",
        "or", "his", "from", "at", "which", "but", "have", "an", "had", "they", "you", "were", "their", "one", "all", "we", "can", "her", "has", "there",
        "been", "if", "more", "when", "will", "would", "who", "so", "no", "archive", "compression", "stream", "buffer", "decoder", "encoder", "window", "dictionary", "symbol", "probability", "entropy",
        "Huffman", "range", "literal", "match", "distance", "length", "block", "header", "checksum", "folder", "entry", "volume", "filter", "branch", "delta", "context",
    ];

    public static byte[] Create(DataKind kind) => kind switch
    {
        DataKind.Text => CreateText(),
        _ => CreateBinary(),
    };

    // Words picked with a skewed distribution, in lines of various lengths, so the data compresses like a text file
    private static byte[] CreateText()
    {
        var random = new Random(42);
        var sb = new StringBuilder(Size + 100);
        var wordsOnLine = 0;
        while (sb.Length < Size)
        {
            var index = (int)(Words.Length * Math.Pow(random.NextDouble(), 2.5));
            sb.Append(Words[index]);
            if (++wordsOnLine > 8 + random.Next(10))
            {
                sb.Append(".\n");
                wordsOnLine = 0;
            }
            else
            {
                sb.Append(' ');
            }
        }

        return Encoding.ASCII.GetBytes(sb.ToString(0, Size));
    }

    // Fixed-size records with increasing counters, small integers, and some random noise, like a table of an executable
    private static byte[] CreateBinary()
    {
        var random = new Random(42);
        var data = new byte[Size];
        var counter = 0x1000;
        for (var i = 0; i + 16 <= data.Length; i += 16)
        {
            BitConverter.TryWriteBytes(data.AsSpan(i), counter);
            counter += random.Next(1, 64);
            BitConverter.TryWriteBytes(data.AsSpan(i + 4), (short)random.Next(0, 100));
            data[i + 6] = (byte)random.Next(0, 4);
            BitConverter.TryWriteBytes(data.AsSpan(i + 8), random.Next(0, 1 << 12));
            if (random.Next(4) == 0)
            {
                random.NextBytes(data.AsSpan(i + 12, 4));
            }
        }

        return data;
    }
}
