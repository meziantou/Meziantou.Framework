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

    /// <summary>The scalar reference of <see cref="SumOfAbsoluteDifferences"/>.</summary>
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

    /// <summary>The scalar reference of <see cref="SumOfSquaredDifferences"/>.</summary>
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

    /// <summary>The scalar reference of <see cref="Subtract"/>.</summary>
    public static void SubtractScalar(ReadOnlySpan<ushort> left, ReadOnlySpan<ushort> right, Span<int> difference)
    {
        right = right[..left.Length];
        for (var j = 0; j < left.Length; j++)
        {
            difference[j] = left[j] - right[j];
        }
    }
}
