namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The adaptive Wiener filter of
/// <see cref="ImageProcessingExtensions.WienerFilter(Image, int, double?, ConvolutionEdgeMode, CancellationToken)"/> (Lim,
/// "Two-Dimensional Signal and Image Processing", 1990, the filter of MATLAB's <c>wiener2</c>): every sample <c>x</c> moves
/// toward the mean <c>μ</c> of its window by <c>min(ν² / σ², 1)</c>, where <c>σ²</c> is the variance of the window and
/// <c>ν²</c> that of the noise. Flat windows, whose variance is that of the noise, are smoothed to their mean; windows that
/// vary much more (edges, texture) are kept.
/// </summary>
/// <remarks>
/// <para>
/// The windows are the <c>(2 r + 1)²</c> pixels around each pixel, read beyond the frame by the edge mode, every pixel
/// counting: with <see cref="ConvolutionEdgeMode.Zero"/>, the zero padding of <c>wiener2</c> and of SciPy's
/// <c>scipy.signal.wiener</c>. Their sums and sums of squares come from <see cref="GuidedFilterer.SumWindows"/>, in double
/// precision and in a constant time per sample whatever the radius; the variance is <c>E[x²] - μ²</c>, clamped to 0.
/// </para>
/// <para>
/// The noise variance is given as a squared fraction of the sample range, or estimated for each working sample of each
/// frame as the mean of the variances of its windows (those that are finite), as <c>wiener2</c> estimates it. The
/// estimate takes a first pass over the windows, added in row order on the calling thread.
/// </para>
/// </remarks>
internal static class AdaptiveWienerFilterer
{
    /// <summary>Gets the number of doubles of the scratch of a frame of <paramref name="format"/>: a sum and a sum of squares per sample.</summary>
    public static long GetScratchDoubles(PixelFormat format, Size size, int radius)
        => GuidedFilterer.GetWindowScratchDoubles(size.Width, radius, 2L * PixelFormats.GetComponentCount(format));

    /// <summary>Filters a leased frame of the format of the copy in place.</summary>
    /// <param name="storage">The frame.</param>
    /// <param name="copy">The working copy, prepared.</param>
    /// <param name="scratch">The scratch, of <see cref="GetScratchDoubles"/> doubles.</param>
    /// <param name="radius">The radius of the windows, positive.</param>
    /// <param name="noiseVariance">The variance of the noise, a squared fraction of the sample range, or <see langword="null"/> to estimate it.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
    public static void Apply(PixelStorage storage, WorkingFrameCopy copy, Span<double> scratch, int radius, double? noiseVariance, CancellationToken cancellationToken)
    {
        copy.Load(storage, cancellationToken);
        var channels = copy.WorkingChannels;
        var diameter = (2d * radius) + 1;
        var count = diameter * diameter;
        var noise = new double[channels];
        if (noiseVariance is { } variance)
        {
            // A working color is a fraction of the range times ColorScale⁻¹ (premultiplied: the product of two samples)
            for (var c = 0; c < channels; c++)
            {
                var scale = channels == 4 && c == 3 ? copy.AlphaScale : copy.ColorScale;
                noise[c] = variance / (scale * scale);
            }
        }
        else
        {
            var sums = new double[channels];
            var counts = new long[channels];
            GuidedFilterer.SumWindows(copy, scratch, 2 * channels, radius, (y, values) => LoadStatistics(copy, y, values), (y, windows) =>
            {
                for (var x = 0; x < copy.Size.Width; x++)
                {
                    for (var c = 0; c < channels; c++)
                    {
                        var index = 2 * ((x * channels) + c);
                        var windowVariance = GetVariance(windows[index], windows[index + 1], count);
                        if (double.IsFinite(windowVariance))
                        {
                            sums[c] += windowVariance;
                            counts[c]++;
                        }
                    }
                }
            }, cancellationToken);

            for (var c = 0; c < channels; c++)
            {
                noise[c] = counts[c] == 0 ? 0 : sums[c] / counts[c];
            }
        }

        GuidedFilterer.SumWindows(copy, scratch, 2 * channels, radius, (y, values) => LoadStatistics(copy, y, values), (y, windows) =>
        {
            var row = copy.GetRow(y);
            var output = copy.GetRowBuffer(0)[..row.Length];
            for (var i = 0; i < row.Length; i++)
            {
                var c = i % channels;
                var mean = windows[2 * i] / count;
                var windowVariance = GetVariance(windows[2 * i], windows[(2 * i) + 1], count);

                // x - min(ν² / σ², 1) (x - μ): μ where the window varies no more than the noise, x without noise
                var gain = windowVariance > noise[c] ? noise[c] / windowVariance : noise[c] > 0 ? 1 : 0;
                output[i] = row[i] - (gain * (row[i] - mean));
            }

            copy.Store(storage, y, output);
        }, cancellationToken);
    }

    /// <summary>Sets every working sample of row <paramref name="y"/> and its square.</summary>
    private static void LoadStatistics(WorkingFrameCopy copy, int y, Span<double> values)
    {
        var row = copy.GetRow(y);
        for (var i = 0; i < row.Length; i++)
        {
            double sample = row[i];
            values[2 * i] = sample;
            values[(2 * i) + 1] = sample * sample;
        }
    }

    /// <summary>Gets the variance of a window from its sum and its sum of squares, clamped to 0 (the difference of two rounded values).</summary>
    private static double GetVariance(double sum, double sumOfSquares, double count)
    {
        var mean = sum / count;
        var variance = (sumOfSquares / count) - (mean * mean);
        return variance > 0 ? variance : double.IsNaN(variance) ? variance : 0;
    }
}
