using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The sums of the blurs and the shadows of the drawings: the weights of a kernel applied along a row, and the rows of a
/// column added with their weights. The vectorized paths add the same products in the same order as the scalar ones,
/// without fused multiply-add, so that they give the same bits.
/// </summary>
internal static class BlurKernels
{
    /// <summary>Adds a row of values multiplied by a weight to a row of sums: <c>sums[i] += weight * values[i]</c>.</summary>
    public static void Accumulate(Span<double> sums, double weight, ReadOnlySpan<float> values)
    {
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var factor = Vector128.Create(weight);
            for (; i + 4 <= values.Length; i += 4)
            {
                var value = Vector128.Create(values.Slice(i, 4));
                var low = sums.Slice(i, 2);
                var high = sums.Slice(i + 2, 2);
                (Vector128.Create(low) + (factor * Vector128.WidenLower(value))).CopyTo(low);
                (Vector128.Create(high) + (factor * Vector128.WidenUpper(value))).CopyTo(high);
            }
        }

        for (; i < values.Length; i++)
        {
            sums[i] += weight * values[i];
        }
    }

    /// <summary>The scalar reference of <see cref="Accumulate"/>.</summary>
    public static void AccumulateScalar(Span<double> sums, double weight, ReadOnlySpan<float> values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            sums[i] += weight * values[i];
        }
    }

    /// <summary>
    /// Blurs a row of premultiplied colors (four values per pixel) along itself: for each pixel of the result, the sum
    /// over the taps of the weight times the pixel <c>firstColumn + x + tap</c>, the pixels beyond the row being its
    /// first or its last one, kept in single precision.
    /// </summary>
    public static void BlurRow(ReadOnlySpan<double> premultiplied, ReadOnlySpan<double> weights, int firstColumn, Span<float> blurred)
    {
        var count = blurred.Length / 4;
        var taps = weights.Length;
        var last = (premultiplied.Length / 4) - 1;
        if (!Vector128.IsHardwareAccelerated)
        {
            BlurRowScalar(premultiplied, weights, firstColumn, blurred, 0, count);
            return;
        }

        // The pixels whose taps all read pixels of the row
        var start = Math.Clamp(-firstColumn, 0, count);
        var end = Math.Clamp(last - taps + 2 - firstColumn, start, count);
        BlurRowScalar(premultiplied, weights, firstColumn, blurred, 0, start);
        var x = start;
        for (; x + 2 <= end; x += 2)
        {
            var red0 = Vector128<double>.Zero;
            var blue0 = Vector128<double>.Zero;
            var red1 = Vector128<double>.Zero;
            var blue1 = Vector128<double>.Zero;
            var entries = premultiplied[((firstColumn + x) * 4)..];
            for (var tap = 0; tap < taps; tap++)
            {
                var weight = Vector128.Create(weights[tap]);
                var entry = entries.Slice(tap * 4, 8);
                red0 += weight * Vector128.Create(entry[..2]);
                blue0 += weight * Vector128.Create(entry.Slice(2, 2));
                red1 += weight * Vector128.Create(entry.Slice(4, 2));
                blue1 += weight * Vector128.Create(entry[6..]);
            }

            Vector128.Narrow(red0, blue0).CopyTo(blurred.Slice(x * 4, 4));
            Vector128.Narrow(red1, blue1).CopyTo(blurred.Slice((x + 1) * 4, 4));
        }

        BlurRowScalar(premultiplied, weights, firstColumn, blurred, x, count);
    }

    /// <summary>The scalar reference of <see cref="BlurRow"/>.</summary>
    public static void BlurRowScalar(ReadOnlySpan<double> premultiplied, ReadOnlySpan<double> weights, int firstColumn, Span<float> blurred)
        => BlurRowScalar(premultiplied, weights, firstColumn, blurred, 0, blurred.Length / 4);

    /// <summary>
    /// Blurs a row of values along itself: for each value of the result, the sum over the taps of the weight times the
    /// value <c>firstColumn + x + tap</c>, the values beyond the row being its first or its last one, kept in single
    /// precision.
    /// </summary>
    public static void BlurValues(ReadOnlySpan<double> values, ReadOnlySpan<double> weights, int firstColumn, Span<float> blurred)
    {
        var count = blurred.Length;
        var taps = weights.Length;
        var last = values.Length - 1;
        if (!Vector128.IsHardwareAccelerated)
        {
            BlurValuesScalar(values, weights, firstColumn, blurred, 0, count);
            return;
        }

        // The values whose taps all read values of the row; two of them per vector
        var start = Math.Clamp(-firstColumn, 0, count);
        var end = Math.Clamp(last - taps + 2 - firstColumn, start, count);
        BlurValuesScalar(values, weights, firstColumn, blurred, 0, start);
        var x = start;
        for (; x + 4 <= end; x += 4)
        {
            var sum0 = Vector128<double>.Zero;
            var sum1 = Vector128<double>.Zero;
            var entries = values[(firstColumn + x)..];
            for (var tap = 0; tap < taps; tap++)
            {
                var weight = Vector128.Create(weights[tap]);
                var entry = entries.Slice(tap, 4);
                sum0 += weight * Vector128.Create(entry[..2]);
                sum1 += weight * Vector128.Create(entry[2..]);
            }

            Vector128.Narrow(sum0, sum1).CopyTo(blurred.Slice(x, 4));
        }

        BlurValuesScalar(values, weights, firstColumn, blurred, x, count);
    }

    /// <summary>The scalar reference of <see cref="BlurValues"/>.</summary>
    public static void BlurValuesScalar(ReadOnlySpan<double> values, ReadOnlySpan<double> weights, int firstColumn, Span<float> blurred)
        => BlurValuesScalar(values, weights, firstColumn, blurred, 0, blurred.Length);

    private static void BlurRowScalar(ReadOnlySpan<double> premultiplied, ReadOnlySpan<double> weights, int firstColumn, Span<float> blurred, int start, int end)
    {
        var last = (premultiplied.Length / 4) - 1;
        for (var x = start; x < end; x++)
        {
            double red = 0, green = 0, blue = 0, alpha = 0;
            var first = firstColumn + x;
            for (var tap = 0; tap < weights.Length; tap++)
            {
                var index = first + tap;
                var entry = premultiplied.Slice((index < 0 ? 0 : index > last ? last : index) * 4, 4);
                var weight = weights[tap];
                red += weight * entry[0];
                green += weight * entry[1];
                blue += weight * entry[2];
                alpha += weight * entry[3];
            }

            var output = blurred.Slice(x * 4, 4);
            output[0] = (float)red;
            output[1] = (float)green;
            output[2] = (float)blue;
            output[3] = (float)alpha;
        }
    }

    private static void BlurValuesScalar(ReadOnlySpan<double> values, ReadOnlySpan<double> weights, int firstColumn, Span<float> blurred, int start, int end)
    {
        var last = values.Length - 1;
        for (var x = start; x < end; x++)
        {
            var sum = 0d;
            var first = firstColumn + x;
            for (var tap = 0; tap < weights.Length; tap++)
            {
                var index = first + tap;
                sum += weights[tap] * values[index < 0 ? 0 : index > last ? last : index];
            }

            blurred[x] = (float)sum;
        }
    }
}
