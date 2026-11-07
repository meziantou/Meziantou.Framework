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
        var lines = lowFrequency ? LowFrequencyLines : HighFrequencyLines;
        var offsets = new int[lines.Length][];
        for (var i = 0; i < lines.Length; i++)
        {
            offsets[i] = Array.ConvertAll(lines[i], point => point.Dy * stride + point.Dx);
        }

        RowBands.For(lum0.Size, maxDegreeOfParallelism, (start, end) => AddLines(padded.Span, blockDiffAc.Span, offsets, width, stride, start, end));
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

    private static void AddLines(ReadOnlySpan<float> diffs, Span<float> output, int[][] offsets, int width, int stride, int start, int end)
    {
        for (var y = start; y < end; y++)
        {
            var outputRow = output.Slice(y * width, width);
            var center = (y + Padding) * stride + Padding;
            for (var x = 0; x < width; x++)
            {
                var retval = 0f;
                foreach (var line in offsets)
                {
                    var sum = 0f;
                    foreach (var offset in line)
                    {
                        sum += diffs[center + x + offset];
                    }

                    retval += sum * sum;
                }

                outputRow[x] += retval;
            }
        }
    }
}
