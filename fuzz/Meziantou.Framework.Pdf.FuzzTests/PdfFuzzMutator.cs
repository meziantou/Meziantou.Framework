using System.Text;

namespace Meziantou.Framework.Pdf.FuzzTests;

/// <summary>
/// Mutates PDF files: byte-level mutations (flips, insertions, deletions, truncations, duplicated chunks) and mutations
/// that know the syntax (numbers, delimiters, keywords, filter names), which reach the parser behind the lexer.
/// </summary>
internal static class PdfFuzzMutator
{
    private static readonly string[] Tokens =
    [
        "<<", ">>", "[", "]", "(", ")", "<", ">", "/", "%", " R", " obj", "endobj", "stream\n", "\nendstream", "xref", "trailer", "startxref", "null", "true",
        "/Length", "/Filter", "/FlateDecode", "/LZWDecode", "/ASCII85Decode", "/ASCIIHexDecode", "/RunLengthDecode", "/DCTDecode", "/DecodeParms", "/Predictor 12", "/Columns 4",
        "/Type", "/Pages", "/Kids", "/Count", "/Parent", "/Contents", "/Resources", "/Font", "/XObject", "/Prev", "/XRefStm", "/ObjStm", "/W [1 2 1]", "/Index",
        "BT", "ET", "Tf", "Tj", "TJ", "Td", "Tm", "Do", "q", "Q", "cm", "BI", "ID", "EI", "-1", "0", "4294967296", "99999999999", "1e9", "-.5", "#", "\\", "\0",
    ];

    public static byte[] Mutate(byte[] input, PdfFuzzRandom random)
    {
        var data = new List<byte>(input);
        var count = 1 + random.Next(4);
        for (var i = 0; i < count; i++)
        {
            MutateOnce(data, random);
        }

        return [.. data];
    }

    private static void MutateOnce(List<byte> data, PdfFuzzRandom random)
    {
        if (data.Count == 0)
        {
            data.AddRange(Encoding.Latin1.GetBytes(Tokens[random.Next(Tokens.Length)]));
            return;
        }

        var position = random.Next(data.Count);
        switch (random.Next(10))
        {
            case 0:
                data[position] ^= (byte)(1 << random.Next(8));
                break;
            case 1:
                data[position] = (byte)random.Next(256);
                break;
            case 2:
                data.RemoveRange(position, Math.Min(data.Count - position, 1 + random.Next(16)));
                break;
            case 3:
                data.InsertRange(position, Encoding.Latin1.GetBytes(Tokens[random.Next(Tokens.Length)]));
                break;
            case 4:
                data.RemoveRange(position, data.Count - position);
                break;
            case 5:
                var length = Math.Min(data.Count - position, 1 + random.Next(256));
                data.InsertRange(random.Next(data.Count), data.GetRange(position, length));
                break;
            case 6:
                ReplaceNumber(data, position, random);
                break;
            case 7:
                // Swap two chunks: offsets and lengths stop matching
                var size = Math.Min(64, data.Count / 4);
                if (size > 0)
                {
                    var other = random.Next(data.Count - size);
                    var start = Math.Min(position, data.Count - size);
                    for (var i = 0; i < size; i++)
                    {
                        (data[start + i], data[other + i]) = (data[other + i], data[start + i]);
                    }
                }

                break;
            case 8:
                data.Insert(position, (byte)"()<>[]{}/%"[random.Next(10)]);
                break;
            default:
                data.InsertRange(position, Enumerable.Repeat((byte)random.Next(256), random.Next(1, 64)));
                break;
        }
    }

    /// <summary>Replaces the number at or after a position with another one: an offset, a length, a count or an object number.</summary>
    private static void ReplaceNumber(List<byte> data, int position, PdfFuzzRandom random)
    {
        var start = position;
        while (start < data.Count && data[start] is not (>= (byte)'0' and <= (byte)'9'))
        {
            start++;
        }

        if (start >= data.Count)
            return;

        var end = start;
        while (end < data.Count && data[end] is >= (byte)'0' and <= (byte)'9')
        {
            end++;
        }

        string[] values = ["0", "1", "-1", "2", "255", "65535", "2147483647", "2147483648", "9999999999", (random.Next(1000)).ToString(System.Globalization.CultureInfo.InvariantCulture)];
        data.RemoveRange(start, end - start);
        data.InsertRange(start, Encoding.ASCII.GetBytes(values[random.Next(values.Length)]));
    }
}
