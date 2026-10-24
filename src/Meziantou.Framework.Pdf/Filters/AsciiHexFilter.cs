using Meziantou.Framework.Pdf.Parsing;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>The <c>ASCIIHexDecode</c> filter (ISO 32000-2, 7.4.2): pairs of hexadecimal digits, whitespace ignored, <c>&gt;</c> at the end.</summary>
internal static class AsciiHexFilter
{
    public static byte[] Decode(ReadOnlySpan<byte> input, long limit)
    {
        var output = new BoundedOutput(limit, (input.Length / 2) + 1);
        var high = -1;
        foreach (var c in input)
        {
            if (c == '>')
                break;

            if (PdfLexer.IsWhitespace(c))
                continue;

            if (!PdfLexer.TryHexValue(c, out var digit))
                throw new InvalidPdfContentException($"The ASCIIHexDecode data has a character that is not a hexadecimal digit (0x{c:X2})");

            if (high < 0)
            {
                high = digit;
            }
            else
            {
                output.Write((byte)((high << 4) | digit));
                high = -1;
            }
        }

        // An odd number of digits: the last digit is followed by 0
        if (high >= 0)
        {
            output.Write((byte)(high << 4));
        }

        return output.ToArray();
    }

    public static byte[] Encode(ReadOnlySpan<byte> input)
    {
        var output = new byte[(input.Length * 2) + 1];
        for (var i = 0; i < input.Length; i++)
        {
            output[i * 2] = HexDigit(input[i] >> 4);
            output[(i * 2) + 1] = HexDigit(input[i] & 0xF);
        }

        output[^1] = (byte)'>';
        return output;
    }

    private static byte HexDigit(int value) => (byte)(value < 10 ? '0' + value : 'A' + value - 10);
}
