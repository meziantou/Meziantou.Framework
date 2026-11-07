using System.Numerics;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>
/// The <c>CCITTFaxDecode</c> filter (ISO 32000-2, 7.4.6): Group 3 one- and two-dimensional and Group 4 facsimile coding
/// (ITU-T T.4 and T.6). The rows are decoded by the CCITT decoder of the TIFF codec of Meziantou.Framework.Imaging; this
/// class reads them as the parameters of the filter say.
/// </summary>
/// <remarks>
/// <para>
/// The output is one bit per pixel, most significant bit first, each row starting on a byte: 1 for white and 0 for black,
/// or the opposite with <c>/BlackIs1 true</c>. Damaged data is not an error: the rows decoded before it are kept, and the
/// missing rows are white, as viewers show a damaged fax.
/// </para>
/// <para>
/// With <c>/EndOfLine true</c> and a non-negative <c>/K</c>, a row that is not preceded by an end-of-line code is damaged,
/// and up to <c>/DamagedRowsBeforeError</c> damaged rows are repaired (Table 11): the decoding resumes at the next
/// end-of-line code, and the damaged row is the previous row, or a white row when the previous row was damaged too.
/// </para>
/// </remarks>
internal static class CcittFaxFilter
{
    private const int MaxColumns = 1 << 20;

    public static byte[] Decode(ReadOnlySpan<byte> data, PdfDictionary? parameters, long limit)
    {
        var k = parameters?.GetInteger(PdfNames.K, 0) ?? 0;
        var encodedByteAlign = parameters?.GetBoolean(PdfName.Create("EncodedByteAlign")) ?? false;
        var columns = parameters?.GetInteger(PdfNames.Columns, 1728) ?? 1728;
        var declaredRows = parameters?.GetInteger(PdfName.Create("Rows"), 0) ?? 0;
        var endOfBlock = parameters?.GetBoolean(PdfName.Create("EndOfBlock"), defaultValue: true) ?? true;
        var blackIs1 = parameters?.GetBoolean(PdfNames.BlackIs1) ?? false;

        // The end-of-line codes are required with /EndOfLine true; the damaged rows are repaired only then, in Group 3 (Table 11)
        var endOfLine = parameters?.GetBoolean(PdfName.Create("EndOfLine")) ?? false;
        var damagedRowsBeforeError = endOfLine && k >= 0 ? Math.Max(0, parameters?.GetInteger(PdfName.Create("DamagedRowsBeforeError"), 0) ?? 0) : 0;
        if (columns is < 1 or > MaxColumns)
            throw new InvalidPdfContentException($"The /Columns of the CCITTFaxDecode filter must be from 1 to {MaxColumns}");

        if (declaredRows < 0)
            throw new InvalidPdfContentException("The /Rows of the CCITTFaxDecode filter must not be negative");

        var width = (int)columns;
        var rowLength = (width + 7) / 8;

        // Divided, not multiplied: the product of a huge row count and the row length overflows
        if (declaredRows > limit / rowLength || declaredRows * rowLength > Array.MaxLength)
            throw new PdfResourceLimitException($"The CCITTFaxDecode data is larger than the limit of {limit} bytes");

        // With /Rows, the output has its final size from the start: the rows are decoded into it, and the rows the data
        // does not have are white without growing a buffer. Without, the rows go to an output that grows to the limit
        var white = blackIs1 ? (byte)0x00 : (byte)0xFF;
        var fixedOutput = declaredRows > 0 ? new byte[declaredRows * rowLength] : null;
        var output = fixedOutput is null ? new BoundedOutput(limit, 1 << 16) : null;
        var changeListLength = (int)TiffCcittDecoder.GetChangeListLength(width);
        var reference = new int[changeListLength];
        var current = new int[changeListLength];
        var buffer = fixedOutput is null ? new byte[rowLength] : null;
        var reader = new TiffBitReader(data);

        // The row above the first one is white: it has no change
        TiffCcittDecoder.Terminate(reference, 0, width);
        var rows = 0L;
        var damagedRows = 0L;
        var previousIsDamaged = false;
        while (declaredRows == 0 || rows < declaredRows)
        {
            if (encodedByteAlign && k < 0)
            {
                reader.AlignToByte();
            }

            // Group 3: an end-of-line code may precede each row. Two in a row end the block (6 for Group 3, 2 for
            // Group 4, 7.4.6); so does the end of the data
            var endOfLines = 0;
            while (TiffCcittDecoder.TrySkipEndOfLine(ref reader))
            {
                endOfLines++;
            }

            if (reader.IsAtEnd || (endOfBlock && endOfLines >= 2))
                break;

            if (encodedByteAlign && k >= 0 && endOfLines == 0)
            {
                reader.AlignToByte();
            }

            var row = fixedOutput is null ? buffer.AsSpan() : fixedOutput.AsSpan((int)(rows * rowLength), rowLength);
            string? error;
            if (endOfLine && k >= 0 && endOfLines == 0 && rows > 0)
            {
                // A row of Group 3 without its end-of-line code: its start is not known
                error = "the row is not preceded by an end-of-line code";
            }
            else
            {
                var isTwoDimensional = k < 0;
                if (k > 0)
                {
                    // The tag bit: 1 when the row is coded in one dimension
                    isTwoDimensional = reader.Read(1) == 0;
                }

                error = isTwoDimensional
                    ? TiffCcittDecoder.DecodeTwoDimensionalRow(ref reader, reference, current, width, out var count)
                    : TiffCcittDecoder.DecodeOneDimensionalRow(ref reader, current, width, out count);
                if (error is null && !reader.IsPastEnd)
                {
                    TiffCcittDecoder.Terminate(current, count, width);
                    row.Clear();
                    TiffCcittDecoder.Paint(current, count, row, width);
                }
            }

            if (error is not null || reader.IsPastEnd)
            {
                // A damaged row ends the decoding, unless the parameters tolerate it and an end-of-line code follows
                if (damagedRows >= damagedRowsBeforeError || !SkipToEndOfLine(ref reader))
                    break;

                damagedRows++;

                // The damaged row repeats the previous row, unless that row was damaged too; it is the reference of the next row
                if (previousIsDamaged || rows == 0)
                {
                    TiffCcittDecoder.Terminate(reference, 0, width);
                    row.Clear();
                }
                else
                {
                    // The previous row as it was written (the buffer still holds it), with 1 for black again
                    fixedOutput?.AsSpan((int)((rows - 1) * rowLength), rowLength).CopyTo(row);
                    if (!blackIs1)
                    {
                        Invert(row);
                    }
                }

                // The end-of-line code that ends the damage starts the next row
                reader.Position -= TiffCcittCodes.EndOfLineLength;
                previousIsDamaged = true;
            }
            else
            {
                (reference, current) = (current, reference);
                previousIsDamaged = false;
            }

            if (!blackIs1)
            {
                // The rows are painted with 1 for black
                Invert(row);
            }

            output?.Write(row);
            rows++;
        }

        if (fixedOutput is not null)
        {
            // The rows the data does not have are white
            fixedOutput.AsSpan((int)(rows * rowLength)).Fill(white);
            return fixedOutput;
        }

        return output!.ToArray();
    }

    /// <summary>
    /// Encodes rows with Group 4 coding (T.6): every row in two dimensions against the row above, and the
    /// end-of-facsimile-block code. The parameters of the filter are <c>/K -1</c>, <c>/Columns</c> and <c>/Rows</c>.
    /// </summary>
    /// <param name="rows">The rows: one bit per pixel, most significant bit first, each row on whole bytes; 1 is black.</param>
    /// <param name="width">The number of pixels of a row.</param>
    /// <param name="height">The number of rows.</param>
    public static byte[] EncodeGroup4(ReadOnlySpan<byte> rows, int width, int height)
    {
        var rowLength = (width + 7) / 8;
        var reference = new int[width + 8];
        var current = new int[width + 8];
        var writer = new BitWriter(Math.Max(64, rows.Length / 8));

        // The row above the first one is white: it has no change
        reference.AsSpan(0, 4).Fill(width);
        for (var y = 0; y < height; y++)
        {
            FindChanges(rows.Slice(y * rowLength, rowLength), width, current);
            WriteTwoDimensionalRow(ref writer, reference, current, width);
            (reference, current) = (current, reference);
        }

        // EOFB: two end-of-line codes (T.6, 2.4)
        writer.Write(TiffCcittCodes.EndOfLine, TiffCcittCodes.EndOfLineLength);
        writer.Write(TiffCcittCodes.EndOfLine, TiffCcittCodes.EndOfLineLength);
        return writer.ToArray();
    }

    /// <summary>
    /// Moves after the next end-of-line code: eleven zeros or more and a one (T.4, 4.1.2), which is how a decoder finds
    /// the start of the row after a damaged one.
    /// </summary>
    /// <returns><see langword="false"/> when the data has no other end-of-line code.</returns>
    private static bool SkipToEndOfLine(ref TiffBitReader reader)
    {
        var zeros = 0;
        while (!reader.IsAtEnd)
        {
            if (reader.Read(1) == 0)
            {
                zeros++;
            }
            else if (zeros >= TiffCcittCodes.EndOfLineLength - 1)
            {
                return true;
            }
            else
            {
                zeros = 0;
            }
        }

        return false;
    }

    /// <summary>Inverts the bits of a row in place: the decoder paints 1 for black.</summary>
    internal static void Invert(Span<byte> row)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            for (; i + Vector<byte>.Count <= row.Length; i += Vector<byte>.Count)
            {
                var slice = row.Slice(i, Vector<byte>.Count);
                Vector.OnesComplement(new Vector<byte>(slice)).CopyTo(slice);
            }
        }

        for (; i < row.Length; i++)
        {
            row[i] = (byte)~row[i];
        }
    }

    /// <summary>
    /// Lists the positions where the color of a row changes, the first run being white (a row that starts with a black
    /// pixel has a change at 0), followed by four entries that stand for "no further change": the end of the row.
    /// </summary>
    private static void FindChanges(ReadOnlySpan<byte> row, int width, Span<int> changes)
    {
        var count = 0;
        var color = 0;
        var position = 0;
        while (position < width)
        {
            // Whole bytes of the current color hold no change
            if ((position & 7) == 0 && position + 8 <= width && row[position >> 3] == (color == 0 ? 0x00 : 0xFF))
            {
                position += 8;
                continue;
            }

            var bit = (row[position >> 3] >> (7 - (position & 7))) & 1;
            if (bit != color)
            {
                changes[count++] = position;
                color = bit;
            }

            position++;
        }

        changes.Slice(count, 4).Fill(width);
    }

    /// <summary>
    /// Writes a row in two dimensions (T.6, 2.2.3): with <c>a0</c> the last position coded, <c>a1</c> and <c>a2</c> the
    /// next changes of the row, and <c>b1</c> and <c>b2</c> the first changes of the reference row to the right of
    /// <c>a0</c> (<c>b1</c> being a change to the color opposite to the one at <c>a0</c>): pass mode when <c>b2</c> is to
    /// the left of <c>a1</c>, vertical mode when <c>a1</c> is at most three pixels away from <c>b1</c>, horizontal mode
    /// (two run lengths) otherwise.
    /// </summary>
    private static void WriteTwoDimensionalRow(ref BitWriter writer, ReadOnlySpan<int> reference, ReadOnlySpan<int> changes, int width)
    {
        var a0 = -1;
        var isBlack = false;
        var a = 0;
        var b = 0;
        while (a0 < width)
        {
            // The changes of even index are changes to black: the index of b1 has the parity of the color at a0
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

                // The color at a0 changed: so does the parity of b1
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

    /// <summary>Writes a run as make-up codes and a terminating code (T.4, 4.1.1).</summary>
    private static void WriteRun(ref BitWriter writer, int run, bool isBlack)
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

    /// <summary>Writes codes, most significant bit first; the last byte is padded with zeros.</summary>
    private struct BitWriter(int capacity)
    {
        private byte[] _buffer = new byte[capacity];
        private int _length;
        private ulong _bits;
        private int _count;

        public void Write(uint code, int length)
        {
            _bits = (_bits << length) | (code & ((1u << length) - 1));
            _count += length;
            while (_count >= 8)
            {
                _count -= 8;
                if (_length == _buffer.Length)
                {
                    Array.Resize(ref _buffer, checked(_buffer.Length * 2));
                }

                _buffer[_length++] = (byte)(_bits >> _count);
            }
        }

        public byte[] ToArray()
        {
            if (_count > 0)
            {
                Write(0, 8 - _count);
            }

            return _buffer.AsSpan(0, _length).ToArray();
        }
    }
}
