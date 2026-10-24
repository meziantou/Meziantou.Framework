using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>The predictors of the <c>FlateDecode</c> and <c>LZWDecode</c> filters (ISO 32000-2, 7.4.4.4): TIFF predictor 2 and the PNG filters.</summary>
internal static class Predictor
{
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

        var columns = parameters.GetInteger(PdfNames.Columns, 1);
        if (columns < 1 || columns > int.MaxValue / 64)
            throw new InvalidPdfContentException($"The Columns of a predictor must be positive, not {columns}");

        var bitsPerPixel = colors * bitsPerComponent;
        var rowLength = (int)(((columns * bitsPerPixel) + 7) / 8);
        var bytesPerPixel = Math.Max(1, (bitsPerPixel + 7) / 8);
        if (predictor == 2)
            return DecodeTiff(data, rowLength, colors, bitsPerComponent, (int)columns);

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

    private static byte[] DecodePng(byte[] data, int rowLength, int bytesPerPixel, long limit)
    {
        var stride = rowLength + 1;
        var rows = (data.Length + stride - 1) / stride;
        if ((long)rows * rowLength > limit)
            throw new PdfResourceLimitException($"The decoded data of a stream exceeds {limit} bytes");

        var output = new byte[rows * rowLength];
        var previous = new byte[rowLength];
        for (var row = 0; row < rows; row++)
        {
            var offset = row * stride;
            var filter = data[offset];
            var available = Math.Min(rowLength, data.Length - offset - 1);
            var current = output.AsSpan(row * rowLength, rowLength);
            data.AsSpan(offset + 1, available).CopyTo(current);
            for (var i = 0; i < rowLength; i++)
            {
                var left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                var up = previous[i];
                var upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
                current[i] = filter switch
                {
                    1 => (byte)(current[i] + left),
                    2 => (byte)(current[i] + up),
                    3 => (byte)(current[i] + ((left + up) >> 1)),
                    4 => (byte)(current[i] + Paeth(left, up, upLeft)),
                    _ => current[i],
                };
            }

            current.CopyTo(previous);
        }

        return output;
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

    private static byte[] DecodeTiff(byte[] data, int rowLength, int colors, int bitsPerComponent, int columns)
    {
        var output = data.ToArray();
        var rows = output.Length / rowLength;
        for (var row = 0; row < rows; row++)
        {
            var current = output.AsSpan(row * rowLength, rowLength);
            switch (bitsPerComponent)
            {
                case 8:
                    for (var i = colors; i < rowLength; i++)
                    {
                        current[i] = (byte)(current[i] + current[i - colors]);
                    }

                    break;
                case 16:
                    for (var i = colors * 2; i + 1 < rowLength; i += 2)
                    {
                        var value = (current[i] << 8) | current[i + 1];
                        var left = (current[i - (colors * 2)] << 8) | current[i - (colors * 2) + 1];
                        var sum = (value + left) & 0xFFFF;
                        current[i] = (byte)(sum >> 8);
                        current[i + 1] = (byte)sum;
                    }

                    break;
                default:
                    var mask = (1 << bitsPerComponent) - 1;
                    for (var sample = colors; sample < columns * colors; sample++)
                    {
                        var value = ReadBits(current, sample * bitsPerComponent, bitsPerComponent);
                        var left = ReadBits(current, (sample - colors) * bitsPerComponent, bitsPerComponent);
                        WriteBits(current, sample * bitsPerComponent, bitsPerComponent, (value + left) & mask);
                    }

                    break;
            }
        }

        return output;
    }

    private static int ReadBits(ReadOnlySpan<byte> data, int bitOffset, int bitCount)
    {
        var shift = 8 - bitCount - (bitOffset & 7);
        return (data[bitOffset >> 3] >> shift) & ((1 << bitCount) - 1);
    }

    private static void WriteBits(Span<byte> data, int bitOffset, int bitCount, int value)
    {
        var shift = 8 - bitCount - (bitOffset & 7);
        var mask = ((1 << bitCount) - 1) << shift;
        data[bitOffset >> 3] = (byte)((data[bitOffset >> 3] & ~mask) | (value << shift));
    }
}
