using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

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
/// <para>
/// With 128-bit hardware vectors, the squared errors of 4 pixels are computed at a time in 16-bit lanes (the premultiplied
/// samples with the exact division of <see cref="PixelConverter.DivideBy255Vector"/>, the squares of differences of at
/// most 255) and summed per channel as 32-bit integers, flushed before they can overflow: the sums are exact, so they are
/// those of <see cref="SumRowErrorsScalar"/>.
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
            SumRowErrors(frames.GetExpectedRow(worker, y), frames.GetActualRow(worker, y), ref colorError, ref alphaError);
        }

        colorErrors[worker] = colorError;
        alphaErrors[worker] = alphaError;
    }

    /// <summary>Adds the squared errors of the premultiplied colors and of the alphas of two rows to the sums.</summary>
    internal static void SumRowErrors(ReadOnlySpan<Rgba32> expected, ReadOnlySpan<Rgba32> actual, ref ulong colorError, ref ulong alphaError)
    {
        var x = 0;
        var length = Math.Min(expected.Length, actual.Length);
        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian)
        {
            ref var expectedBytes = ref unsafe(MemoryMarshal.GetReference(MemoryMarshal.AsBytes(expected)));
            ref var actualBytes = ref unsafe(MemoryMarshal.GetReference(MemoryMarshal.AsBytes(actual)));
            while (x + 4 <= length)
            {
                // Each lane adds the squared error of one sample of each pixel, at most 255²: 65,536 pixels fit in 32 bits
                var end = Math.Min(length, x + 65536);
                var sums = Vector128<uint>.Zero;
                for (; x + 4 <= end; x += 4)
                {
                    var (expectedLow, expectedHigh) = Premultiply(unsafe(Vector128.LoadUnsafe(ref expectedBytes, (nuint)(4 * x))));
                    var (actualLow, actualHigh) = Premultiply(unsafe(Vector128.LoadUnsafe(ref actualBytes, (nuint)(4 * x))));
                    var low = Square(expectedLow, actualLow);
                    var high = Square(expectedHigh, actualHigh);
                    sums += Vector128.WidenLower(low) + Vector128.WidenUpper(low) + Vector128.WidenLower(high) + Vector128.WidenUpper(high);
                }

                colorError += (ulong)sums.GetElement(0) + sums.GetElement(1) + sums.GetElement(2);
                alphaError += sums.GetElement(3);
            }
        }

        SumRowErrorsScalar(expected[x..length], actual[x..length], ref colorError, ref alphaError);
    }

    /// <summary>The pixel-at-a-time reference of <see cref="SumRowErrors"/>.</summary>
    internal static void SumRowErrorsScalar(ReadOnlySpan<Rgba32> expected, ReadOnlySpan<Rgba32> actual, ref ulong colorError, ref ulong alphaError)
    {
        for (var x = 0; x < expected.Length; x++)
        {
            var e = expected[x];
            var a = actual[x];
            colorError += SquaredError(SsimAccumulator.Premultiply(e.R, e.A), SsimAccumulator.Premultiply(a.R, a.A))
                + SquaredError(SsimAccumulator.Premultiply(e.G, e.A), SsimAccumulator.Premultiply(a.G, a.A))
                + SquaredError(SsimAccumulator.Premultiply(e.B, e.A), SsimAccumulator.Premultiply(a.B, a.A));
            alphaError += SquaredError(e.A, a.A);
        }
    }

    /// <summary>
    /// The samples of 4 pixels in 16-bit lanes, the colors premultiplied by their alpha as <see cref="SsimAccumulator.Premultiply"/>
    /// does; the alpha is multiplied by 255, which <c>(a * 255 + 127) / 255</c> brings back to itself.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<ushort> Low, Vector128<ushort> High) Premultiply(Vector128<byte> pixels)
    {
        var (low, high) = Vector128.Widen(pixels);
        var order = Vector128.Create((ushort)3, 3, 3, 3, 7, 7, 7, 7);
        var alphaLanes = Vector128.Create(0, 0, 0, ushort.MaxValue, 0, 0, 0, ushort.MaxValue);
        var peak = Vector128.Create((ushort)byte.MaxValue);
        var lowFactors = Vector128.ConditionalSelect(alphaLanes, peak, Vector128.Shuffle(low, order));
        var highFactors = Vector128.ConditionalSelect(alphaLanes, peak, Vector128.Shuffle(high, order));
        return (PixelConverter.DivideBy255Vector(low * lowFactors), PixelConverter.DivideBy255Vector(high * highFactors));
    }

    /// <summary>The squares of the differences of samples of at most 255: at most 255², exact in 16 bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> Square(Vector128<ushort> expected, Vector128<ushort> actual)
    {
        var difference = expected.AsInt16() - actual.AsInt16();
        return (difference * difference).AsUInt16();
    }

    private static ulong SquaredError(int expected, int actual)
    {
        var difference = expected - actual;
        return (ulong)(difference * difference);
    }

    private static double ToPsnr(double meanSquaredError) => meanSquaredError is 0 ? double.PositiveInfinity : 10 * Math.Log10(PeakSquared / meanSquaredError);
}
