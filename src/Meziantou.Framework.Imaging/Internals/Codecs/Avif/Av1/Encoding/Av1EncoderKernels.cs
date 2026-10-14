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
