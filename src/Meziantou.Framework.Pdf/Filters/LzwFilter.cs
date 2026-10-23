namespace Meziantou.Framework.Pdf.Filters;

/// <summary>The <c>LZWDecode</c> filter (ISO 32000-2, 7.4.4): codes of 9 to 12 bits, most significant bit first, a clear-table code (256) and an end-of-data code (257).</summary>
internal static class LzwFilter
{
    private const int ClearTable = 256;
    private const int EndOfData = 257;
    private const int MaxCodes = 4096;

    /// <param name="input">The encoded data.</param>
    /// <param name="earlyChange">1 (the default) when the code width grows one code early, 0 otherwise.</param>
    /// <param name="limit">The maximum length of the decoded data.</param>
    public static byte[] Decode(ReadOnlySpan<byte> input, int earlyChange, long limit)
    {
        var output = new BoundedOutput(limit, input.Length * 3);

        // The table of entries: the prefix code and the last byte of each entry, and its length and first byte
        var prefix = new int[MaxCodes];
        var suffix = new byte[MaxCodes];
        var lengths = new int[MaxCodes];
        var firstBytes = new byte[MaxCodes];
        for (var i = 0; i < 256; i++)
        {
            prefix[i] = -1;
            suffix[i] = (byte)i;
            lengths[i] = 1;
            firstBytes[i] = (byte)i;
        }

        var next = 258;
        var width = 9;
        var previous = -1;
        var bitBuffer = 0;
        var bitCount = 0;
        var position = 0;
        var entry = new byte[MaxCodes];
        while (true)
        {
            while (bitCount < width)
            {
                if (position >= input.Length)
                    return output.ToArray();

                bitBuffer = ((bitBuffer << 8) | input[position++]) & 0xFFFFFF;
                bitCount += 8;
            }

            var code = (bitBuffer >> (bitCount - width)) & ((1 << width) - 1);
            bitCount -= width;
            if (code == ClearTable)
            {
                next = 258;
                width = 9;
                previous = -1;
                continue;
            }

            if (code == EndOfData)
                return output.ToArray();

            if (previous < 0)
            {
                if (code > 255)
                {
                    // A code that is not a byte right after a clear-table code: the data is damaged
                    return output.ToArray();
                }

                output.Write((byte)code);
                previous = code;
                continue;
            }

            byte first;
            if (code < next)
            {
                first = WriteEntry(output, code, prefix, suffix, lengths, entry);
            }
            else if (code == next)
            {
                // The entry that is being defined: the previous entry followed by its own first byte
                first = firstBytes[previous];
                WriteEntry(output, previous, prefix, suffix, lengths, entry);
                output.Write(first);
            }
            else
            {
                return output.ToArray();
            }

            if (next < MaxCodes)
            {
                prefix[next] = previous;
                suffix[next] = code == next ? firstBytes[previous] : first;
                lengths[next] = lengths[previous] + 1;
                firstBytes[next] = firstBytes[previous];
                next++;
            }

            previous = code;
            if (next + earlyChange >= (1 << width) && width < 12)
            {
                width++;
            }
        }
    }

    public static byte[] Encode(ReadOnlySpan<byte> input)
    {
        var output = new List<byte>(input.Length);
        var bitBuffer = 0L;
        var bitCount = 0;
        var width = 9;
        var table = new Dictionary<(int Prefix, byte Value), int>();
        var next = 258;

        void WriteCode(int code)
        {
            bitBuffer = ((bitBuffer << width) | (uint)code) & 0xFFFFFF;
            bitCount += width;
            while (bitCount >= 8)
            {
                output.Add((byte)(bitBuffer >> (bitCount - 8)));
                bitCount -= 8;
            }
        }

        WriteCode(ClearTable);
        var current = -1;
        foreach (var value in input)
        {
            if (current < 0)
            {
                current = value;
                continue;
            }

            if (table.TryGetValue((current, value), out var code))
            {
                current = code;
                continue;
            }

            WriteCode(current);
            table[(current, value)] = next++;
            current = value;

            // The decoder grows the width when the next code reaches the limit, one code early (EarlyChange 1)
            if (next + 1 > (1 << width))
            {
                if (width == 12)
                {
                    WriteCode(ClearTable);
                    table.Clear();
                    next = 258;
                    width = 9;
                }
                else
                {
                    width++;
                }
            }
        }

        if (current >= 0)
        {
            WriteCode(current);
            next++;
            if (next + 1 > (1 << width) && width < 12)
            {
                width++;
            }
        }

        WriteCode(EndOfData);
        if (bitCount > 0)
        {
            output.Add((byte)(bitBuffer << (8 - bitCount)));
        }

        return [.. output];
    }

    private static byte WriteEntry(BoundedOutput output, int code, int[] prefix, byte[] suffix, int[] lengths, byte[] entry)
    {
        var length = lengths[code];
        var index = length;
        for (var c = code; c >= 0 && index > 0; c = prefix[c])
        {
            entry[--index] = suffix[c];
        }

        output.Write(entry.AsSpan(0, length));
        return entry[0];
    }
}
