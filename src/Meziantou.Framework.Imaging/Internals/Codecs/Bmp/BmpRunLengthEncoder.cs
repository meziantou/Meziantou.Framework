namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the rows of the BMP run-length streams, <c>BI_RLE8</c> and <c>BI_RLE4</c>, from rows of palette indexes (one byte
/// per pixel for both).
/// </summary>
/// <remarks>
/// <para>
/// A row is a sequence of encoded runs (a pixel count and one index, or two alternating indexes for 4 bits) and of absolute
/// runs (the escape 0, a count of 3 to 255 and the literal indexes, padded to a 16-bit boundary), followed by the
/// end-of-line escape. Every pixel of the row is stored: the delta escape is never written, and a run never crosses a row.
/// </para>
/// <para>
/// Three or more pixels that repeat (with a period of one index for 8 bits, of two indexes for 4 bits) are an encoded run;
/// the pixels in between are an absolute run, or encoded runs when there are fewer than the three pixels absolute mode
/// requires. A row therefore never takes more than two bytes per pixel, plus its end-of-line escape
/// (<see cref="GetMaxEncodedRowLength"/>), and the output is a pure function of the row.
/// </para>
/// </remarks>
internal static class BmpRunLengthEncoder
{
    /// <summary>The length of the end-of-bitmap escape that follows the last row.</summary>
    public const int EndOfBitmapLength = 2;

    // The count of a run is one byte; absolute mode cannot store fewer than three pixels (0, 1 and 2 are escapes)
    private const int MaxCount = 255;
    private const int MinRepeat = 3;

    /// <summary>Gets the largest number of bytes <see cref="EncodeRow"/> writes for a row.</summary>
    /// <param name="width">The number of pixels of a row.</param>
    /// <returns>Two bytes per pixel and the end-of-line escape.</returns>
    public static long GetMaxEncodedRowLength(int width) => (2L * width) + 2;

    /// <summary>Encodes one row, including its end-of-line escape.</summary>
    /// <param name="indexes">The palette indexes of the row, one byte per pixel (below 16 for <paramref name="fourBit"/>).</param>
    /// <param name="fourBit"><see langword="true"/> for <c>BI_RLE4</c>, <see langword="false"/> for <c>BI_RLE8</c>.</param>
    /// <param name="destination">At least <see cref="GetMaxEncodedRowLength"/> bytes.</param>
    /// <returns>The number of bytes written.</returns>
    public static int EncodeRow(ReadOnlySpan<byte> indexes, bool fourBit, Span<byte> destination)
    {
        var written = fourBit ? EncodeRow4(indexes, destination) : EncodeRow8(indexes, destination);
        destination[written] = 0;
        destination[written + 1] = BmpFormat.RunLengthEndOfLine;
        return written + 2;
    }

    /// <summary>Writes the end-of-bitmap escape.</summary>
    /// <param name="destination">At least <see cref="EndOfBitmapLength"/> bytes.</param>
    public static void WriteEndOfBitmap(Span<byte> destination)
    {
        destination[0] = 0;
        destination[1] = BmpFormat.RunLengthEndOfBitmap;
    }

    private static int EncodeRow8(ReadOnlySpan<byte> row, Span<byte> destination)
    {
        var written = 0;
        var position = 0;
        while (position < row.Length)
        {
            var run = GetRun8(row, position);
            if (run >= MinRepeat)
            {
                destination[written++] = (byte)run;
                destination[written++] = row[position];
                position += run;
                continue;
            }

            // The literal pixels up to the next run worth encoding
            var start = position;
            while (position < row.Length && position - start < MaxCount)
            {
                run = GetRun8(row, position);
                if (run >= MinRepeat)
                    break;

                position += run;
            }

            position = Math.Min(position, start + MaxCount);
            var count = position - start;
            if (count >= MinRepeat)
            {
                destination[written++] = 0;
                destination[written++] = (byte)count;
                row.Slice(start, count).CopyTo(destination[written..]);
                written += count;
                if ((count & 1) != 0)
                {
                    destination[written++] = 0;
                }

                continue;
            }

            // One or two pixels: too few for absolute mode
            var literals = row[..position];
            for (var i = start; i < position; i += run)
            {
                run = GetRun8(literals, i);
                destination[written++] = (byte)run;
                destination[written++] = row[i];
            }
        }

        return written;
    }

    private static int EncodeRow4(ReadOnlySpan<byte> row, Span<byte> destination)
    {
        var written = 0;
        var position = 0;
        while (position < row.Length)
        {
            var run = GetRun4(row, position);
            if (run >= MinRepeat)
            {
                destination[written++] = (byte)run;
                destination[written++] = (byte)((row[position] << 4) | row[position + 1]);
                position += run;
                continue;
            }

            // The literal pixels up to the next run worth encoding
            var start = position;
            while (position < row.Length && position - start < MaxCount && GetRun4(row, position) < MinRepeat)
            {
                position++;
            }

            var count = position - start;
            if (count >= MinRepeat)
            {
                // Two indexes per byte, the first in the high-order half; an odd count leaves the last half at zero
                destination[written++] = 0;
                destination[written++] = (byte)count;
                var length = (count + 1) / 2;
                for (var i = 0; i < length; i++)
                {
                    var second = (2 * i) + 1 < count ? row[start + (2 * i) + 1] : 0;
                    destination[written++] = (byte)((row[start + (2 * i)] << 4) | second);
                }

                if ((length & 1) != 0)
                {
                    destination[written++] = 0;
                }

                continue;
            }

            // One or two pixels: too few for absolute mode, and exactly what one encoded run of two indexes can store
            destination[written++] = (byte)count;
            destination[written++] = (byte)((row[start] << 4) | (count == 2 ? row[start + 1] : 0));
        }

        return written;
    }

    /// <summary>Gets the number of consecutive pixels equal to the one at <paramref name="position"/>, at most <see cref="MaxCount"/>.</summary>
    private static int GetRun8(ReadOnlySpan<byte> row, int position)
    {
        var limit = Math.Min(row.Length, position + MaxCount);
        var value = row[position];
        var end = position + 1;
        while (end < limit && row[end] == value)
        {
            end++;
        }

        return end - position;
    }

    /// <summary>
    /// Gets the number of consecutive pixels that alternate the two indexes at <paramref name="position"/> (each pixel equals
    /// the one two positions before it), at most <see cref="MaxCount"/>.
    /// </summary>
    private static int GetRun4(ReadOnlySpan<byte> row, int position)
    {
        var limit = Math.Min(row.Length, position + MaxCount);
        var end = Math.Min(position + 2, limit);
        while (end < limit && row[end] == row[end - 2])
        {
            end++;
        }

        return end - position;
    }
}
