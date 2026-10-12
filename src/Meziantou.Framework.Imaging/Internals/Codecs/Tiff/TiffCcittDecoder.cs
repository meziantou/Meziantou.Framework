namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decoder of the CCITT facsimile compressions of TIFF, written from ITU-T Recommendations T.4 and T.6 and from
/// sections 10 and 11 of TIFF 6.0: modified Huffman run lengths (compression 2), Group 3 one- and two-dimensional coding
/// (compression 3) and Group 4 (compression 4).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// A decoded row is one bit per pixel, most significant bit first: 0 for the pixels of a white run and 1 for the pixels
/// of a black run. <c>PhotometricInterpretation</c> then says what 0 displays as, like for any bilevel data.
/// </description></item>
/// <item><description>
/// Compression 2 codes every row in one dimension and starts each row on a byte boundary. Compression 3 puts an
/// end-of-line code before each row, possibly preceded by fill bits; with two-dimensional coding (<c>T4Options</c> bit 0)
/// the bit that follows it says how the row is coded. Compression 4 codes every row in two dimensions, without
/// end-of-line codes, the row above the first one being white.
/// </description></item>
/// <item><description>
/// A row is kept as the positions where the color changes, which is what two-dimensional coding refers to in the row
/// above. The two lists (the row being decoded and the reference row) are the only working memory.
/// </description></item>
/// <item><description>
/// Nothing is repaired: a code that does not exist, a run that passes the end of the row, a change to the left of the
/// previous one, an end-of-line code inside a row, and data that ends before the last row are reported. The
/// uncompressed mode extension is recognized and reported as such by the caller (<see cref="UncompressedModeError"/>).
/// </description></item>
/// </list>
/// </remarks>
internal static class TiffCcittDecoder
{
    /// <summary>The error reported for the uncompressed mode extension, which is a valid but unsupported feature.</summary>
    public const string UncompressedModeError = "it uses the uncompressed mode extension";

    /// <summary>Gets the number of entries of each of the two change lists the decoder needs for rows of a given width.</summary>
    /// <param name="width">The number of pixels of a row.</param>
    /// <returns>The number of entries.</returns>
    public static long GetChangeListLength(int width) => (long)width + 8;

    /// <summary>Decodes the rows of one block.</summary>
    /// <param name="data">The compressed block.</param>
    /// <param name="destination">Exactly <paramref name="rowCount"/> rows of <c>(width + 7) / 8</c> bytes.</param>
    /// <param name="width">The number of pixels of a row.</param>
    /// <param name="rowCount">The number of rows.</param>
    /// <param name="compression">2, 3 or 4.</param>
    /// <param name="twoDimensional">Whether a Group 3 block mixes one- and two-dimensional rows (<c>T4Options</c> bit 0).</param>
    /// <param name="reference">A change list of <see cref="GetChangeListLength"/> entries.</param>
    /// <param name="current">Another change list of the same length.</param>
    /// <returns>
    /// <see langword="null"/> on success, otherwise what is wrong with the data (<see cref="UncompressedModeError"/> itself
    /// for the extension the decoder does not read).
    /// </returns>
    public static string? Decode(ReadOnlySpan<byte> data, Span<byte> destination, int width, int rowCount, int compression, bool twoDimensional, Span<int> reference, Span<int> current)
    {
        var rowLength = (width + 7) / 8;
        destination[..(rowLength * rowCount)].Clear();
        var reader = new TiffBitReader(data);

        // The row above the first one is white: it has no change
        var referenceCount = 0;
        Terminate(reference, referenceCount, width);
        for (var row = 0; row < rowCount; row++)
        {
            var isTwoDimensional = compression == TiffFormat.CompressionCcittFax4;
            if (compression == TiffFormat.CompressionCcittFax3)
            {
                var hasEndOfLine = TrySkipEndOfLine(ref reader);
                if (twoDimensional)
                {
                    if (!hasEndOfLine)
                        return string.Create(CultureInfo.InvariantCulture, $"the row {row} is not preceded by an end-of-line code");

                    // The tag bit: 1 when the row is coded in one dimension
                    isTwoDimensional = reader.Read(1) == 0;
                }
            }

            var error = isTwoDimensional
                ? DecodeTwoDimensionalRow(ref reader, reference, current, width, out var count)
                : DecodeOneDimensionalRow(ref reader, current, width, out count);
            if (error is not null)
                return error == UncompressedModeError ? error : string.Create(CultureInfo.InvariantCulture, $"the row {row} {error}");

            if (reader.IsPastEnd)
                return string.Create(CultureInfo.InvariantCulture, $"the data ends in the row {row}");

            Terminate(current, count, width);
            Paint(current, count, destination.Slice(row * rowLength, rowLength), width);
            if (compression == TiffFormat.CompressionCcittRle)
            {
                reader.AlignToByte();
            }

            var swap = reference;
            reference = current;
            current = swap;
        }

        return null;
    }

    /// <summary>Ends a change list with the entries that stand for "no further change": the end of the row.</summary>
    private static void Terminate(Span<int> changes, int count, int width) => changes.Slice(count, 4).Fill(width);

    /// <summary>Sets the pixels of the black runs: the runs that start at the changes of even index.</summary>
    private static void Paint(ReadOnlySpan<int> changes, int count, Span<byte> row, int width)
    {
        for (var i = 0; i < count; i += 2)
        {
            var start = changes[i];
            var end = Math.Min(changes[i + 1], width);
            if (start < end)
            {
                FillBits(row, start, end);
            }
        }
    }

    /// <summary>Sets the bits of the pixels <paramref name="start"/> to <paramref name="end"/> (exclusive).</summary>
    internal static void FillBits(Span<byte> row, int start, int end)
    {
        var firstByte = start >> 3;
        var lastByte = (end - 1) >> 3;
        var firstMask = (byte)(0xFF >> (start & 7));
        var lastMask = (byte)(0xFF << (7 - ((end - 1) & 7)));
        if (firstByte == lastByte)
        {
            row[firstByte] |= (byte)(firstMask & lastMask);
            return;
        }

        row[firstByte] |= firstMask;
        row[(firstByte + 1)..lastByte].Fill(0xFF);
        row[lastByte] |= lastMask;
    }

    /// <summary>
    /// Consumes an end-of-line code and the fill bits before it: at least eleven zeros and a one. The position is not
    /// changed when the next bits are something else.
    /// </summary>
    private static bool TrySkipEndOfLine(ref TiffBitReader reader)
    {
        var start = reader.Position;
        var zeros = 0;
        while (!reader.IsAtEnd)
        {
            if (reader.Read(1) != 0)
            {
                if (zeros >= TiffCcittCodes.EndOfLineLength - 1)
                    return true;

                break;
            }

            zeros++;
        }

        reader.Position = start;
        return false;
    }

    /// <summary>Reads one run: its make-up codes and its terminating code.</summary>
    /// <returns>The length of the run, or -1 when a code does not exist or the run is longer than <paramref name="limit"/>.</returns>
    private static int ReadRun(ref TiffBitReader reader, bool isBlack, int limit)
    {
        var total = 0;
        while (true)
        {
            var length = TiffCcittCodes.Lookup(isBlack, reader.Peek(TiffCcittCodes.MaxRunCodeLength), out var run);
            if (length == 0)
                return -1;

            reader.Skip(length);
            total += run;
            if (total > limit)
                return -1;

            if (run < 64)
                return total;
        }
    }

    /// <summary>Decodes a row coded in one dimension: alternating white and black runs, the first one white (T.4, 4.1).</summary>
    private static string? DecodeOneDimensionalRow(ref TiffBitReader reader, Span<int> changes, int width, out int count)
    {
        count = 0;
        var position = 0;
        var isBlack = false;
        while (position < width)
        {
            var run = ReadRun(ref reader, isBlack, width - position);
            if (run < 0)
                return "has an invalid run code or a run that passes the end of the row";

            position += run;
            if (count >= changes.Length - 4)
                return "has more runs than pixels";

            changes[count++] = position;
            isBlack = !isBlack;
        }

        // The end of the row is not a change
        count--;
        return null;
    }

    /// <summary>
    /// Decodes a row coded in two dimensions (T.4, 4.2 and T.6, 2.2): each code places the next changes of the row
    /// relative to <c>a0</c>, the last position coded, and to <c>b1</c> and <c>b2</c>, the first changes of the reference
    /// row to the right of <c>a0</c> (<c>b1</c> being a change to the color opposite to the one at <c>a0</c>).
    /// </summary>
    private static string? DecodeTwoDimensionalRow(ref TiffBitReader reader, ReadOnlySpan<int> reference, Span<int> changes, int width, out int count)
    {
        count = 0;

        // Before the first pixel there is an imaginary white pixel. The changes of even index of a row are changes to
        // black, so the index of b1 has the parity of the color at a0
        var a0 = -1;
        var isBlack = false;
        var b = 0;
        while (a0 < width)
        {
            while (reference[b] <= a0 && reference[b] < width)
            {
                b += 2;
            }

            var b1 = reference[b];
            var b2 = reference[b + 1];
            if (count >= changes.Length - 5)
                return "has more changes than pixels";

            var window = reader.Peek(7);
            int offset;
            if ((window & 0b1000000) != 0)
            {
                reader.Skip(1);
                offset = 0;
            }
            else if ((window >> 4) == 0b011)
            {
                reader.Skip(3);
                offset = 1;
            }
            else if ((window >> 4) == 0b010)
            {
                reader.Skip(3);
                offset = -1;
            }
            else if ((window >> 4) == TiffCcittCodes.HorizontalCode)
            {
                reader.Skip(TiffCcittCodes.HorizontalLength);
                var start = Math.Max(a0, 0);
                var first = ReadRun(ref reader, isBlack, width - start);
                if (first < 0)
                    return "has an invalid run code or a run that passes the end of the row";

                var second = ReadRun(ref reader, !isBlack, width - start - first);
                if (second < 0)
                    return "has an invalid run code or a run that passes the end of the row";

                changes[count++] = start + first;
                changes[count++] = start + first + second;
                a0 = start + first + second;
                continue;
            }
            else if ((window >> 3) == TiffCcittCodes.PassCode)
            {
                // The run of the reference row between b1 and b2 has no counterpart in this row
                reader.Skip(TiffCcittCodes.PassLength);
                a0 = b2;
                continue;
            }
            else if ((window >> 1) == 0b000011)
            {
                reader.Skip(6);
                offset = 2;
            }
            else if ((window >> 1) == 0b000010)
            {
                reader.Skip(6);
                offset = -2;
            }
            else if (window == 0b0000011)
            {
                reader.Skip(7);
                offset = 3;
            }
            else if (window == 0b0000010)
            {
                reader.Skip(7);
                offset = -3;
            }
            else if (window == 0b0000001)
            {
                return UncompressedModeError;
            }
            else
            {
                return reader.Peek(TiffCcittCodes.EndOfLineLength) == TiffCcittCodes.EndOfLine
                    ? "ends with an end-of-line code before its last pixel"
                    : "has an invalid two-dimensional code";
            }

            // Vertical mode: the change is at most three pixels away from b1
            var a1 = b1 + offset;
            if (a1 < a0 || a1 < 0 || a1 > width)
                return "has a change to the left of the previous one or after the end of the row";

            changes[count++] = a1;
            a0 = a1;
            isBlack = !isBlack;
            b = b > 0 ? b - 1 : b + 1;
        }

        // The end of the row is not a change
        while (count > 0 && changes[count - 1] >= width)
        {
            count--;
        }

        return null;
    }
}
