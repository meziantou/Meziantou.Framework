using Meziantou.Framework.Pdf.Parsing;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>
/// The <c>ASCII85Decode</c> filter (ISO 32000-2, 7.4.3): groups of five characters from <c>!</c> to <c>u</c> for four
/// bytes, <c>z</c> for four zeros, <c>~&gt;</c> at the end.
/// </summary>
internal static class Ascii85Filter
{
    public static byte[] Decode(ReadOnlySpan<byte> input, long limit)
    {
        var output = new BoundedOutput(limit, (input.Length * 4 / 5) + 4);
        Span<uint> group = stackalloc uint[5];
        var count = 0;

        // Some producers keep the "<~" prefix of PostScript
        if (input.StartsWith("<~"u8))
        {
            input = input[2..];
        }

        foreach (var c in input)
        {
            if (c == '~')
                break;

            if (PdfLexer.IsWhitespace(c))
                continue;

            if (c == 'z' && count == 0)
            {
                output.Write([0, 0, 0, 0]);
                continue;
            }

            if (c is < (byte)'!' or > (byte)'u')
                throw new InvalidPdfContentException($"The ASCII85Decode data has an invalid character (0x{c:X2})");

            group[count++] = (uint)(c - '!');
            if (count == 5)
            {
                WriteGroup(output, group, 4);
                count = 0;
            }
        }

        // A final group of n characters gives n - 1 bytes: the missing characters are 'u'
        if (count == 1)
            throw new InvalidPdfContentException("The ASCII85Decode data ends with a group of one character");

        if (count > 1)
        {
            for (var i = count; i < 5; i++)
            {
                group[i] = 84;
            }

            WriteGroup(output, group, count - 1);
        }

        return output.ToArray();
    }

    public static byte[] Encode(ReadOnlySpan<byte> input)
    {
        var output = new List<byte>((input.Length * 5 / 4) + 8);
        Span<byte> characters = stackalloc byte[5];
        for (var i = 0; i < input.Length; i += 4)
        {
            var length = Math.Min(4, input.Length - i);
            uint value = 0;
            for (var j = 0; j < 4; j++)
            {
                value = (value << 8) | (j < length ? input[i + j] : 0u);
            }

            if (value == 0 && length == 4)
            {
                output.Add((byte)'z');
                continue;
            }

            for (var j = 4; j >= 0; j--)
            {
                characters[j] = (byte)((value % 85) + '!');
                value /= 85;
            }

            for (var j = 0; j <= length; j++)
            {
                output.Add(characters[j]);
            }
        }

        output.Add((byte)'~');
        output.Add((byte)'>');
        return [.. output];
    }

    private static void WriteGroup(BoundedOutput output, ReadOnlySpan<uint> group, int byteCount)
    {
        ulong value = 0;
        foreach (var digit in group)
        {
            value = (value * 85) + digit;
        }

        if (value > uint.MaxValue)
            throw new InvalidPdfContentException("The ASCII85Decode data has a group whose value exceeds 2^32 - 1");

        for (var i = 0; i < byteCount; i++)
        {
            output.Write((byte)(value >> (24 - (i * 8))));
        }
    }
}
