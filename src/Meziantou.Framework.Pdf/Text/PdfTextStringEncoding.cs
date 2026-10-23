using System.Text;

namespace Meziantou.Framework.Pdf.Text;

/// <summary>The encodings of text strings (ISO 32000-2, 7.9.2.2): PDFDocEncoding (Annex D, table D.2), UTF-16BE and UTF-8 with a byte order mark.</summary>
internal static class PdfTextStringEncoding
{
    // The characters of the codes 0x18 to 0x1F and 0x80 to 0xA0 of PDFDocEncoding; the other codes are ISO-8859-1,
    // except 0x7F, 0x9F and 0xAD, which have no character
    private static readonly char[] HighCodes =
    [
        '\u2022', '\u2020', '\u2021', '\u2026', '\u2014', '\u2013', '\u0192', '\u2044', '\u2039', '\u203A', '\u2212', '\u2030', '\u201E', '\u201C', '\u201D', '\u2018',
        '\u2019', '\u201A', '\u2122', '\uFB01', '\uFB02', '\u0141', '\u0152', '\u0160', '\u0178', '\u017D', '\u0131', '\u0142', '\u0153', '\u0161', '\u017E', '\uFFFD',
        '\u20AC',
    ];

    private static readonly char[] LowCodes = ['\u02D8', '\u02C7', '\u02C6', '\u02D9', '\u02DD', '\u02DB', '\u02DA', '\u02DC'];

    private static readonly Dictionary<char, byte> ReverseTable = CreateReverseTable();

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes[2..(2 + ((bytes.Length - 2) & ~1))]);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes[3..]);

        // Not allowed, but written by some producers
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes[2..(2 + ((bytes.Length - 2) & ~1))]);

        return string.Create(bytes.Length, bytes.ToArray(), static (span, data) =>
        {
            for (var i = 0; i < data.Length; i++)
            {
                span[i] = DecodePdfDocEncoding(data[i]);
            }
        });
    }

    public static char DecodePdfDocEncoding(byte value)
    {
        return value switch
        {
            >= 0x18 and <= 0x1F => LowCodes[value - 0x18],
            0x7F or 0xAD => '\uFFFD',
            >= 0x80 and <= 0xA0 => HighCodes[value - 0x80],
            _ => (char)value,
        };
    }

    public static bool TryEncodePdfDocEncoding(char value, out byte code)
    {
        if (value is '\t' or '\n' or '\r' || (value is >= ' ' and <= '~') || (value is >= '\u00A1' and <= '\u00FF' && value != '\u00AD'))
        {
            code = (byte)value;
            return true;
        }

        return ReverseTable.TryGetValue(value, out code);
    }

    /// <summary>Encodes a text: PDFDocEncoding when it can, otherwise UTF-16BE with a byte order mark.</summary>
    public static byte[] Encode(string text)
    {
        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            if (!TryEncodePdfDocEncoding(text[i], out bytes[i]))
                return [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(text)];
        }

        return bytes;
    }

    private static Dictionary<char, byte> CreateReverseTable()
    {
        var table = new Dictionary<char, byte>();
        for (var i = 0; i < LowCodes.Length; i++)
        {
            table[LowCodes[i]] = (byte)(0x18 + i);
        }

        for (var i = 0; i < HighCodes.Length; i++)
        {
            if (HighCodes[i] != '\uFFFD')
            {
                table[HighCodes[i]] = (byte)(0x80 + i);
            }
        }

        return table;
    }
}
