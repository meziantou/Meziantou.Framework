using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>
/// The <c>CCITTFaxDecode</c> filter (ISO 32000-2, 7.4.6): Group 3 one- and two-dimensional and Group 4 facsimile coding
/// (ITU-T T.4 and T.6). The rows are decoded by the CCITT decoder of the TIFF codec of Meziantou.Framework.Imaging; this
/// class reads them as the parameters of the filter say.
/// </summary>
/// <remarks>
/// The output is one bit per pixel, most significant bit first, each row starting on a byte: 1 for white and 0 for black,
/// or the opposite with <c>/BlackIs1 true</c>. Damaged data is not an error: the rows decoded before it are kept, and the
/// missing rows are white, as viewers show a damaged fax.
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
        if (columns is < 1 or > MaxColumns)
            throw new InvalidPdfContentException($"The /Columns of the CCITTFaxDecode filter must be from 1 to {MaxColumns}");

        if (declaredRows < 0)
            throw new InvalidPdfContentException("The /Rows of the CCITTFaxDecode filter must not be negative");

        var width = (int)columns;
        var rowLength = (width + 7) / 8;
        if (declaredRows * rowLength > limit)
            throw new PdfResourceLimitException($"The CCITTFaxDecode data is larger than the limit of {limit} bytes");

        var output = new BoundedOutput(limit, (int)Math.Min(declaredRows * rowLength, 1 << 20));
        var changeListLength = (int)TiffCcittDecoder.GetChangeListLength(width);
        var reference = new int[changeListLength];
        var current = new int[changeListLength];
        var row = new byte[rowLength];
        var reader = new TiffBitReader(data);

        // The row above the first one is white: it has no change
        TiffCcittDecoder.Terminate(reference, 0, width);
        var rows = 0L;
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

            var isTwoDimensional = k < 0;
            if (k > 0)
            {
                // The tag bit: 1 when the row is coded in one dimension
                isTwoDimensional = reader.Read(1) == 0;
            }

            var error = isTwoDimensional
                ? TiffCcittDecoder.DecodeTwoDimensionalRow(ref reader, reference, current, width, out var count)
                : TiffCcittDecoder.DecodeOneDimensionalRow(ref reader, current, width, out count);
            if (error is not null || reader.IsPastEnd)
                break;

            TiffCcittDecoder.Terminate(current, count, width);
            row.AsSpan().Clear();
            TiffCcittDecoder.Paint(current, count, row, width);
            WriteRow(output, row, blackIs1);
            rows++;
            (reference, current) = (current, reference);
        }

        // The rows the data does not have are white
        if (rows < declaredRows)
        {
            row.AsSpan().Clear();
            while (rows < declaredRows)
            {
                WriteRow(output, row, blackIs1);
                rows++;
            }
        }

        return output.ToArray();
    }

    /// <summary>Writes a row whose bits are 1 for black: as they are with <c>/BlackIs1 true</c>, inverted otherwise.</summary>
    private static void WriteRow(BoundedOutput output, byte[] row, bool blackIs1)
    {
        if (blackIs1)
        {
            output.Write(row);
            return;
        }

        Span<byte> inverted = row.Length <= 1024 ? stackalloc byte[row.Length] : new byte[row.Length];
        for (var i = 0; i < row.Length; i++)
        {
            inverted[i] = (byte)~row[i];
        }

        output.Write(inverted);
    }
}
