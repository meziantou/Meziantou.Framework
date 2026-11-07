namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The run-length byte coder of the RLE compression (compression notes, section 3): packets that start with a signed
/// count byte, a repeat run (0 to 127: the next byte, count + 1 times) or a literal (-128 to -1: the next -count bytes).
/// </summary>
internal static class ExrRle
{
    /// <summary>Decodes the packets of a chunk.</summary>
    /// <param name="source">The packets.</param>
    /// <param name="destination">Receives exactly its length of bytes.</param>
    /// <exception cref="InvalidImageContentException">The packets do not produce exactly the bytes of <paramref name="destination"/>.</exception>
    public static void Decode(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var input = 0;
        var output = 0;
        while (input < source.Length)
        {
            var count = (sbyte)source[input++];
            if (count >= 0)
            {
                var run = count + 1;
                if (input >= source.Length)
                    throw ExrFormat.Invalid("an RLE repeat packet has no value byte.");

                if (run > destination.Length - output)
                    throw TooLong(destination.Length);

                destination.Slice(output, run).Fill(source[input++]);
                output += run;
            }
            else
            {
                var literal = -count;
                if (literal > source.Length - input)
                    throw ExrFormat.Invalid("an RLE literal packet extends past the end of the chunk.");

                if (literal > destination.Length - output)
                    throw TooLong(destination.Length);

                source.Slice(input, literal).CopyTo(destination[output..]);
                input += literal;
                output += literal;
            }
        }

        if (output != destination.Length)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the RLE data of a chunk produce {output} bytes; {destination.Length} are expected."));
    }

    /// <summary>Encodes bytes as packets: runs of 3 to 128 equal bytes, literals of 1 to 127 bytes.</summary>
    /// <param name="source">The bytes.</param>
    /// <param name="destination">Receives the packets.</param>
    /// <returns>The number of bytes written, or -1 when the packets do not fit <paramref name="destination"/>.</returns>
    public static int Encode(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var input = 0;
        var output = 0;
        while (input < source.Length)
        {
            var run = CountRun(source, input);
            if (run >= 3)
            {
                if (destination.Length - output < 2)
                    return -1;

                destination[output++] = (byte)(run - 1);
                destination[output++] = source[input];
                input += run;
                continue;
            }

            // A literal stops before a run of 3 equal bytes
            var start = input;
            while (input < source.Length && input - start < 127 && CountRun(source, input) < 3)
            {
                input++;
            }

            var length = input - start;
            if (destination.Length - output < length + 1)
                return -1;

            destination[output++] = (byte)(sbyte)-length;
            source.Slice(start, length).CopyTo(destination[output..]);
            output += length;
        }

        return output;
    }

    private static int CountRun(ReadOnlySpan<byte> source, int start)
    {
        var value = source[start];
        var end = Math.Min(source.Length, start + 128);
        var i = start + 1;
        while (i < end && source[i] == value)
        {
            i++;
        }

        return i - start;
    }

    private static InvalidImageContentException TooLong(int expected)
        => ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the RLE data of a chunk produce more than {expected} bytes."));
}
