using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The Malta filters of <see cref="ButteraugliCalculator"/> (<c>MaltaDiffMap</c> and <c>MaltaDiffMapLF</c> in
/// <c>lib/jxl/butteraugli/butteraugli.cc</c> of libjxl 0.12.0): the error of every pixel is the sum of the squared sums of
/// the weighted differences along 16 short lines through it, which detects line-shaped artifacts.
/// </summary>
internal static class ButteraugliMalta
{
    /// <summary>The margin of zeros around the plane of differences, so the lines never leave the buffer.</summary>
    public const int Padding = 4;

    // Each line is a list of (dx, dy) points, in the order libjxl adds them
    private static readonly (int Dx, int Dy)[][] HighFrequencyLines =
    [
        [(-4, 0), (-3, 0), (-2, 0), (-1, 0), (0, 0), (1, 0), (2, 0), (3, 0), (4, 0)],
        [(0, -4), (0, -3), (0, -2), (0, -1), (0, 0), (0, 1), (0, 2), (0, 3), (0, 4)],
        [(-3, -3), (-2, -2), (-1, -1), (0, 0), (1, 1), (2, 2), (3, 3)],
        [(3, -3), (2, -2), (1, -1), (0, 0), (-1, 1), (-2, 2), (-3, 3)],
        [(1, -4), (1, -3), (1, -2), (0, -1), (0, 0), (0, 1), (-1, 2), (-1, 3), (-1, 4)],
        [(-1, -4), (-1, -3), (-1, -2), (0, -1), (0, 0), (0, 1), (1, 2), (1, 3), (1, 4)],
        [(-4, -1), (-3, -1), (-2, -1), (-1, 0), (0, 0), (1, 0), (2, 1), (3, 1), (4, 1)],
        [(-4, 1), (-3, 1), (-2, 1), (-1, 0), (0, 0), (1, 0), (2, -1), (3, -1), (4, -1)],
        [(-2, -3), (-1, -2), (-1, -1), (0, 0), (1, 1), (1, 2), (2, 3)],
        [(2, -3), (1, -2), (1, -1), (0, 0), (-1, 1), (-1, 2), (-2, 3)],
        [(-3, -2), (-2, -1), (-1, -1), (0, 0), (1, 1), (2, 1), (3, 2)],
        [(3, -2), (2, -1), (1, -1), (0, 0), (-1, 1), (-2, 1), (-3, 2)],

        // libjxl repeats four of the lines
        [(-4, 1), (-3, 1), (-2, 1), (-1, 0), (0, 0), (1, 0), (2, -1), (3, -1), (4, -1)],
        [(-4, -1), (-3, -1), (-2, -1), (-1, 0), (0, 0), (1, 0), (2, 1), (3, 1), (4, 1)],
        [(-1, -4), (-1, -3), (-1, -2), (0, -1), (0, 0), (0, 1), (1, 2), (1, 3), (1, 4)],
        [(1, -4), (1, -3), (1, -2), (0, -1), (0, 0), (0, 1), (-1, 2), (-1, 3), (-1, 4)],
    ];

    private static readonly (int Dx, int Dy)[][] LowFrequencyLines =
    [
        [(-4, 0), (-2, 0), (0, 0), (2, 0), (4, 0)],
        [(0, -4), (0, -2), (0, 0), (0, 2), (0, 4)],
        [(-3, -3), (-2, -2), (0, 0), (2, 2), (3, 3)],
        [(3, -3), (2, -2), (0, 0), (-2, 2), (-3, 3)],
        [(1, -4), (1, -2), (0, 0), (-1, 2), (-1, 4)],
        [(-1, -4), (-1, -2), (0, 0), (1, 2), (1, 4)],
        [(-4, -1), (-2, -1), (0, 0), (2, 1), (4, 1)],
        [(-4, 1), (-2, 1), (0, 0), (2, -1), (4, -1)],
        [(-2, -3), (-1, -2), (0, 0), (1, 2), (2, 3)],
        [(2, -3), (1, -2), (0, 0), (-1, 2), (-2, 3)],
        [(-3, -2), (-2, -1), (0, 0), (2, 1), (3, 2)],
        [(3, -2), (2, -1), (0, 0), (-2, 1), (-3, 2)],
        [(-4, 2), (-2, 1), (0, 0), (2, -1), (4, -2)],
        [(-4, -2), (-2, -1), (0, 0), (2, 1), (4, 2)],
        [(-2, -4), (-1, -2), (0, 0), (1, 2), (2, 4)],
        [(2, -4), (1, -2), (0, 0), (-1, 2), (-2, 4)],
    ];

    /// <summary>
    /// Adds the Malta error of <c>lum1</c> against <c>lum0</c> to <c>blockDiffAc</c>. <c>padded</c> is a buffer of
    /// <c>(width + 8) × (height + 8)</c> floats whose margin of <see cref="Padding"/> samples is zero.
    /// </summary>
    public static void AddDiffMap(bool lowFrequency, FloatPlane lum0, FloatPlane lum1, double w0Gt1, double w0Lt1, double norm1, FloatPlane padded, FloatPlane blockDiffAc, int maxDegreeOfParallelism)
    {
        const double Length = 3.75;
        const float Weight0 = 0.5f;
        const float Weight1 = 0.33f;
        var mulli = lowFrequency ? 0.611612573796 : 0.39905817637;

        var wPre0Gt1 = mulli * Math.Sqrt(Weight0 * w0Gt1) / (Length * 2 + 1);
        var wPre0Lt1 = mulli * Math.Sqrt(Weight1 * w0Lt1) / (Length * 2 + 1);
        var norm2Gt1 = (float)(wPre0Gt1 * norm1);
        var norm2Lt1 = (float)(wPre0Lt1 * norm1);
        var norm1Float = (float)norm1;

        var width = lum0.Width;
        var stride = width + 2 * Padding;

        // The differences of every row, then the lines through every pixel, which read the rows around it
        RowBands.For(lum0.Size, maxDegreeOfParallelism, (start, end) => ComputeDifferences(lum0.Span, lum1.Span, padded.Span, width, stride, norm1Float, norm2Gt1, norm2Lt1, start, end));
        var offsets = GetLineOffsets(lowFrequency, stride, out var lineEnds);
        RowBands.For(lum0.Size, maxDegreeOfParallelism, (start, end) =>
        {
            for (var y = start; y < end; y++)
            {
                AddLines(padded.Span, blockDiffAc.Span.Slice(y * width, width), offsets, lineEnds, (y + Padding) * stride + Padding);
            }
        });
    }

    /// <summary>
    /// Gets the offsets of the points of every line in a padded plane of <paramref name="stride"/> floats per row, line after
    /// line: line <c>i</c> ends at <c>lineEnds[i]</c>.
    /// </summary>
    internal static int[] GetLineOffsets(bool lowFrequency, int stride, out int[] lineEnds)
    {
        var lines = lowFrequency ? LowFrequencyLines : HighFrequencyLines;
        var offsets = new List<int>();
        lineEnds = new int[lines.Length];
        for (var i = 0; i < lines.Length; i++)
        {
            foreach (var (dx, dy) in lines[i])
            {
                offsets.Add(dy * stride + dx);
            }

            lineEnds[i] = offsets.Count;
        }

        return [.. offsets];
    }

    private static void ComputeDifferences(ReadOnlySpan<float> plane0, ReadOnlySpan<float> plane1, Span<float> diffs, int width, int stride, float norm1Float, float norm2Gt1, float norm2Lt1, int start, int end)
    {
        for (var y = start; y < end; y++)
        {
            var row0 = plane0.Slice(y * width, width);
            var row1 = plane1.Slice(y * width, width);
            var rowDiffs = diffs.Slice((y + Padding) * stride + Padding, width);
            for (var x = 0; x < width; x++)
            {
                var value0 = row0[x];
                var value1 = row1[x];
                var absval = 0.5f * (Math.Abs(value0) + Math.Abs(value1));
                var diff = value0 - value1;
                var scaler = norm2Gt1 / (norm1Float + absval);

                // Primary symmetric quadratic objective
                var result = scaler * diff;

                var scaler2 = norm2Lt1 / (norm1Float + absval);
                double fabs0 = Math.Abs(value0);

                // Secondary half-open quadratic objectives
                var tooSmall = 0.55 * fabs0;
                var tooBig = 1.05 * fabs0;
                if (value0 < 0)
                {
                    if (value1 > -tooSmall)
                    {
                        result = (float)(result - scaler2 * (value1 + tooSmall));
                    }
                    else if (value1 < -tooBig)
                    {
                        result = (float)(result + scaler2 * (-value1 - tooBig));
                    }
                }
                else if (value1 < tooSmall)
                {
                    result = (float)(result + scaler2 * (tooSmall - value1));
                }
                else if (value1 > tooBig)
                {
                    result = (float)(result - scaler2 * (value1 - tooBig));
                }

                rowDiffs[x] = result;
            }
        }
    }

    /// <summary>
    /// Adds the Malta error of the pixels of a row to <paramref name="output"/>: <paramref name="center"/> is the index of the
    /// first pixel of the row in <paramref name="diffs"/>. The vectors compute the pixels of consecutive columns with the
    /// additions of <see cref="AddLinesScalar"/>, in the same order.
    /// </summary>
    internal static void AddLines(ReadOnlySpan<float> diffs, Span<float> output, ReadOnlySpan<int> offsets, ReadOnlySpan<int> lineEnds, int center)
    {
        var width = output.Length;
        var x = 0;
        if (Vector128.IsHardwareAccelerated && width >= Vector128<float>.Count && lineEnds.Length > 0)
        {
            // Every load reads inside the plane: the vectors cover the columns [0, width) only
            var minOffset = int.MaxValue;
            var maxOffset = int.MinValue;
            var tapCount = lineEnds[^1];
            foreach (var offset in offsets[..tapCount])
            {
                minOffset = Math.Min(minOffset, offset);
                maxOffset = Math.Max(maxOffset, offset);
            }

            if (center + minOffset < 0 || (long)center + width - 1 + maxOffset >= diffs.Length)
                throw new ArgumentOutOfRangeException(nameof(center));

            ref var source = ref unsafe(Unsafe.Add(ref MemoryMarshal.GetReference(diffs), center));
            ref var target = ref unsafe(MemoryMarshal.GetReference(output));
            ref var offset0 = ref unsafe(MemoryMarshal.GetReference(offsets));
            if (Vector512.IsHardwareAccelerated)
            {
                // Two vectors at a time share the loads of the offsets
                for (; x + (2 * Vector512<float>.Count) <= width; x += 2 * Vector512<float>.Count)
                {
                    ref var column = ref unsafe(Unsafe.Add(ref source, x));
                    var retval0 = Vector512<float>.Zero;
                    var retval1 = Vector512<float>.Zero;
                    var t = 0;
                    foreach (var lineEnd in lineEnds)
                    {
                        var sum0 = Vector512<float>.Zero;
                        var sum1 = Vector512<float>.Zero;
                        for (; t < lineEnd; t++)
                        {
                            ref var tap = ref unsafe(Unsafe.Add(ref column, Unsafe.Add(ref offset0, t)));
                            sum0 += unsafe(Vector512.LoadUnsafe(ref tap));
                            sum1 += unsafe(Vector512.LoadUnsafe(ref tap, (nuint)Vector512<float>.Count));
                        }

                        retval0 += sum0 * sum0;
                        retval1 += sum1 * sum1;
                    }

                    unsafe
                    {
                        (Vector512.LoadUnsafe(ref target, (nuint)x) + retval0).StoreUnsafe(ref target, (nuint)x);
                        (Vector512.LoadUnsafe(ref target, (nuint)(x + Vector512<float>.Count)) + retval1).StoreUnsafe(ref target, (nuint)(x + Vector512<float>.Count));
                    }
                }

                for (; x + Vector512<float>.Count <= width; x += Vector512<float>.Count)
                {
                    ref var column = ref unsafe(Unsafe.Add(ref source, x));
                    var retval = Vector512<float>.Zero;
                    var t = 0;
                    foreach (var lineEnd in lineEnds)
                    {
                        var sum = Vector512<float>.Zero;
                        for (; t < lineEnd; t++)
                        {
                            sum += unsafe(Vector512.LoadUnsafe(ref Unsafe.Add(ref column, Unsafe.Add(ref offset0, t))));
                        }

                        retval += sum * sum;
                    }

                    unsafe { (Vector512.LoadUnsafe(ref target, (nuint)x) + retval).StoreUnsafe(ref target, (nuint)x); }
                }
            }

            if (Vector256.IsHardwareAccelerated)
            {
                // Two vectors at a time share the loads of the offsets
                for (; x + (2 * Vector256<float>.Count) <= width; x += 2 * Vector256<float>.Count)
                {
                    ref var column = ref unsafe(Unsafe.Add(ref source, x));
                    var retval0 = Vector256<float>.Zero;
                    var retval1 = Vector256<float>.Zero;
                    var t = 0;
                    foreach (var lineEnd in lineEnds)
                    {
                        var sum0 = Vector256<float>.Zero;
                        var sum1 = Vector256<float>.Zero;
                        for (; t < lineEnd; t++)
                        {
                            ref var tap = ref unsafe(Unsafe.Add(ref column, Unsafe.Add(ref offset0, t)));
                            sum0 += unsafe(Vector256.LoadUnsafe(ref tap));
                            sum1 += unsafe(Vector256.LoadUnsafe(ref tap, (nuint)Vector256<float>.Count));
                        }

                        retval0 += sum0 * sum0;
                        retval1 += sum1 * sum1;
                    }

                    unsafe
                    {
                        (Vector256.LoadUnsafe(ref target, (nuint)x) + retval0).StoreUnsafe(ref target, (nuint)x);
                        (Vector256.LoadUnsafe(ref target, (nuint)(x + Vector256<float>.Count)) + retval1).StoreUnsafe(ref target, (nuint)(x + Vector256<float>.Count));
                    }
                }

                for (; x + Vector256<float>.Count <= width; x += Vector256<float>.Count)
                {
                    ref var column = ref unsafe(Unsafe.Add(ref source, x));
                    var retval = Vector256<float>.Zero;
                    var t = 0;
                    foreach (var lineEnd in lineEnds)
                    {
                        var sum = Vector256<float>.Zero;
                        for (; t < lineEnd; t++)
                        {
                            sum += unsafe(Vector256.LoadUnsafe(ref Unsafe.Add(ref column, Unsafe.Add(ref offset0, t))));
                        }

                        retval += sum * sum;
                    }

                    unsafe { (Vector256.LoadUnsafe(ref target, (nuint)x) + retval).StoreUnsafe(ref target, (nuint)x); }
                }
            }

            // Two vectors at a time share the loads of the offsets
            for (; x + (2 * Vector128<float>.Count) <= width; x += 2 * Vector128<float>.Count)
            {
                ref var column = ref unsafe(Unsafe.Add(ref source, x));
                var retval0 = Vector128<float>.Zero;
                var retval1 = Vector128<float>.Zero;
                var t = 0;
                foreach (var lineEnd in lineEnds)
                {
                    var sum0 = Vector128<float>.Zero;
                    var sum1 = Vector128<float>.Zero;
                    for (; t < lineEnd; t++)
                    {
                        ref var tap = ref unsafe(Unsafe.Add(ref column, Unsafe.Add(ref offset0, t)));
                        sum0 += unsafe(Vector128.LoadUnsafe(ref tap));
                        sum1 += unsafe(Vector128.LoadUnsafe(ref tap, (nuint)Vector128<float>.Count));
                    }

                    retval0 += sum0 * sum0;
                    retval1 += sum1 * sum1;
                }

                unsafe
                {
                    (Vector128.LoadUnsafe(ref target, (nuint)x) + retval0).StoreUnsafe(ref target, (nuint)x);
                    (Vector128.LoadUnsafe(ref target, (nuint)(x + Vector128<float>.Count)) + retval1).StoreUnsafe(ref target, (nuint)(x + Vector128<float>.Count));
                }
            }

            for (; x + Vector128<float>.Count <= width; x += Vector128<float>.Count)
            {
                ref var column = ref unsafe(Unsafe.Add(ref source, x));
                var retval = Vector128<float>.Zero;
                var t = 0;
                foreach (var lineEnd in lineEnds)
                {
                    var sum = Vector128<float>.Zero;
                    for (; t < lineEnd; t++)
                    {
                        sum += unsafe(Vector128.LoadUnsafe(ref Unsafe.Add(ref column, Unsafe.Add(ref offset0, t))));
                    }

                    retval += sum * sum;
                }

                unsafe { (Vector128.LoadUnsafe(ref target, (nuint)x) + retval).StoreUnsafe(ref target, (nuint)x); }
            }
        }

        AddLinesScalar(diffs, output, offsets, lineEnds, center, x);
    }

    /// <summary>The scalar reference of <see cref="AddLines"/>, from column <paramref name="start"/>.</summary>
    internal static void AddLinesScalar(ReadOnlySpan<float> diffs, Span<float> output, ReadOnlySpan<int> offsets, ReadOnlySpan<int> lineEnds, int center, int start = 0)
    {
        for (var x = start; x < output.Length; x++)
        {
            var retval = 0f;
            var t = 0;
            foreach (var lineEnd in lineEnds)
            {
                var sum = 0f;
                for (; t < lineEnd; t++)
                {
                    sum += diffs[center + x + offsets[t]];
                }

                retval += sum * sum;
            }

            output[x] += retval;
        }
    }
}
