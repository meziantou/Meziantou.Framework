using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Estimates the skew angle of a document by projection profiles (Postl, "Detection of Linear Oblique Structures and Skew
/// Scan in Digitized Documents", 1986; Baird, "The Skew Angle of Printed Documents", 1987): the dark pixels are projected
/// along the candidate angles, and the angle whose projection has the sharpest profile (the largest sum of the squares of
/// its counts) is the angle of the lines of text.
/// </summary>
/// <remarks>
/// <para>
/// The frame is read as its Rec. 709 luma composited over white (a transparent pixel is paper), at 256 levels. The ink is
/// the smaller of the two classes of the threshold of Otsu ("A Threshold Selection Method from Gray-Level Histograms",
/// 1979), the smallest level that maximizes the variance between the classes, so that dark text on light paper and light
/// text on dark paper both work.
/// </para>
/// <para>
/// A pixel at <c>(x, y)</c>, its center relative to the center of the frame, falls in the bin <c>floor(y cos α - x sin α)</c>
/// of the angle α, whose sine and cosine come from <c>DeterministicAngle</c>: the lines <c>y = x tan α + c</c> fall in one
/// bin each. The angles are the multiples of 0.5 degree up to the maximum angle, then the multiples of the precision within
/// 0.5 degree of the best one. The scores are integers; among equal scores, the angle nearest to 0 wins, then the negative
/// one, so the result does not depend on the platform. The ink positions (8 bytes per ink pixel) and the profile are
/// charged to the image.
/// </para>
/// </remarks>
internal static class SkewEstimator
{
    private const double CoarseStep = 0.5;

    public static double Estimate(AllocationScope scope, PixelFormat format, scoped in PixelLease lease, SkewEstimationOptions options, CancellationToken cancellationToken)
    {
        var width = lease.Width;
        var height = lease.Height;
        var count = (long)width * height;
        using var levelBuffer = ResampleWeights.Rent<byte>(scope, count);
        var levels = levelBuffer.Span[..(int)count];
        ReadPaperLevels(scope, format, lease, levels, cancellationToken);

        Span<long> histogram = stackalloc long[256];
        histogram.Clear();
        foreach (var level in levels)
        {
            histogram[level]++;
        }

        if (!TryGetOtsuThreshold(histogram, count, out var threshold))
            return 0;

        var dark = 0L;
        for (var i = 0; i <= threshold; i++)
        {
            dark += histogram[i];
        }

        var inkIsDark = dark <= count - dark;
        var inkCount = (int)(inkIsDark ? dark : count - dark);
        if (inkCount == 0)
            return 0;

        // The ink, as positions relative to the center doubled (so that they are integers)
        using var inkBuffer = ResampleWeights.Rent<int>(scope, 2L * inkCount);
        var ink = unsafe(MemoryMarshal.Cast<byte, int>(inkBuffer.Span))[..(2 * inkCount)];
        var n = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var level = levels[(y * width) + x];
                if ((level <= threshold) == inkIsDark)
                {
                    ink[n++] = (2 * x) + 1 - width;
                    ink[n++] = (2 * y) + 1 - height;
                }
            }
        }

        var diagonal = Math.Sqrt(((double)width * width) + ((double)height * height));
        var offset = (int)Math.Ceiling(diagonal / 2) + 2;
        using var profileBuffer = ResampleWeights.Rent<int>(scope, (2L * offset) + 1);
        var profile = unsafe(MemoryMarshal.Cast<byte, int>(profileBuffer.Span))[..((2 * offset) + 1)];

        var best = 0.0;
        var bestScore = long.MinValue;
        var steps = (int)Math.Floor(options.MaxAngle / CoarseStep);
        for (var i = -steps; i <= steps; i++)
        {
            Consider(i * CoarseStep, ink, profile, offset, options.MaxAngle, ref best, ref bestScore, cancellationToken);
        }

        var center = best;
        var fineSteps = (int)Math.Floor(CoarseStep / options.Precision);
        for (var i = -fineSteps; i <= fineSteps; i++)
        {
            Consider(center + (i * options.Precision), ink, profile, offset, options.MaxAngle, ref best, ref bestScore, cancellationToken);
        }

        return best;
    }

    /// <summary>Scores an angle and keeps it when it is better than the best one: a larger score, then nearer to 0, then negative.</summary>
    private static void Consider(double angle, ReadOnlySpan<int> ink, Span<int> profile, int offset, double maxAngle, ref double best, ref long bestScore, CancellationToken cancellationToken)
    {
        if (Math.Abs(angle) > maxAngle)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        DeterministicAngle.SinCos(angle / 180, out var sin, out var cos);
        profile.Clear();
        for (var i = 0; i < ink.Length; i += 2)
        {
            var projection = ((ink[i + 1] * cos) - (ink[i] * sin)) / 2;
            profile[(int)Math.Floor(projection) + offset]++;
        }

        var score = 0L;
        foreach (var bin in profile)
        {
            score += (long)bin * bin;
        }

        if (score > bestScore || (score == bestScore && (Math.Abs(angle) < Math.Abs(best) || (Math.Abs(angle) == Math.Abs(best) && angle < best))))
        {
            bestScore = score;
            best = angle;
        }
    }

    /// <summary>Reads the luma of every pixel composited over white, at 256 levels (rounded to nearest).</summary>
    private static void ReadPaperLevels(AllocationScope scope, PixelFormat format, scoped in PixelLease lease, Span<byte> levels, CancellationToken cancellationToken)
    {
        var width = lease.Width;
        var max = (long)ToneSamples.GetLevelCount(format) - 1;
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(format);
        using var row = ToneSamples.RentRow(scope, width);
        var samples = ToneSamples.GetSamples(row, width);
        var luma = ToneSamples.GetLuma(row, width);
        for (var y = 0; y < lease.Height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            ToneSamples.Read(format, lease.GetRowBytes(y)[..(width * bytesPerPixel)], samples);
            ToneSamples.ComputeLuma(samples, luma);
            for (var x = 0; x < width; x++)
            {
                var alpha = (long)samples[(4 * x) + 3];
                var paper = (luma[x] * alpha) + (max * (max - alpha));
                levels[(y * width) + x] = (byte)(((paper * 255) + (max * max / 2)) / (max * max));
            }
        }
    }

    /// <summary>Gets the threshold of Otsu: the smallest level <c>t</c> that maximizes the variance between the levels up to <c>t</c> and the others; <see langword="false"/> for a single level.</summary>
    private static bool TryGetOtsuThreshold(ReadOnlySpan<long> histogram, long count, out int threshold)
    {
        var total = 0.0;
        for (var i = 0; i < histogram.Length; i++)
        {
            total += (double)i * histogram[i];
        }

        threshold = 0;
        var bestVariance = 0.0;
        var weight = 0L;
        var sum = 0.0;
        for (var t = 0; t < histogram.Length - 1; t++)
        {
            weight += histogram[t];
            sum += (double)t * histogram[t];
            var other = count - weight;
            if (weight == 0 || other == 0)
                continue;

            var meanDifference = (sum / weight) - ((total - sum) / other);
            var variance = (double)weight * other * meanDifference * meanDifference;
            if (variance > bestVariance)
            {
                bestVariance = variance;
                threshold = t;
            }
        }

        return bestVariance > 0;
    }
}
