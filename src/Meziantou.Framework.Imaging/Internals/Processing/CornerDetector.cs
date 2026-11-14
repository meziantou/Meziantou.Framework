using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The corner detectors of <see cref="ImageProcessingExtensions.DetectCorners(ImageFrame, CornerDetectionOptions?, CancellationToken)"/>:
/// Harris and Stephens ("A Combined Corner and Edge Detector", 1988), Shi and Tomasi ("Good Features to Track", 1994) and
/// the FAST-9 segment test of Rosten and Drummond ("Machine Learning for High-Speed Corner Detection", 2006).
/// </summary>
/// <remarks>
/// <para>
/// The frame is read as its Rec. 709 luma premultiplied by alpha, as a fraction of the largest sample (floating-point
/// samples binned to 65,536 levels, <see cref="ToneSamples"/>). The structure tensor of Harris and Shi-Tomasi is made of the
/// products of the Sobel derivatives (divided by 8, the pixels beyond the edges being the edge pixels), each smoothed by
/// the Gaussian weights of <see cref="GaussianKernel"/>, rows then columns, with the same edges.
/// </para>
/// <para>
/// The FAST circle is the 16 pixels at distance 3 of the Bresenham circle; the pixels nearer than 3 to an edge are not
/// tested. The score of a corner is the larger of the sums of <c>|I(q) - I(p)| - t</c> over the brighter and over the
/// darker pixels of the circle.
/// </para>
/// <para>
/// A candidate is a local maximum of the response among its 8 neighbors (greater than those before it in row order, not
/// less than those after it) above the threshold; the candidates are sorted by response, then by row, then by column, and
/// kept unless a kept corner is within the minimum distance. Everything is computed with additions, multiplications,
/// divisions and square roots in double precision, so the result does not depend on the platform.
/// </para>
/// </remarks>
internal static class CornerDetector
{
    private static readonly int[] CircleX = [0, 1, 2, 3, 3, 3, 2, 1, 0, -1, -2, -3, -3, -3, -2, -1];
    private static readonly int[] CircleY = [-3, -3, -2, -1, 0, 1, 2, 3, 3, 3, 2, 1, 0, -1, -2, -3];

    public static IReadOnlyList<ImageCorner> Detect(AllocationScope scope, PixelFormat format, scoped in PixelLease lease, CornerDetectionOptions options, CancellationToken cancellationToken)
    {
        var width = lease.Width;
        var height = lease.Height;
        var count = (long)width * height;
        using var lumaBuffer = ResampleWeights.Rent<double>(scope, count);
        using var responseBuffer = ResampleWeights.Rent<double>(scope, count);
        var luma = unsafe(MemoryMarshal.Cast<byte, double>(lumaBuffer.Span))[..(int)count];
        var responses = unsafe(MemoryMarshal.Cast<byte, double>(responseBuffer.Span))[..(int)count];
        ReadLuma(scope, format, lease, luma, cancellationToken);

        double threshold;
        if (options.Method == CornerDetectionMethod.Fast)
        {
            ComputeFast(luma, responses, width, height, options.FastThreshold, cancellationToken);
            threshold = double.Epsilon;
        }
        else
        {
            var largest = ComputeStructureTensorResponses(scope, luma, responses, width, height, options, cancellationToken);
            if (!(largest > 0))
                return [];

            threshold = largest * options.ResponseThreshold;
        }

        return Select(responses, width, height, threshold, options, cancellationToken);
    }

    /// <summary>Reads the luma premultiplied by alpha of every pixel, as a fraction of the largest sample.</summary>
    internal static void ReadLuma(AllocationScope scope, PixelFormat format, scoped in PixelLease lease, Span<double> luma, CancellationToken cancellationToken)
    {
        var width = lease.Width;
        var max = (double)(ToneSamples.GetLevelCount(format) - 1);
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(format);
        using var row = ToneSamples.RentRow(scope, width);
        var samples = ToneSamples.GetSamples(row, width);
        var levels = ToneSamples.GetLuma(row, width);
        for (var y = 0; y < lease.Height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            ToneSamples.Read(format, lease.GetRowBytes(y)[..(width * bytesPerPixel)], samples);
            ToneSamples.ComputeLuma(samples, levels);
            for (var x = 0; x < width; x++)
            {
                luma[(y * width) + x] = levels[x] * (samples[(4 * x) + 3] / max) / max;
            }
        }
    }

    /// <summary>Computes the Harris or Shi-Tomasi response of every pixel and returns the largest one.</summary>
    private static double ComputeStructureTensorResponses(AllocationScope scope, ReadOnlySpan<double> luma, Span<double> responses, int width, int height, CornerDetectionOptions options, CancellationToken cancellationToken)
    {
        var count = (long)width * height;
        using var tensorBuffer = ResampleWeights.Rent<double>(scope, 3 * count);
        using var temporaryBuffer = ResampleWeights.Rent<double>(scope, count);
        var tensor = unsafe(MemoryMarshal.Cast<byte, double>(tensorBuffer.Span))[..(int)(3 * count)];
        var temporary = unsafe(MemoryMarshal.Cast<byte, double>(temporaryBuffer.Span))[..(int)count];
        var xx = tensor[..(int)count];
        var yy = tensor.Slice((int)count, (int)count);
        var xy = tensor.Slice((int)(2 * count), (int)count);
        for (var y = 0; y < height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var above = Math.Max(y - 1, 0) * width;
            var row = y * width;
            var below = Math.Min(y + 1, height - 1) * width;
            for (var x = 0; x < width; x++)
            {
                var left = Math.Max(x - 1, 0);
                var right = Math.Min(x + 1, width - 1);
                var gx = ((luma[above + right] - luma[above + left]) + (2 * (luma[row + right] - luma[row + left])) + (luma[below + right] - luma[below + left])) / 8;
                var gy = ((luma[below + left] - luma[above + left]) + (2 * (luma[below + x] - luma[above + x])) + (luma[below + right] - luma[above + right])) / 8;
                xx[row + x] = gx * gx;
                yy[row + x] = gy * gy;
                xy[row + x] = gx * gy;
            }
        }

        var weights = GaussianKernel.Create(options.Sigma);
        Smooth(xx, temporary, width, height, weights, cancellationToken);
        Smooth(yy, temporary, width, height, weights, cancellationToken);
        Smooth(xy, temporary, width, height, weights, cancellationToken);

        var largest = 0.0;
        for (var i = 0; i < responses.Length; i++)
        {
            var a = xx[i];
            var c = yy[i];
            var b = xy[i];
            var trace = a + c;
            var response = options.Method == CornerDetectionMethod.ShiTomasi
                ? (trace - Math.Sqrt(((a - c) * (a - c)) + (4 * b * b))) / 2
                : ((a * c) - (b * b)) - (options.HarrisK * trace * trace);
            responses[i] = response;
            largest = Math.Max(largest, response);
        }

        return largest;
    }

    /// <summary>Smooths a plane with separable weights, rows then columns, the pixels beyond the edges being the edge pixels.</summary>
    private static void Smooth(Span<double> plane, Span<double> temporary, int width, int height, double[] weights, CancellationToken cancellationToken)
    {
        var radius = weights.Length / 2;
        for (var y = 0; y < height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var sum = 0.0;
                for (var k = 0; k < weights.Length; k++)
                {
                    sum += weights[k] * plane[row + Math.Clamp(x + k - radius, 0, width - 1)];
                }

                temporary[row + x] = sum;
            }
        }

        for (var y = 0; y < height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            for (var x = 0; x < width; x++)
            {
                var sum = 0.0;
                for (var k = 0; k < weights.Length; k++)
                {
                    sum += weights[k] * temporary[(Math.Clamp(y + k - radius, 0, height - 1) * width) + x];
                }

                plane[(y * width) + x] = sum;
            }
        }
    }

    /// <summary>Computes the FAST-9 score of every pixel: 0 for a pixel that is not a corner.</summary>
    private static void ComputeFast(ReadOnlySpan<double> luma, Span<double> responses, int width, int height, double threshold, CancellationToken cancellationToken)
    {
        responses.Clear();
        Span<int> states = stackalloc int[16];
        for (var y = 3; y < height - 3; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            for (var x = 3; x < width - 3; x++)
            {
                var center = luma[(y * width) + x];
                var brighter = 0.0;
                var darker = 0.0;
                for (var i = 0; i < 16; i++)
                {
                    var value = luma[((y + CircleY[i]) * width) + x + CircleX[i]];
                    if (value > center + threshold)
                    {
                        states[i] = 1;
                        brighter += value - center - threshold;
                    }
                    else if (value < center - threshold)
                    {
                        states[i] = -1;
                        darker += center - value - threshold;
                    }
                    else
                    {
                        states[i] = 0;
                    }
                }

                if (HasArc(states, 1) || HasArc(states, -1))
                {
                    responses[(y * width) + x] = Math.Max(brighter, darker);
                }
            }
        }
    }

    /// <summary>Gets whether 9 contiguous pixels of the circle (wrapping around) have a state.</summary>
    private static bool HasArc(ReadOnlySpan<int> states, int state)
    {
        var run = 0;
        for (var i = 0; i < 16 + 8; i++)
        {
            if (states[i % 16] == state)
            {
                run++;
                if (run >= 9)
                    return true;
            }
            else
            {
                run = 0;
            }
        }

        return false;
    }

    /// <summary>Keeps the local maxima above the threshold, the strongest first, at least the minimum distance apart.</summary>
    private static List<ImageCorner> Select(ReadOnlySpan<double> responses, int width, int height, double threshold, CornerDetectionOptions options, CancellationToken cancellationToken)
    {
        var candidates = new List<ImageCorner>();
        for (var y = 0; y < height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            for (var x = 0; x < width; x++)
            {
                var response = responses[(y * width) + x];
                if (response >= threshold && IsLocalMaximum(responses, width, height, x, y, response))
                {
                    candidates.Add(new ImageCorner(x, y, response));
                }
            }
        }

        candidates.Sort(static (a, b) =>
        {
            var byResponse = b.Response.CompareTo(a.Response);
            if (byResponse != 0)
                return byResponse;

            var byRow = a.Y.CompareTo(b.Y);
            return byRow != 0 ? byRow : a.X.CompareTo(b.X);
        });

        var corners = new List<ImageCorner>();
        var minDistanceSquared = (long)options.MinDistance * options.MinDistance;
        foreach (var candidate in candidates)
        {
            if (corners.Exists(corner => (((long)(corner.X - candidate.X) * (corner.X - candidate.X)) + ((long)(corner.Y - candidate.Y) * (corner.Y - candidate.Y))) < minDistanceSquared))
                continue;

            corners.Add(candidate);
            if (corners.Count == options.MaxCorners)
                break;
        }

        return corners;
    }

    private static bool IsLocalMaximum(ReadOnlySpan<double> responses, int width, int height, int x, int y, double response)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if ((dx == 0 && dy == 0) || (uint)(x + dx) >= (uint)width || (uint)(y + dy) >= (uint)height)
                    continue;

                var neighbor = responses[((y + dy) * width) + x + dx];
                var isBefore = dy < 0 || (dy == 0 && dx < 0);
                if (isBefore ? neighbor >= response : neighbor > response)
                    return false;
            }
        }

        return true;
    }
}
