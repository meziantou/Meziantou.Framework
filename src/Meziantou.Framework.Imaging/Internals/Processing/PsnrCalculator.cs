namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Computes the peak signal-to-noise ratio of two frames of the same size (<see cref="ImageSimilarity.ComputePsnr"/>).</summary>
/// <remarks>
/// The squared errors are summed as integers, so the mean squared error is exact. The R, G and B channels are
/// premultiplied by alpha with the rounding of <see cref="SsimAccumulator"/>, and their mean squared error gives the color
/// ratio, which is what scikit-image's <c>peak_signal_noise_ratio(expected, actual, data_range=255)</c> computes on the
/// H×W×3 arrays. The alpha channel gives its own ratio, and the result is the lower of the two.
/// </remarks>
internal static class PsnrCalculator
{
    private const double PeakSquared = 255d * 255d;

    /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
    public static double Compute(PixelSource expected, PixelSource actual, CancellationToken cancellationToken)
    {
        // At most 3 × 255² per pixel: a ulong holds the sum for any frame the library can allocate
        ulong colorError = 0;
        ulong alphaError = 0;
        for (var y = 0; y < expected.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expectedRow = expected.GetRow(y);
            var actualRow = actual.GetRow(y);
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

        var pixelCount = (double)expected.Width * expected.Height;
        return Math.Min(ToPsnr(colorError / (3 * pixelCount)), ToPsnr(alphaError / pixelCount));
    }

    private static ulong SquaredError(int expected, int actual)
    {
        var difference = expected - actual;
        return (ulong)(difference * difference);
    }

    private static double ToPsnr(double meanSquaredError) => meanSquaredError is 0 ? double.PositiveInfinity : 10 * Math.Log10(PeakSquared / meanSquaredError);
}
