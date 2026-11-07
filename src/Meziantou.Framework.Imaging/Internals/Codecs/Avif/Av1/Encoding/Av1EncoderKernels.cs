using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The sample kernels of the AV1 encoder search: differences between the samples of the source and of a prediction or a
/// reconstruction. The samples have at most 12 bits. The sums are exact: the vector paths only change the order of the
/// additions of integers.
/// </summary>
internal static class Av1EncoderKernels
{
    // The number of vectors of samples added into 32-bit lanes before the lanes are added into 64-bit sums: a lane receives
    // two squared differences (below 2^24) per vector
    private const int SquaredChunkVectors = 64;

    // A lane receives two absolute differences (below 2^12) per vector
    private const int AbsoluteChunkVectors = 1 << 16;

    /// <summary>Returns the sum of the absolute differences of two rows of samples.</summary>
    public static long SumOfAbsoluteDifferences(ReadOnlySpan<ushort> left, ReadOnlySpan<ushort> right)
    {
        right = right[..left.Length];
        long sum = 0;
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            while (i <= left.Length - Vector128<ushort>.Count)
            {
                var end = Math.Min(left.Length - Vector128<ushort>.Count, i + ((AbsoluteChunkVectors - 1) * Vector128<ushort>.Count));
                var lanes = Vector128<uint>.Zero;
                for (; i <= end; i += Vector128<ushort>.Count)
                {
                    var a = Vector128.Create(left[i..]);
                    var b = Vector128.Create(right[i..]);
                    var (low, high) = Vector128.Widen(Vector128.Max(a, b) - Vector128.Min(a, b));
                    lanes += low + high;
                }

                sum += (long)Vector128.Sum(Vector128.WidenLower(lanes) + Vector128.WidenUpper(lanes));
            }
        }

        return sum + SumOfAbsoluteDifferencesScalar(left[i..], right[i..]);
    }

    /// <summary>The scalar reference of <see cref="SumOfAbsoluteDifferences(ReadOnlySpan{ushort}, ReadOnlySpan{ushort})"/>.</summary>
    public static long SumOfAbsoluteDifferencesScalar(ReadOnlySpan<ushort> left, ReadOnlySpan<ushort> right)
    {
        right = right[..left.Length];
        long sum = 0;
        for (var j = 0; j < left.Length; j++)
        {
            sum += Math.Abs(left[j] - right[j]);
        }

        return sum;
    }

    /// <summary>Returns the sum of the squared differences of two rows of samples.</summary>
    public static long SumOfSquaredDifferences(ReadOnlySpan<ushort> left, ReadOnlySpan<ushort> right)
    {
        right = right[..left.Length];
        long sum = 0;
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            while (i <= left.Length - Vector128<ushort>.Count)
            {
                var end = Math.Min(left.Length - Vector128<ushort>.Count, i + ((SquaredChunkVectors - 1) * Vector128<ushort>.Count));
                var lanes = Vector128<uint>.Zero;
                for (; i <= end; i += Vector128<ushort>.Count)
                {
                    var a = Vector128.Create(left[i..]);
                    var b = Vector128.Create(right[i..]);
                    var (low, high) = Vector128.Widen(Vector128.Max(a, b) - Vector128.Min(a, b));
                    lanes += (low * low) + (high * high);
                }

                sum += (long)Vector128.Sum(Vector128.WidenLower(lanes) + Vector128.WidenUpper(lanes));
            }
        }

        return sum + SumOfSquaredDifferencesScalar(left[i..], right[i..]);
    }

    /// <summary>The scalar reference of <see cref="SumOfSquaredDifferences(ReadOnlySpan{ushort}, ReadOnlySpan{ushort})"/>.</summary>
    public static long SumOfSquaredDifferencesScalar(ReadOnlySpan<ushort> left, ReadOnlySpan<ushort> right)
    {
        right = right[..left.Length];
        long sum = 0;
        for (var j = 0; j < left.Length; j++)
        {
            var difference = left[j] - right[j];
            sum += difference * difference;
        }

        return sum;
    }

    /// <summary>
    /// Returns the sum of the absolute differences between source samples and their chroma from luma prediction with an
    /// alpha (section 7.11.5): <paramref name="samples"/> packs the source sample (low 16 bits) and the DC prediction (high
    /// 16 bits), <paramref name="luma"/> holds the luma samples minus their average.
    /// </summary>
    public static long GetChromaFromLumaError(ReadOnlySpan<int> samples, ReadOnlySpan<int> luma, int alpha, int maxSample)
    {
        luma = luma[..samples.Length];
        long error = 0;
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            // A lane adds at most 2^12 per group of 4 samples: the lanes are added to the sum every 2^16 groups
            var max = Vector128.Create(maxSample);
            var rounding = Vector128.Create(32);
            var low = Vector128.Create(0xFFFF);
            while (i <= samples.Length - Vector128<int>.Count)
            {
                var end = Math.Min(samples.Length - Vector128<int>.Count, i + ((AbsoluteChunkVectors - 1) * Vector128<int>.Count));
                var lanes = Vector128<int>.Zero;
                for (; i <= end; i += Vector128<int>.Count)
                {
                    var packed = Vector128.Create(samples[i..]);
                    var product = Vector128.Create(luma[i..]) * alpha;
                    var sign = product >> 31;
                    var scaledLuma = (((Vector128.Abs(product) + rounding) >> 6) ^ sign) - sign;
                    var predicted = Vector128.Min(Vector128.Max((packed >>> 16) + scaledLuma, Vector128<int>.Zero), max);
                    lanes += Vector128.Abs((packed & low) - predicted);
                }

                error += (long)Vector128.Sum(Vector128.WidenLower(lanes.AsUInt32()) + Vector128.WidenUpper(lanes.AsUInt32()));
            }
        }

        return error + GetChromaFromLumaErrorScalar(samples[i..], luma[i..], alpha, maxSample);
    }

    /// <summary>The scalar reference of <see cref="GetChromaFromLumaError"/>.</summary>
    public static long GetChromaFromLumaErrorScalar(ReadOnlySpan<int> samples, ReadOnlySpan<int> luma, int alpha, int maxSample)
    {
        long error = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var original = samples[i] & 0xFFFF;
            var predicted = samples[i] >>> 16;
            var product = alpha * luma[i];
            var scaledLuma = product >= 0 ? (product + 32) >> 6 : -((-product + 32) >> 6);
            error += Math.Abs(original - Math.Clamp(predicted + scaledLuma, 0, maxSample));
        }

        return error;
    }

    /// <summary>
    /// Returns the sum of the absolute differences between the samples of a block of <paramref name="left"/> and the ones of
    /// the same block of <paramref name="right"/>.
    /// </summary>
    /// <remarks>The rows of a block of width 4 are taken two at a time, in the lanes of one vector.</remarks>
    public static long SumOfAbsoluteDifferences(ReadOnlySpan<ushort> left, int leftStride, Span<ushort> right, int rightStride, int width, int height)
        => SumOfAbsoluteDifferences(left, leftStride, right, rightStride, width, height, copy: false);

    /// <summary>
    /// Returns the sum of the absolute differences between the samples of a block of <paramref name="source"/> and the ones of
    /// the same block of <paramref name="predicted"/>, then copies the samples of the source over the predicted ones.
    /// </summary>
    public static long SumOfAbsoluteDifferencesAndCopy(ReadOnlySpan<ushort> source, int sourceStride, Span<ushort> predicted, int predictedStride, int width, int height)
        => SumOfAbsoluteDifferences(source, sourceStride, predicted, predictedStride, width, height, copy: true);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long SumOfAbsoluteDifferences(ReadOnlySpan<ushort> source, int sourceStride, Span<ushort> predicted, int predictedStride, int width, int height, bool copy)
    {
        long sum = 0;
        var i = 0;
        if (width is 4 && Vector128.IsHardwareAccelerated)
        {
            // A lane receives two absolute differences (below 2^12) per pair of rows: 64 rows at most
            var lanes = Vector128<uint>.Zero;
            for (; i + 1 < height; i += 2)
            {
                var first = predicted.Slice(i * predictedStride, 4);
                var second = predicted.Slice((i + 1) * predictedStride, 4);
                var originalFirst = Vector64.Create(source.Slice(i * sourceStride, 4));
                var originalSecond = Vector64.Create(source.Slice((i + 1) * sourceStride, 4));
                var a = Vector128.Create(originalFirst, originalSecond);
                var b = Vector128.Create(Vector64.Create((ReadOnlySpan<ushort>)first), Vector64.Create((ReadOnlySpan<ushort>)second));
                var (low, high) = Vector128.Widen(Vector128.Max(a, b) - Vector128.Min(a, b));
                lanes += low + high;
                if (copy)
                {
                    originalFirst.CopyTo(first);
                    originalSecond.CopyTo(second);
                }
            }

            sum = Vector128.Sum(lanes);
        }
        else if (width % Vector128<ushort>.Count is 0 && Vector128.IsHardwareAccelerated)
        {
            // The lanes are added to the sum every AbsoluteChunkVectors vectors
            var rowVectors = width / Vector128<ushort>.Count;
            while (i < height)
            {
                var lanes = Vector128<uint>.Zero;
                var end = Math.Min(height, i + Math.Max(1, AbsoluteChunkVectors / rowVectors));
                for (; i < end; i++)
                {
                    var original = source.Slice(i * sourceStride, width);
                    var row = predicted.Slice(i * predictedStride, width);
                    for (var j = 0; j < width; j += Vector128<ushort>.Count)
                    {
                        var a = Vector128.Create(original[j..]);
                        var b = Vector128.Create((ReadOnlySpan<ushort>)row[j..]);
                        var (low, high) = Vector128.Widen(Vector128.Max(a, b) - Vector128.Min(a, b));
                        lanes += low + high;
                        if (copy)
                            a.CopyTo(row[j..]);
                    }
                }

                sum += (long)Vector128.Sum(Vector128.WidenLower(lanes) + Vector128.WidenUpper(lanes));
            }
        }

        for (; i < height; i++)
        {
            var original = source.Slice(i * sourceStride, width);
            var row = predicted.Slice(i * predictedStride, width);
            sum += SumOfAbsoluteDifferences(original, row);
            if (copy)
                original.CopyTo(row);
        }

        return sum;
    }

    /// <summary>The scalar reference of <see cref="SumOfAbsoluteDifferencesAndCopy"/>.</summary>
    public static long SumOfAbsoluteDifferencesAndCopyScalar(ReadOnlySpan<ushort> source, int sourceStride, Span<ushort> predicted, int predictedStride, int width, int height)
    {
        long sum = 0;
        for (var i = 0; i < height; i++)
        {
            var original = source.Slice(i * sourceStride, width);
            var row = predicted.Slice(i * predictedStride, width);
            sum += SumOfAbsoluteDifferencesScalar(original, row);
            original.CopyTo(row);
        }

        return sum;
    }

    /// <summary>
    /// Returns the sum of the squared differences between the samples of a block of <paramref name="left"/> and the ones of
    /// the same block of <paramref name="right"/>.
    /// </summary>
    /// <remarks>The rows of a block of width 4 are taken two at a time, in the lanes of one vector.</remarks>
    public static long SumOfSquaredDifferences(ReadOnlySpan<ushort> left, int leftStride, ReadOnlySpan<ushort> right, int rightStride, int width, int height)
    {
        long sum = 0;
        var i = 0;
        if (width is 4 && Vector128.IsHardwareAccelerated)
        {
            // A lane receives two squared differences (below 2^24) per pair of rows: the lanes are added every 64 pairs
            while (i + 1 < height)
            {
                var lanes = Vector128<uint>.Zero;
                var end = Math.Min(height - 1, i + (2 * SquaredChunkVectors));
                for (; i < end; i += 2)
                {
                    var a = Vector128.Create(Vector64.Create(left.Slice(i * leftStride, 4)), Vector64.Create(left.Slice((i + 1) * leftStride, 4)));
                    var b = Vector128.Create(Vector64.Create(right.Slice(i * rightStride, 4)), Vector64.Create(right.Slice((i + 1) * rightStride, 4)));
                    var (low, high) = Vector128.Widen(Vector128.Max(a, b) - Vector128.Min(a, b));
                    lanes += (low * low) + (high * high);
                }

                sum += (long)Vector128.Sum(Vector128.WidenLower(lanes) + Vector128.WidenUpper(lanes));
            }
        }
        else if (width % Vector128<ushort>.Count is 0 && width <= SquaredChunkVectors * Vector128<ushort>.Count && Vector128.IsHardwareAccelerated)
        {
            // The lanes are added to the sum every SquaredChunkVectors vectors, or after every row of more vectors
            var rowVectors = width / Vector128<ushort>.Count;
            while (i < height)
            {
                var lanes = Vector128<uint>.Zero;
                var end = Math.Min(height, i + Math.Max(1, SquaredChunkVectors / rowVectors));
                for (; i < end; i++)
                {
                    var leftRow = left.Slice(i * leftStride, width);
                    var rightRow = right.Slice(i * rightStride, width);
                    for (var j = 0; j < width; j += Vector128<ushort>.Count)
                    {
                        var a = Vector128.Create(leftRow[j..]);
                        var b = Vector128.Create(rightRow[j..]);
                        var (low, high) = Vector128.Widen(Vector128.Max(a, b) - Vector128.Min(a, b));
                        lanes += (low * low) + (high * high);
                    }
                }

                sum += (long)Vector128.Sum(Vector128.WidenLower(lanes) + Vector128.WidenUpper(lanes));
            }
        }

        for (; i < height; i++)
        {
            sum += SumOfSquaredDifferences(left.Slice(i * leftStride, width), right.Slice(i * rightStride, width));
        }

        return sum;
    }

    /// <summary>The scalar reference of <see cref="SumOfSquaredDifferences(ReadOnlySpan{ushort}, int, ReadOnlySpan{ushort}, int, int, int)"/>.</summary>
    public static long SumOfSquaredDifferencesScalar(ReadOnlySpan<ushort> left, int leftStride, ReadOnlySpan<ushort> right, int rightStride, int width, int height)
    {
        long sum = 0;
        for (var i = 0; i < height; i++)
        {
            sum += SumOfSquaredDifferencesScalar(left.Slice(i * leftStride, width), right.Slice(i * rightStride, width));
        }

        return sum;
    }

    /// <summary>
    /// Writes the differences between the samples of a block of <paramref name="left"/> and the ones of the same block of
    /// <paramref name="right"/>, row after row.
    /// </summary>
    public static void Subtract(ReadOnlySpan<ushort> left, int leftStride, ReadOnlySpan<ushort> right, int rightStride, int width, int height, Span<int> difference)
    {
        var i = 0;
        if (width is 4 && Vector128.IsHardwareAccelerated)
        {
            for (; i + 1 < height; i += 2)
            {
                var a = Vector128.Create(Vector64.Create(left.Slice(i * leftStride, 4)), Vector64.Create(left.Slice((i + 1) * leftStride, 4)));
                var b = Vector128.Create(Vector64.Create(right.Slice(i * rightStride, 4)), Vector64.Create(right.Slice((i + 1) * rightStride, 4)));
                var (leftLow, leftHigh) = Vector128.Widen(a);
                var (rightLow, rightHigh) = Vector128.Widen(b);
                (leftLow.AsInt32() - rightLow.AsInt32()).CopyTo(difference[(i * 4)..]);
                (leftHigh.AsInt32() - rightHigh.AsInt32()).CopyTo(difference[((i + 1) * 4)..]);
            }
        }

        else if (width % Vector128<ushort>.Count is 0 && Vector128.IsHardwareAccelerated)
        {
            for (; i < height; i++)
            {
                var leftRow = left.Slice(i * leftStride, width);
                var rightRow = right.Slice(i * rightStride, width);
                var output = difference.Slice(i * width, width);
                for (var j = 0; j < width; j += Vector128<ushort>.Count)
                {
                    var (leftLow, leftHigh) = Vector128.Widen(Vector128.Create(leftRow[j..]));
                    var (rightLow, rightHigh) = Vector128.Widen(Vector128.Create(rightRow[j..]));
                    (leftLow.AsInt32() - rightLow.AsInt32()).CopyTo(output[j..]);
                    (leftHigh.AsInt32() - rightHigh.AsInt32()).CopyTo(output[(j + Vector128<int>.Count)..]);
                }
            }
        }

        for (; i < height; i++)
        {
            Subtract(left.Slice(i * leftStride, width), right.Slice(i * rightStride, width), difference.Slice(i * width, width));
        }
    }

    /// <summary>Writes the differences <paramref name="left"/> - <paramref name="right"/> of two rows of samples.</summary>
    public static void Subtract(ReadOnlySpan<ushort> left, ReadOnlySpan<ushort> right, Span<int> difference)
    {
        right = right[..left.Length];
        difference = difference[..left.Length];
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            for (; i <= left.Length - Vector128<ushort>.Count; i += Vector128<ushort>.Count)
            {
                var (leftLow, leftHigh) = Vector128.Widen(Vector128.Create(left[i..]));
                var (rightLow, rightHigh) = Vector128.Widen(Vector128.Create(right[i..]));
                (leftLow.AsInt32() - rightLow.AsInt32()).CopyTo(difference[i..]);
                (leftHigh.AsInt32() - rightHigh.AsInt32()).CopyTo(difference[(i + Vector128<int>.Count)..]);
            }
        }

        SubtractScalar(left[i..], right[i..], difference[i..]);
    }

    /// <summary>The scalar reference of <see cref="Subtract(ReadOnlySpan{ushort}, ReadOnlySpan{ushort}, Span{int})"/>.</summary>
    public static void SubtractScalar(ReadOnlySpan<ushort> left, ReadOnlySpan<ushort> right, Span<int> difference)
    {
        right = right[..left.Length];
        for (var j = 0; j < left.Length; j++)
        {
            difference[j] = left[j] - right[j];
        }
    }
}
