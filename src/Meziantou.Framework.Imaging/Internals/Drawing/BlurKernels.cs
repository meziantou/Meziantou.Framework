using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The sums of the blurs and the shadows of the drawings: the weights of a kernel applied along a row, and the rows of a
/// column added with their weights. The vectorized paths add the same products in the same order as the scalar ones,
/// without fused multiply-add, so that they give the same bits.
/// </summary>
internal static class BlurKernels
{
    /// <summary>The largest number of rows <see cref="SumRows"/> adds in one call.</summary>
    public const int MaxSummedRows = 32;

    /// <summary>
    /// Adds rows of values multiplied by their weights to a row of sums, in the order of the rows:
    /// <c>sums[i] = (((s + weights[0] rows[offsets[0] + i]) + weights[1] rows[offsets[1] + i]) + ...)</c>, where
    /// <c>s</c> is 0 for the first rows of a sum and the sum so far otherwise. Each sum stays in a register while
    /// the rows are added to it, and the products are added one by one, without fused multiply-add.
    /// </summary>
    /// <param name="sums">The sums.</param>
    /// <param name="rows">The values of the rows.</param>
    /// <param name="offsets">Where each row starts in <paramref name="rows"/>.</param>
    /// <param name="weights">The weight of each row.</param>
    /// <param name="isFirst"><see langword="true"/> when the sums start from 0, <see langword="false"/> when the rows are added to what they hold.</param>
    public static void SumRows(Span<double> sums, ReadOnlySpan<float> rows, ReadOnlySpan<int> offsets, ReadOnlySpan<double> weights, bool isFirst)
    {
        var count = offsets.Length;
        var length = sums.Length;
        var i = 0;

        // Four independent sums per iteration hide the latency of the additions, whatever the width of the vectors
        if (Vector512.IsHardwareAccelerated)
        {
            for (; i + 32 <= length; i += 32)
            {
                var sum = sums.Slice(i, 32);
                var sum0 = isFirst ? Vector512<double>.Zero : Vector512.Create(sum[..8]);
                var sum1 = isFirst ? Vector512<double>.Zero : Vector512.Create(sum.Slice(8, 8));
                var sum2 = isFirst ? Vector512<double>.Zero : Vector512.Create(sum.Slice(16, 8));
                var sum3 = isFirst ? Vector512<double>.Zero : Vector512.Create(sum[24..]);
                for (var k = 0; k < count; k++)
                {
                    var factor = Vector512.Create(weights[k]);
                    var values = rows.Slice(offsets[k] + i, 32);
                    var low = Vector512.Create(values[..16]);
                    var high = Vector512.Create(values[16..]);
                    sum0 += factor * Vector512.WidenLower(low);
                    sum1 += factor * Vector512.WidenUpper(low);
                    sum2 += factor * Vector512.WidenLower(high);
                    sum3 += factor * Vector512.WidenUpper(high);
                }

                sum0.CopyTo(sum[..8]);
                sum1.CopyTo(sum.Slice(8, 8));
                sum2.CopyTo(sum.Slice(16, 8));
                sum3.CopyTo(sum[24..]);
            }
        }

        if (Vector256.IsHardwareAccelerated)
        {
            for (; i + 16 <= length; i += 16)
            {
                var sum = sums.Slice(i, 16);
                var sum0 = isFirst ? Vector256<double>.Zero : Vector256.Create(sum[..4]);
                var sum1 = isFirst ? Vector256<double>.Zero : Vector256.Create(sum.Slice(4, 4));
                var sum2 = isFirst ? Vector256<double>.Zero : Vector256.Create(sum.Slice(8, 4));
                var sum3 = isFirst ? Vector256<double>.Zero : Vector256.Create(sum[12..]);
                for (var k = 0; k < count; k++)
                {
                    var factor = Vector256.Create(weights[k]);
                    var values = rows.Slice(offsets[k] + i, 16);
                    var low = Vector256.Create(values[..8]);
                    var high = Vector256.Create(values[8..]);
                    sum0 += factor * Vector256.WidenLower(low);
                    sum1 += factor * Vector256.WidenUpper(low);
                    sum2 += factor * Vector256.WidenLower(high);
                    sum3 += factor * Vector256.WidenUpper(high);
                }

                sum0.CopyTo(sum[..4]);
                sum1.CopyTo(sum.Slice(4, 4));
                sum2.CopyTo(sum.Slice(8, 4));
                sum3.CopyTo(sum[12..]);
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            for (; i + 8 <= length; i += 8)
            {
                var sum = sums.Slice(i, 8);
                var sum0 = isFirst ? Vector128<double>.Zero : Vector128.Create(sum[..2]);
                var sum1 = isFirst ? Vector128<double>.Zero : Vector128.Create(sum.Slice(2, 2));
                var sum2 = isFirst ? Vector128<double>.Zero : Vector128.Create(sum.Slice(4, 2));
                var sum3 = isFirst ? Vector128<double>.Zero : Vector128.Create(sum[6..]);
                for (var k = 0; k < count; k++)
                {
                    var factor = Vector128.Create(weights[k]);
                    var values = rows.Slice(offsets[k] + i, 8);
                    var low = Vector128.Create(values[..4]);
                    var high = Vector128.Create(values[4..]);
                    sum0 += factor * Vector128.WidenLower(low);
                    sum1 += factor * Vector128.WidenUpper(low);
                    sum2 += factor * Vector128.WidenLower(high);
                    sum3 += factor * Vector128.WidenUpper(high);
                }

                sum0.CopyTo(sum[..2]);
                sum1.CopyTo(sum.Slice(2, 2));
                sum2.CopyTo(sum.Slice(4, 2));
                sum3.CopyTo(sum[6..]);
            }
        }

        SumRowsScalar(sums, rows, offsets, weights, isFirst, i);
    }

    /// <summary>The scalar reference of <see cref="SumRows"/>.</summary>
    public static void SumRowsScalar(Span<double> sums, ReadOnlySpan<float> rows, ReadOnlySpan<int> offsets, ReadOnlySpan<double> weights, bool isFirst)
        => SumRowsScalar(sums, rows, offsets, weights, isFirst, 0);

    private static void SumRowsScalar(Span<double> sums, ReadOnlySpan<float> rows, ReadOnlySpan<int> offsets, ReadOnlySpan<double> weights, bool isFirst, int start)
    {
        for (var i = start; i < sums.Length; i++)
        {
            var sum = isFirst ? 0 : sums[i];
            for (var k = 0; k < offsets.Length; k++)
            {
                sum += weights[k] * rows[offsets[k] + i];
            }

            sums[i] = sum;
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
        if (Vector512.IsHardwareAccelerated)
        {
            // Two pixels per vector, eight per iteration
            for (; x + 8 <= end; x += 8)
            {
                var sum0 = Vector512<double>.Zero;
                var sum1 = Vector512<double>.Zero;
                var sum2 = Vector512<double>.Zero;
                var sum3 = Vector512<double>.Zero;
                var entries = premultiplied[((firstColumn + x) * 4)..];
                for (var tap = 0; tap < taps; tap++)
                {
                    var weight = Vector512.Create(weights[tap]);
                    var entry = entries.Slice(tap * 4, 32);
                    sum0 += weight * Vector512.Create(entry[..8]);
                    sum1 += weight * Vector512.Create(entry.Slice(8, 8));
                    sum2 += weight * Vector512.Create(entry.Slice(16, 8));
                    sum3 += weight * Vector512.Create(entry[24..]);
                }

                Vector512.Narrow(sum0, sum1).CopyTo(blurred.Slice(x * 4, 16));
                Vector512.Narrow(sum2, sum3).CopyTo(blurred.Slice((x + 4) * 4, 16));
            }
        }

        if (Vector256.IsHardwareAccelerated)
        {
            // A pixel per vector, four per iteration
            for (; x + 4 <= end; x += 4)
            {
                var sum0 = Vector256<double>.Zero;
                var sum1 = Vector256<double>.Zero;
                var sum2 = Vector256<double>.Zero;
                var sum3 = Vector256<double>.Zero;
                var entries = premultiplied[((firstColumn + x) * 4)..];
                for (var tap = 0; tap < taps; tap++)
                {
                    var weight = Vector256.Create(weights[tap]);
                    var entry = entries.Slice(tap * 4, 16);
                    sum0 += weight * Vector256.Create(entry[..4]);
                    sum1 += weight * Vector256.Create(entry.Slice(4, 4));
                    sum2 += weight * Vector256.Create(entry.Slice(8, 4));
                    sum3 += weight * Vector256.Create(entry[12..]);
                }

                Vector256.Narrow(sum0, sum1).CopyTo(blurred.Slice(x * 4, 8));
                Vector256.Narrow(sum2, sum3).CopyTo(blurred.Slice((x + 2) * 4, 8));
            }
        }

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

        // The values whose taps all read values of the row
        var start = Math.Clamp(-firstColumn, 0, count);
        var end = Math.Clamp(last - taps + 2 - firstColumn, start, count);
        BlurValuesScalar(values, weights, firstColumn, blurred, 0, start);
        var x = start;
        if (Vector512.IsHardwareAccelerated)
        {
            for (; x + 16 <= end; x += 16)
            {
                var sum0 = Vector512<double>.Zero;
                var sum1 = Vector512<double>.Zero;
                var entries = values[(firstColumn + x)..];
                for (var tap = 0; tap < taps; tap++)
                {
                    var weight = Vector512.Create(weights[tap]);
                    var entry = entries.Slice(tap, 16);
                    sum0 += weight * Vector512.Create(entry[..8]);
                    sum1 += weight * Vector512.Create(entry[8..]);
                }

                Vector512.Narrow(sum0, sum1).CopyTo(blurred.Slice(x, 16));
            }
        }

        if (Vector256.IsHardwareAccelerated)
        {
            for (; x + 8 <= end; x += 8)
            {
                var sum0 = Vector256<double>.Zero;
                var sum1 = Vector256<double>.Zero;
                var entries = values[(firstColumn + x)..];
                for (var tap = 0; tap < taps; tap++)
                {
                    var weight = Vector256.Create(weights[tap]);
                    var entry = entries.Slice(tap, 8);
                    sum0 += weight * Vector256.Create(entry[..4]);
                    sum1 += weight * Vector256.Create(entry[4..]);
                }

                Vector256.Narrow(sum0, sum1).CopyTo(blurred.Slice(x, 8));
            }
        }

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
