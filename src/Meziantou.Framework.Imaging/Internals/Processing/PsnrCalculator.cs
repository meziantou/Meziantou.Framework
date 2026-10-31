namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Computes the peak signal-to-noise ratio of two frames of the same size (<see cref="ImageSimilarity.ComputePsnr(ImageFrame, ImageFrame, CancellationToken)"/>).</summary>
/// <remarks>
/// <para>
/// The squared errors are summed as integers, so the mean squared error is exact. The R, G and B channels are
/// premultiplied by alpha with the rounding of <see cref="SsimAccumulator"/>, and their mean squared error gives the color
/// ratio, which is what scikit-image's <c>peak_signal_noise_ratio(expected, actual, data_range=255)</c> computes on the
/// H×W×3 arrays. The alpha channel gives its own ratio, and the result is the lower of the two.
/// </para>
/// <para>
/// Large frames are read by up to <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> workers, by row bands: the integer
/// sums of the bands add up to the same exact errors.
/// </para>
/// </remarks>
internal static class PsnrCalculator
{
    private const double PeakSquared = 255d * 255d;

    /// <exception cref="ImageResourceLimitException">The row buffers exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is outside [0, 1] or is not a number.</exception>
    public static double Compute(AllocationScope scope, PixelStorage expected, PixelFormat expectedFormat, PixelStorage actual, PixelFormat actualFormat, int maxDegreeOfParallelism, CancellationToken cancellationToken)
    {
        var size = new Size(expected.Width, expected.Height);
        var workers = RowBands.GetWorkerCount(size, maxDegreeOfParallelism);
        using var frames = new ComparedFrames(scope, expected, expectedFormat, actual, actualFormat, workers);

        // At most 3 × 255² per pixel: a ulong holds the sum for any frame the library can allocate
        var colorErrors = new ulong[workers];
        var alphaErrors = new ulong[workers];
        if (workers is 1)
        {
            SumErrors(frames, worker: 0, 0, size.Height, colorErrors, alphaErrors, cancellationToken);
        }
        else
        {
            RowBands.RunParallelInBandOrder(workers, band => SumErrors(frames, band, RowBands.GetBandStart(size.Height, band, workers), RowBands.GetBandStart(size.Height, band + 1, workers), colorErrors, alphaErrors, cancellationToken));
        }

        ulong colorError = 0;
        ulong alphaError = 0;
        for (var worker = 0; worker < workers; worker++)
        {
            colorError += colorErrors[worker];
            alphaError += alphaErrors[worker];
        }

        var pixelCount = (double)size.Width * size.Height;
        return Math.Min(ToPsnr(colorError / (3 * pixelCount)), ToPsnr(alphaError / pixelCount));
    }

    private static void SumErrors(ComparedFrames frames, int worker, int start, int end, ulong[] colorErrors, ulong[] alphaErrors, CancellationToken cancellationToken)
    {
        ulong colorError = 0;
        ulong alphaError = 0;
        for (var y = start; y < end; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expectedRow = frames.GetExpectedRow(worker, y);
            var actualRow = frames.GetActualRow(worker, y);
            for (var x = 0; x < expectedRow.Length; x++)
            {
                var e = expectedRow[x];
                var a = actualRow[x];
                colorError += SquaredError(SsimAccumulator.Premultiply(e.R, e.A), SsimAccumulator.Premultiply(a.R, a.A))
                    + SquaredError(SsimAccumulator.Premultiply(e.G, e.A), SsimAccumulator.Premultiply(a.G, a.A))
                    + SquaredError(SsimAccumulator.Premultiply(e.B, e.A), SsimAccumulator.Premultiply(a.B, a.A));
                alphaError += SquaredError(e.A, a.A);
            }
        }

        colorErrors[worker] = colorError;
        alphaErrors[worker] = alphaError;
    }

    private static ulong SquaredError(int expected, int actual)
    {
        var difference = expected - actual;
        return (ulong)(difference * difference);
    }

    private static double ToPsnr(double meanSquaredError) => meanSquaredError is 0 ? double.PositiveInfinity : 10 * Math.Log10(PeakSquared / meanSquaredError);
}
