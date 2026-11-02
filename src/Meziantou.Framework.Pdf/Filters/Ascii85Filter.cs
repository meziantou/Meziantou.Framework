using Meziantou.Framework.Pdf.Parsing;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>
/// The <c>ASCII85Decode</c> filter (ISO 32000-2, 7.4.3): groups of five characters from <c>!</c> to <c>u</c> for four
/// bytes, <c>z</c> for four zeros, <c>~&gt;</c> at the end.
/// </summary>
/// <remarks>
/// Damaged data (a character that is not a digit, <c>z</c> inside a group, a group whose value exceeds 2^32 - 1) ends
/// the data like <c>~&gt;</c>: the groups before it are kept and the group it is in is dropped. A final group of one
/// character, which cannot give a byte, is dropped too. The data is invalid only when no byte comes before the damage.
/// </remarks>
internal static class Ascii85Filter
{
    public static byte[] Decode(ReadOnlySpan<byte> input, long limit)
    {
        var output = new BoundedOutput(limit, (int)Math.Min(((long)input.Length * 4 / 5) + 4, int.MaxValue));
        Span<uint> group = stackalloc uint[5];
        var count = 0;

        // Some producers keep the "<~" prefix of PostScript
        if (input.StartsWith("<~"u8))
        {
            input = input[2..];
        }

        string? damage = null;
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
            {
                damage = $"an invalid character (0x{c:X2})";
                break;
            }

            group[count++] = (uint)(c - '!');
            if (count == 5)
            {
                count = 0;
                if (!TryWriteGroup(output, group, 4))
                {
                    damage = "a group whose value exceeds 2^32 - 1";
                    break;
                }
            }
        }

        if (damage is null && count > 1)
        {
            // A final group of n characters gives n - 1 bytes: the missing characters are 'u'
            group[count..].Fill(84);
            if (!TryWriteGroup(output, group, count - 1))
            {
                damage = "a group whose value exceeds 2^32 - 1";
            }
        }
        else if (damage is null && count == 1)
        {
            damage = "a final group of one character";
        }

        if (damage is not null && output.Length == 0)
            throw new InvalidPdfContentException($"The ASCII85Decode data has {damage}");

        return output.ToArray();
    }

    public static byte[] Encode(ReadOnlySpan<byte> input)
    {
        // Five characters per group of four bytes ('z' for four zeros), the characters of the last bytes plus one, "~>"
        var length = 2L;
        for (var i = 0; i < input.Length; i += 4)
        {
            var groupLength = Math.Min(4, input.Length - i);
            length += groupLength == 4 && input.Slice(i, 4).IndexOfAnyExcept((byte)0) < 0 ? 1 : groupLength + 1;
        }

        var output = new byte[length];
        var position = 0;
        Span<byte> characters = stackalloc byte[5];
        for (var i = 0; i < input.Length; i += 4)
        {
            var groupLength = Math.Min(4, input.Length - i);
            uint value = 0;
            for (var j = 0; j < 4; j++)
            {
                value = (value << 8) | (j < groupLength ? input[i + j] : 0u);
            }

            if (value == 0 && groupLength == 4)
            {
                output[position++] = (byte)'z';
                continue;
            }

            for (var j = 4; j >= 0; j--)
            {
                characters[j] = (byte)((value % 85) + '!');
                value /= 85;
            }

            characters[..(groupLength + 1)].CopyTo(output.AsSpan(position));
            position += groupLength + 1;
        }

        output[position++] = (byte)'~';
        output[position] = (byte)'>';
        return output;
    }

    private static bool TryWriteGroup(BoundedOutput output, ReadOnlySpan<uint> group, int byteCount)
    {
        ulong value = 0;
        foreach (var digit in group)
        {
            value = (value * 85) + digit;
        }

        if (value > uint.MaxValue)
            return false;

        for (var i = 0; i < byteCount; i++)
        {
            output.Write((byte)(value >> (24 - (i * 8))));
        }

        return true;
    }
}
