namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Encodes the alpha values of a lossy frame as an <c>ALPH</c> chunk payload (WebP container specification, "Alpha"):
/// lossless compression (a headerless VP8L image stream whose green component holds the filtered values), with the
/// filter (none, horizontal, vertical or gradient) of smallest estimated entropy (effort 3 and above; otherwise no filter).
/// Alpha is always exact; no preprocessing is applied.
/// </summary>
internal static class WebPAlphaEncoder
{
    /// <summary>Encodes the alpha channel of <paramref name="rgba"/>.</summary>
    /// <returns>The compressed bitstream (after the header byte) and the header byte.</returns>
    public static (Vp8LBitWriter Bitstream, byte Header) Encode(AllocationScope scope, ReadOnlySpan<byte> rgba, int width, int height, int effort, CancellationToken cancellationToken)
    {
        var count = width * height;
        using var alphaBuffer = scope.Rent(count, AllocationKind.Temporary, clear: false);
        var alpha = alphaBuffer.RawBuffer.AsSpan(0, count);
        for (var i = 0; i < count; i++)
        {
            alpha[i] = rgba[(i * 4) + 3];
        }

        var filter = 0;
        if (effort >= 3)
        {
            var bestCost = double.MaxValue;
            for (var candidate = 0; candidate < 4; candidate++)
            {
                var cost = FilteredEntropy(alpha, width, height, candidate);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    filter = candidate;
                }
            }
        }

        using var pixelBuffer = scope.Rent(count * sizeof(uint), AllocationKind.Temporary, clear: false);
        var argb = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(pixelBuffer.RawBuffer.AsSpan()))[..count];
        for (var y = 0; y < height; y++)
        {
            var row = alpha.Slice(y * width, width);
            var above = y == 0 ? default : alpha.Slice((y - 1) * width, width);
            for (var x = 0; x < width; x++)
            {
                var value = filter == 0 ? row[x] : (byte)(row[x] - WebPAlphaDecoder.Predict(row, above, x, y, filter));
                argb[(y * width) + x] = 0xFF000000u | ((uint)value << 8);
            }
        }

        Vp8LBitWriter? writer = null;
        try
        {
            writer = new Vp8LBitWriter(scope);
            Vp8LEncoder.EncodeImageStream(scope, writer, argb, width, height, effort, cancellationToken);
            writer.Finish();
            var result = (writer, (byte)(1 | (filter << 2)));
            writer = null;
            return result;
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private static double FilteredEntropy(ReadOnlySpan<byte> alpha, int width, int height, int filter)
    {
        Span<int> histogram = stackalloc int[256];
        histogram.Clear();
        for (var y = 0; y < height; y++)
        {
            var row = alpha.Slice(y * width, width);
            var above = y == 0 ? default : alpha.Slice((y - 1) * width, width);
            for (var x = 0; x < width; x++)
            {
                var value = filter == 0 ? row[x] : (byte)(row[x] - WebPAlphaDecoder.Predict(row, above, x, y, filter));
                histogram[value]++;
            }
        }

        var total = (double)width * height;
        var bits = 0.0;
        foreach (var value in histogram)
        {
            if (value != 0)
            {
                bits += value * Math.Log2(total / value);
            }
        }

        return bits;
    }
}
