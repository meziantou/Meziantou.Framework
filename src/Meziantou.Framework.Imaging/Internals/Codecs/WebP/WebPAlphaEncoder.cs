using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Encodes the alpha values of a lossy frame as an <c>ALPH</c> chunk payload (WebP container specification, "Alpha"):
/// lossless compression (a headerless VP8L image stream whose green component holds the filtered values), with the
/// filter (none, horizontal, vertical or gradient) of smallest estimated entropy (effort 3 and above; otherwise no filter).
/// Alpha is always exact; no preprocessing is applied.
/// </summary>
internal static class WebPAlphaEncoder
{
    // The filtered values are computed in chunks of a row
    private const int ChunkLength = 256;

    /// <summary>Encodes the alpha channel of <paramref name="rgba"/>.</summary>
    /// <returns>The compressed bitstream (after the header byte) and the header byte.</returns>
    public static (LsbBitWriter Bitstream, byte Header) Encode(AllocationScope scope, ReadOnlySpan<byte> rgba, int width, int height, int effort, CancellationToken cancellationToken)
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
        Span<byte> values = stackalloc byte[ChunkLength];
        for (var y = 0; y < height; y++)
        {
            for (var x0 = 0; x0 < width; x0 += ChunkLength)
            {
                var x1 = Math.Min(width, x0 + ChunkLength);
                var chunk = values[..(x1 - x0)];
                FilterRow(alpha, width, y, x0, x1, filter, chunk);
                ToGreen(chunk, argb.Slice((y * width) + x0, x1 - x0));
            }
        }

        LsbBitWriter? writer = null;
        try
        {
            writer = new LsbBitWriter(scope);
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
        Span<byte> values = stackalloc byte[ChunkLength];
        for (var y = 0; y < height; y++)
        {
            for (var x0 = 0; x0 < width; x0 += ChunkLength)
            {
                var x1 = Math.Min(width, x0 + ChunkLength);
                var chunk = values[..(x1 - x0)];
                FilterRow(alpha, width, y, x0, x1, filter, chunk);
                foreach (var value in chunk)
                {
                    histogram[value]++;
                }
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

    /// <summary>
    /// Computes the filtered values <paramref name="x0"/> to <paramref name="x1"/> of row <paramref name="y"/>: the value minus
    /// its prediction from the original values (<see cref="WebPAlphaDecoder.Predict"/>), the value itself without filter.
    /// Values that are not on the first row or column are computed 16 at a time.
    /// </summary>
    internal static void FilterRow(ReadOnlySpan<byte> alpha, int width, int y, int x0, int x1, int filter, Span<byte> values)
    {
        var row = alpha.Slice(y * width, width);
        if (filter == 0)
        {
            row[x0..x1].CopyTo(values);
            return;
        }

        var above = y == 0 ? default : alpha.Slice((y - 1) * width, width);
        var x = x0;
        if (y > 0 && Vector128.IsHardwareAccelerated)
        {
            if (x == 0)
            {
                values[0] = (byte)(row[0] - above[0]);
                x = 1;
            }

            for (; x <= x1 - Vector128<byte>.Count; x += Vector128<byte>.Count)
            {
                var current = Vector128.Create(row.Slice(x, Vector128<byte>.Count));
                Vector128<byte> prediction;
                if (filter == 1)
                {
                    prediction = Vector128.Create(row.Slice(x - 1, Vector128<byte>.Count));
                }
                else if (filter == 2)
                {
                    prediction = Vector128.Create(above.Slice(x, Vector128<byte>.Count));
                }
                else
                {
                    // Gradient: left + top - top-left clamped to 0-255, in 16-bit lanes
                    var left = Vector128.Create(row.Slice(x - 1, Vector128<byte>.Count));
                    var top = Vector128.Create(above.Slice(x, Vector128<byte>.Count));
                    var topLeft = Vector128.Create(above.Slice(x - 1, Vector128<byte>.Count));
                    prediction = Vector128.Narrow(Gradient(left, top, topLeft, upper: false), Gradient(left, top, topLeft, upper: true));
                }

                (current - prediction).CopyTo(values.Slice(x - x0, Vector128<byte>.Count));
            }
        }

        for (; x < x1; x++)
        {
            values[x - x0] = (byte)(row[x] - WebPAlphaDecoder.Predict(row, above, x, y, filter));
        }

        static Vector128<ushort> Gradient(Vector128<byte> left, Vector128<byte> top, Vector128<byte> topLeft, bool upper)
        {
            var sum = (Widen(left, upper) + Widen(top, upper)) - Widen(topLeft, upper);
            return Vector128.Min(Vector128.Max(sum, Vector128<short>.Zero), Vector128.Create((short)255)).AsUInt16();
        }

        static Vector128<short> Widen(Vector128<byte> value, bool upper) => (upper ? Vector128.WidenUpper(value) : Vector128.WidenLower(value)).AsInt16();
    }

    /// <summary>The scalar reference of <see cref="FilterRow"/>.</summary>
    internal static void FilterRowScalar(ReadOnlySpan<byte> alpha, int width, int y, int x0, int x1, int filter, Span<byte> values)
    {
        var row = alpha.Slice(y * width, width);
        var above = y == 0 ? default : alpha.Slice((y - 1) * width, width);
        for (var x = x0; x < x1; x++)
        {
            values[x - x0] = filter == 0 ? row[x] : (byte)(row[x] - WebPAlphaDecoder.Predict(row, above, x, y, filter));
        }
    }

    // The values in the green component of opaque pixels, 16 at a time
    private static void ToGreen(ReadOnlySpan<byte> values, Span<uint> argb)
    {
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var opaque = Vector128.Create(0xFF000000u);
            for (; i <= values.Length - Vector128<byte>.Count; i += Vector128<byte>.Count)
            {
                var bytes = Vector128.Create(values.Slice(i, Vector128<byte>.Count));
                var low = Vector128.WidenLower(bytes);
                var high = Vector128.WidenUpper(bytes);
                (Vector128.ShiftLeft(Vector128.WidenLower(low), 8) | opaque).CopyTo(argb.Slice(i, 4));
                (Vector128.ShiftLeft(Vector128.WidenUpper(low), 8) | opaque).CopyTo(argb.Slice(i + 4, 4));
                (Vector128.ShiftLeft(Vector128.WidenLower(high), 8) | opaque).CopyTo(argb.Slice(i + 8, 4));
                (Vector128.ShiftLeft(Vector128.WidenUpper(high), 8) | opaque).CopyTo(argb.Slice(i + 12, 4));
            }
        }

        for (; i < values.Length; i++)
        {
            argb[i] = 0xFF000000u | ((uint)values[i] << 8);
        }
    }
}
