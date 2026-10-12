namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The PackBits run-length scheme of TIFF strips and tiles (TIFF 6.0, section 9), written from the specification.
/// </summary>
/// <remarks>
/// A header byte <c>n</c> read as a signed number is followed by <c>n + 1</c> literal bytes when it is between 0 and 127, or
/// by one byte to repeat <c>1 - n</c> times when it is between -127 and -1; -128 does nothing.
/// </remarks>
internal static class TiffPackBits
{
    /// <summary>The largest number of bytes of one literal or repeated run.</summary>
    private const int MaxRunLength = 128;

    /// <summary>Decodes one block into a buffer of exactly its decompressed size.</summary>
    /// <param name="source">The compressed block.</param>
    /// <param name="destination">The buffer to fill.</param>
    /// <returns>
    /// The number of bytes produced: the length of <paramref name="destination"/>, or fewer when the block ends first. The
    /// bytes a block produces beyond the buffer are not written.
    /// </returns>
    public static int Decode(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var written = 0;
        var position = 0;
        while (written < destination.Length && position < source.Length)
        {
            var header = (sbyte)source[position++];
            if (header == sbyte.MinValue)
                continue;

            if (header >= 0)
            {
                var count = Math.Min(header + 1, source.Length - position);
                var copied = Math.Min(count, destination.Length - written);
                source.Slice(position, copied).CopyTo(destination[written..]);
                written += copied;
                position += count;
                if (count != header + 1)
                    break;
            }
            else
            {
                if (position == source.Length)
                    break;

                var count = Math.Min(1 - header, destination.Length - written);
                destination.Slice(written, count).Fill(source[position++]);
                written += count;
            }
        }

        return written;
    }

    /// <summary>Compresses one row. The specification packs each row separately: a run never crosses a row.</summary>
    /// <param name="row">The bytes of the row.</param>
    /// <param name="output">The output buffer the compressed row is appended to.</param>
    public static void EncodeRow(ReadOnlySpan<byte> row, ImageOutputBuffer output)
    {
        ArgumentNullException.ThrowIfNull(output);

        // The worst case is a row of literal runs: one header byte for every 128 bytes (a repeated run is never longer
        // than the bytes it replaces)
        var destination = output.GetSpan(row.Length + (row.Length / MaxRunLength) + 2);
        var written = 0;
        var literalStart = 0;
        var position = 0;
        while (position < row.Length)
        {
            var value = row[position];
            var run = 1;
            while (run < MaxRunLength && position + run < row.Length && row[position + run] == value)
            {
                run++;
            }

            // A repeated run of two bytes costs as much as the two bytes inside a literal run, and splits that run
            if (run < 3)
            {
                position += run;
                continue;
            }

            written += WriteLiterals(row[literalStart..position], destination[written..]);
            destination[written++] = (byte)(1 - run);
            destination[written++] = value;
            position += run;
            literalStart = position;
        }

        written += WriteLiterals(row[literalStart..], destination[written..]);
        output.Advance(written);
    }

    private static int WriteLiterals(ReadOnlySpan<byte> literals, Span<byte> destination)
    {
        var written = 0;
        while (!literals.IsEmpty)
        {
            var count = Math.Min(literals.Length, MaxRunLength);
            destination[written++] = (byte)(count - 1);
            literals[..count].CopyTo(destination[written..]);
            written += count;
            literals = literals[count..];
        }

        return written;
    }
}
