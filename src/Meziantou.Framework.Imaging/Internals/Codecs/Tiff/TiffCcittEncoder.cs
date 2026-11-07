namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The encoder of the CCITT facsimile compressions of TIFF, written from ITU-T Recommendations T.4 and T.6 and from
/// sections 10 and 11 of TIFF 6.0: the counterpart of <see cref="TiffCcittDecoder"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Compression 2 (modified Huffman): every row is coded in one dimension and padded to a byte boundary; there is no
/// end-of-line code.
/// </description></item>
/// <item><description>
/// Compression 3 (Group 3): every row is coded in one dimension and preceded by an end-of-line code, without fill bits
/// (<c>T4Options</c> is 0). No return-to-control sequence is written: TIFF does not use it.
/// </description></item>
/// <item><description>
/// Compression 4 (Group 4): every row is coded in two dimensions against the row above (a white row for the first row
/// of a strip), and the strip ends with the end-of-facsimile-block sequence (two end-of-line codes).
/// </description></item>
/// </list>
/// A row is one bit per pixel, most significant bit first, where a set bit is a pixel of a black run.
/// </remarks>
internal static class TiffCcittEncoder
{
    /// <summary>Gets the number of entries of each of the two change lists the encoder needs for rows of a given width.</summary>
    /// <param name="width">The number of pixels of a row.</param>
    /// <returns>The number of entries.</returns>
    public static long GetChangeListLength(int width) => (long)width + 8;

    /// <summary>Compresses the rows of one strip.</summary>
    /// <param name="rows">The rows, each of <c>(width + 7) / 8</c> bytes.</param>
    /// <param name="width">The number of pixels of a row.</param>
    /// <param name="rowCount">The number of rows.</param>
    /// <param name="compression">2, 3 or 4.</param>
    /// <param name="reference">A change list of <see cref="GetChangeListLength"/> entries.</param>
    /// <param name="current">Another change list of the same length.</param>
    /// <param name="output">The output buffer the compressed strip is appended to.</param>
    public static void Encode(ReadOnlySpan<byte> rows, int width, int rowCount, int compression, Span<int> reference, Span<int> current, ImageOutputBuffer output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var rowLength = (width + 7) / 8;
        var writer = new TiffBitWriter(output);

        // The row above the first one is white: it has no change
        reference[..4].Fill(width);
        for (var row = 0; row < rowCount; row++)
        {
            FindChanges(rows.Slice(row * rowLength, rowLength), width, current);
            switch (compression)
            {
                case TiffFormat.CompressionCcittFax4:
                    WriteTwoDimensionalRow(ref writer, reference, current, width);
                    break;

                case TiffFormat.CompressionCcittFax3:
                    writer.Write(TiffCcittCodes.EndOfLine, TiffCcittCodes.EndOfLineLength);
                    WriteOneDimensionalRow(ref writer, current, width);
                    break;

                default:
                    WriteOneDimensionalRow(ref writer, current, width);
                    writer.AlignToByte();
                    break;
            }

            var swap = reference;
            reference = current;
            current = swap;
        }

        if (compression == TiffFormat.CompressionCcittFax4)
        {
            writer.Write(TiffCcittCodes.EndOfLine, TiffCcittCodes.EndOfLineLength);
            writer.Write(TiffCcittCodes.EndOfLine, TiffCcittCodes.EndOfLineLength);
        }

        writer.Flush();
    }

    /// <summary>
    /// Lists the positions where the color of a row changes, the first run being white (a row that starts with a black
    /// pixel has a change at 0), followed by entries that stand for "no further change": the end of the row.
    /// </summary>
    internal static int FindChanges(ReadOnlySpan<byte> row, int width, Span<int> changes)
    {
        var count = 0;
        var previous = 0;
        var position = 0;
        while (position < width)
        {
            // Whole bytes of the current color hold no change
            if ((position & 7) == 0 && position + 8 <= width && row[position >> 3] == (previous == 0 ? 0x00 : 0xFF))
            {
                position += 8;
                continue;
            }

            var bit = (row[position >> 3] >> (7 - (position & 7))) & 1;
            if (bit != previous)
            {
                changes[count++] = position;
                previous = bit;
            }

            position++;
        }

        changes.Slice(count, 4).Fill(width);
        return count;
    }

    /// <summary>Writes a run as make-up codes and a terminating code.</summary>
    private static void WriteRun(ref TiffBitWriter writer, int run, bool isBlack)
    {
        while (run > TiffCcittCodes.MaxMakeUpRun)
        {
            var largest = TiffCcittCodes.GetMakeUp(isBlack, TiffCcittCodes.MaxMakeUpRun);
            writer.Write(largest.Bits, largest.Length);
            run -= TiffCcittCodes.MaxMakeUpRun;
        }

        if (run >= 64)
        {
            var makeUp = TiffCcittCodes.GetMakeUp(isBlack, run & ~63);
            writer.Write(makeUp.Bits, makeUp.Length);
            run &= 63;
        }

        var terminating = TiffCcittCodes.GetTerminating(isBlack, run);
        writer.Write(terminating.Bits, terminating.Length);
    }

    /// <summary>Writes a row in one dimension: alternating white and black runs, the first one white (T.4, 4.1).</summary>
    private static void WriteOneDimensionalRow(ref TiffBitWriter writer, ReadOnlySpan<int> changes, int width)
    {
        var position = 0;
        var isBlack = false;
        for (var i = 0; position < width; i++)
        {
            // After the last change, the entries of the list are the end of the row
            var next = changes[i];
            WriteRun(ref writer, next - position, isBlack);
            position = next;
            isBlack = !isBlack;
        }
    }

    /// <summary>
    /// Writes a row in two dimensions (T.6, 2.2.3): with <c>a0</c> the last position coded, <c>a1</c> and <c>a2</c> the
    /// next changes of the row, and <c>b1</c> and <c>b2</c> the first changes of the reference row to the right of
    /// <c>a0</c> (<c>b1</c> being a change to the color opposite to the one at <c>a0</c>): pass mode when <c>b2</c> is to
    /// the left of <c>a1</c>, vertical mode when <c>a1</c> is at most three pixels away from <c>b1</c>, horizontal mode
    /// (two run lengths) otherwise.
    /// </summary>
    private static void WriteTwoDimensionalRow(ref TiffBitWriter writer, ReadOnlySpan<int> reference, ReadOnlySpan<int> changes, int width)
    {
        var a0 = -1;
        var isBlack = false;
        var a = 0;
        var b = 0;
        while (a0 < width)
        {
            while (reference[b] <= a0 && reference[b] < width)
            {
                b += 2;
            }

            var a1 = changes[a];
            var b1 = reference[b];
            var b2 = reference[b + 1];
            if (b2 < a1)
            {
                writer.Write(TiffCcittCodes.PassCode, TiffCcittCodes.PassLength);
                a0 = b2;
            }
            else if (Math.Abs(a1 - b1) <= 3)
            {
                var code = TiffCcittCodes.GetVertical(a1 - b1);
                writer.Write(code.Bits, code.Length);
                a0 = a1;
                isBlack = !isBlack;
                a++;
                b = b > 0 ? b - 1 : b + 1;
            }
            else
            {
                var a2 = changes[a + 1];
                writer.Write(TiffCcittCodes.HorizontalCode, TiffCcittCodes.HorizontalLength);
                WriteRun(ref writer, a1 - Math.Max(a0, 0), isBlack);
                WriteRun(ref writer, a2 - a1, !isBlack);
                a0 = a2;
                a += 2;
            }
        }
    }
}
