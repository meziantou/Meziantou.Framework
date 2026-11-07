using System.Globalization;
using System.Numerics;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>The predictors of the <c>FlateDecode</c> and <c>LZWDecode</c> filters (ISO 32000-2, 7.4.4.4): TIFF predictor 2 and the PNG filters.</summary>
/// <remarks>
/// A truncated last row keeps the data it has, and nothing else: the PNG predictors decode the bytes after its filter
/// type, the TIFF predictor the complete samples it holds (the bits of an incomplete sample are left as they are). The
/// decoded data is never longer than the encoded data.
/// </remarks>
internal static class Predictor
{
    /// <summary>Decodes the data of a filter with the predictor of its parameters.</summary>
    /// <param name="data">The output of the filter. The decoding may reuse the array: the caller gives it up.</param>
    /// <param name="parameters">The parameters of the filter.</param>
    /// <param name="limit">The maximum length of the decoded data.</param>
    public static byte[] Decode(byte[] data, PdfDictionary? parameters, long limit)
    {
        if (parameters is null)
            return data;

        var predictor = parameters.GetInteger(PdfNames.Predictor, 1);
        if (predictor <= 1)
            return data;

        var colors = (int)Math.Clamp(parameters.GetInteger(PdfNames.Colors, 1), 1, 32);
        var bitsPerComponent = (int)parameters.GetInteger(PdfNames.BitsPerComponent, 8);
        if (bitsPerComponent is not (1 or 2 or 4 or 8 or 16))
            throw new InvalidPdfContentException($"The BitsPerComponent of a predictor must be 1, 2, 4, 8 or 16, not {bitsPerComponent}");

        // A row (and its PNG filter type) must fit in an array
        var bitsPerPixel = colors * bitsPerComponent;
        var maxColumns = (Array.MaxLength - 1L) * 8 / bitsPerPixel;
        var columns = parameters.GetInteger(PdfNames.Columns, 1);
        if (columns < 1 || columns > maxColumns)
            throw new InvalidPdfContentException(string.Create(CultureInfo.InvariantCulture, $"The Columns of a predictor must be from 1 to {maxColumns} for {colors} colors of {bitsPerComponent} bits, not {columns}"));

        var rowLength = (int)(((columns * bitsPerPixel) + 7) / 8);
        var bytesPerPixel = Math.Max(1, (bitsPerPixel + 7) / 8);
        if (predictor == 2)
            return DecodeTiff(data, rowLength, colors, bitsPerComponent, columns);

        if (predictor is >= 10 and <= 15)
            return DecodePng(data, rowLength, bytesPerPixel, limit);

        throw new InvalidPdfContentException($"Unknown predictor {predictor}");
    }

    /// <summary>Applies the PNG Up filter to every row (predictor 12), for the cross-reference streams the writer compresses.</summary>
    public static byte[] EncodePngUp(ReadOnlySpan<byte> data, int rowLength)
    {
        var rows = (data.Length + rowLength - 1) / rowLength;
        var output = new byte[rows * (rowLength + 1)];
        for (var row = 0; row < rows; row++)
        {
            var target = output.AsSpan(row * (rowLength + 1), rowLength + 1);
            target[0] = 2;
            for (var i = 0; i < rowLength; i++)
            {
                var index = (row * rowLength) + i;
                var value = index < data.Length ? data[index] : (byte)0;
                var above = row > 0 ? data[index - rowLength] : (byte)0;
                target[i + 1] = (byte)(value - above);
            }
        }

        return output;
    }

    /// <summary>
    /// Applies a PNG filter to each row (predictor 15): the filter whose bytes, as signed values, have the smallest sum of
    /// absolute values, the heuristic of the PNG specification (12.8).
    /// </summary>
    public static byte[] EncodePng(ReadOnlySpan<byte> data, int rowLength, int bytesPerPixel)
    {
        var rows = (int)((data.Length + (long)rowLength - 1) / rowLength);
        var output = new byte[data.Length + rows];
        var candidate = new byte[rowLength];
        for (var row = 0; row < rows; row++)
        {
            var start = row * rowLength;
            var current = data.Slice(start, Math.Min(rowLength, data.Length - start));
            var previous = row > 0 ? data.Slice(start - rowLength, current.Length) : [];
            var target = output.AsSpan(start + row, current.Length + 1);
            var best = target[1..];
            current.CopyTo(best);
            target[0] = 0;
            var bestCost = Cost(best);
            for (var filter = (byte)1; filter <= 4; filter++)
            {
                var filtered = candidate.AsSpan(0, current.Length);
                Filter(filter, current, previous, filtered, bytesPerPixel);
                var cost = Cost(filtered);
                if (cost < bestCost)
                {
                    filtered.CopyTo(best);
                    target[0] = filter;
                    bestCost = cost;
                }
            }
        }

        return output;
    }

    private static long Cost(ReadOnlySpan<byte> filtered)
    {
        var cost = 0L;
        foreach (var value in filtered)
        {
            cost += value < 128 ? value : 256 - value;
        }

        return cost;
    }

    /// <summary>Applies a PNG filter to a row (PNG, 9.2): the previous row is empty for the first row, whose bytes above are 0.</summary>
    private static void Filter(byte filter, ReadOnlySpan<byte> current, ReadOnlySpan<byte> previous, Span<byte> target, int bytesPerPixel)
    {
        var first = Math.Min(bytesPerPixel, current.Length);
        switch (filter)
        {
            case 1:
                current[..first].CopyTo(target);
                for (var i = first; i < current.Length; i++)
                {
                    target[i] = (byte)(current[i] - current[i - bytesPerPixel]);
                }

                break;

            case 2 when previous.IsEmpty:
                current.CopyTo(target);
                break;

            case 2:
                for (var i = 0; i < current.Length; i++)
                {
                    target[i] = (byte)(current[i] - previous[i]);
                }

                break;

            case 3:
                for (var i = 0; i < current.Length; i++)
                {
                    var left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                    var up = previous.IsEmpty ? 0 : previous[i];
                    target[i] = (byte)(current[i] - ((left + up) >> 1));
                }

                break;

            default:
                for (var i = 0; i < current.Length; i++)
                {
                    var left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                    var up = previous.IsEmpty ? 0 : previous[i];
                    var upLeft = i >= bytesPerPixel && !previous.IsEmpty ? previous[i - bytesPerPixel] : 0;
                    target[i] = (byte)(current[i] - Paeth(left, up, upLeft));
                }

                break;
        }
    }

    private static byte[] DecodePng(byte[] data, int rowLength, int bytesPerPixel, long limit)
    {
        // Each row is its filter type and its bytes; a last row with its type alone has no data
        var stride = rowLength + 1L;
        var fullRows = data.Length / stride;
        var lastRow = (int)(data.Length % stride);
        var length = (fullRows * rowLength) + Math.Max(0, lastRow - 1);
        if (length > limit)
            throw new PdfResourceLimitException(string.Create(CultureInfo.InvariantCulture, $"The decoded data of a stream exceeds {limit} bytes ({nameof(PdfReadOptions)}.{nameof(PdfReadOptions.MaxDecodedStreamLength)})"));

        var output = new byte[length];
        var rows = fullRows + (lastRow > 1 ? 1 : 0);
        for (var row = 0L; row < rows; row++)
        {
            var offset = (int)(row * stride);
            var available = (int)Math.Min(rowLength, data.Length - offset - 1);
            var current = output.AsSpan((int)(row * rowLength), available);
            var previous = row > 0 ? output.AsSpan((int)((row - 1) * rowLength), available) : [];
            Unfilter(data[offset], data.AsSpan(offset + 1, available), current, previous, bytesPerPixel);
        }

        return output;
    }

    /// <summary>Reverses the PNG filter of a row (PNG, 9.2): the previous row is empty for the first row, whose bytes above are 0.</summary>
    private static void Unfilter(byte filter, ReadOnlySpan<byte> source, Span<byte> current, ReadOnlySpan<byte> previous, int bytesPerPixel)
    {
        var first = Math.Min(bytesPerPixel, source.Length);
        switch (filter)
        {
            case 1:
                Sub(source, current, first, bytesPerPixel);
                break;

            case 2 when !previous.IsEmpty:
                var i = 0;
                if (Vector.IsHardwareAccelerated)
                {
                    for (; i + Vector<byte>.Count <= source.Length; i += Vector<byte>.Count)
                    {
                        (new Vector<byte>(source[i..]) + new Vector<byte>(previous[i..])).CopyTo(current[i..]);
                    }
                }

                for (; i < source.Length; i++)
                {
                    current[i] = (byte)(source[i] + previous[i]);
                }

                break;

            case 3 when previous.IsEmpty:
                source[..first].CopyTo(current);
                for (var j = first; j < source.Length; j++)
                {
                    current[j] = (byte)(source[j] + (current[j - bytesPerPixel] >> 1));
                }

                break;

            case 3:
                for (var j = 0; j < first; j++)
                {
                    current[j] = (byte)(source[j] + (previous[j] >> 1));
                }

                for (var j = first; j < source.Length; j++)
                {
                    current[j] = (byte)(source[j] + ((current[j - bytesPerPixel] + previous[j]) >> 1));
                }

                break;

            case 4 when previous.IsEmpty:
                // The bytes above are 0: the Paeth predictor is the byte on the left
                Sub(source, current, first, bytesPerPixel);
                break;

            case 4:
                for (var j = 0; j < first; j++)
                {
                    current[j] = (byte)(source[j] + previous[j]);
                }

                for (var j = first; j < source.Length; j++)
                {
                    current[j] = (byte)(source[j] + Paeth(current[j - bytesPerPixel], previous[j], previous[j - bytesPerPixel]));
                }

                break;

            default:
                // None, Up on the first row, and the unknown types
                source.CopyTo(current);
                break;
        }
    }

    private static void Sub(ReadOnlySpan<byte> source, Span<byte> current, int first, int bytesPerPixel)
    {
        source[..first].CopyTo(current);
        for (var i = first; i < source.Length; i++)
        {
            current[i] = (byte)(source[i] + current[i - bytesPerPixel]);
        }
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var distanceLeft = Math.Abs(estimate - left);
        var distanceUp = Math.Abs(estimate - up);
        var distanceUpLeft = Math.Abs(estimate - upLeft);
        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
            return left;

        return distanceUp <= distanceUpLeft ? up : upLeft;
    }

    /// <summary>Reverses the TIFF predictor 2 (TIFF 6.0, section 14) in place: each sample is the difference with the sample of the same component on its left.</summary>
    private static byte[] DecodeTiff(byte[] data, int rowLength, int colors, int bitsPerComponent, long columns)
    {
        for (var offset = 0L; offset < data.Length; offset += rowLength)
        {
            var current = data.AsSpan((int)offset, (int)Math.Min(rowLength, data.Length - offset));
            switch (bitsPerComponent)
            {
                case 8:
                    for (var i = colors; i < current.Length; i++)
                    {
                        current[i] = (byte)(current[i] + current[i - colors]);
                    }

                    break;

                case 16:
                    for (var i = colors * 2; i + 1 < current.Length; i += 2)
                    {
                        var value = (current[i] << 8) | current[i + 1];
                        var left = (current[i - (colors * 2)] << 8) | current[i - (colors * 2) + 1];
                        var sum = (value + left) & 0xFFFF;
                        current[i] = (byte)(sum >> 8);
                        current[i + 1] = (byte)sum;
                    }

                    break;

                default:
                    // The complete samples of the row: the last bits of a row are padding
                    var samples = Math.Min(columns * colors, (long)current.Length * 8 / bitsPerComponent);
                    var mask = (1 << bitsPerComponent) - 1;
                    for (var sample = (long)colors; sample < samples; sample++)
                    {
                        var value = ReadBits(current, sample * bitsPerComponent, bitsPerComponent);
                        var left = ReadBits(current, (sample - colors) * bitsPerComponent, bitsPerComponent);
                        WriteBits(current, sample * bitsPerComponent, bitsPerComponent, (value + left) & mask);
                    }

                    break;
            }
        }

        return data;
    }

    private static int ReadBits(ReadOnlySpan<byte> data, long bitOffset, int bitCount)
    {
        var shift = 8 - bitCount - (int)(bitOffset & 7);
        return (data[(int)(bitOffset >> 3)] >> shift) & ((1 << bitCount) - 1);
    }

    private static void WriteBits(Span<byte> data, long bitOffset, int bitCount, int value)
    {
        var shift = 8 - bitCount - (int)(bitOffset & 7);
        var mask = ((1 << bitCount) - 1) << shift;
        var index = (int)(bitOffset >> 3);
        data[index] = (byte)((data[index] & ~mask) | (value << shift));
    }
}
