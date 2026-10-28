using System.Globalization;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// Measures how similar two frames look: the Structural Similarity Index (SSIM), the peak signal-to-noise ratio (PSNR), the
/// SSIMULACRA 2 score and the Butteraugli distance, and the distance between two 64-bit perceptual hashes, the difference
/// hash (dHash) and the perceptual hash (pHash).
/// </summary>
/// <remarks>
/// <para>
/// Every member reads the pixels as 8-bit RGBA, whatever the pixel format, with the conversion of
/// <see cref="Image.CloneAs{TPixel}"/>: 16-bit samples are rounded to 8 bits, gray samples are copied to the three color
/// channels, and formats without alpha are opaque. Floating-point samples (<see cref="RgbaFloat"/>,
/// <see cref="RgbaHalf"/>) are rounded to 8 bits as they are stored, whatever their transfer function label, like the
/// samples of the other formats: a sample outside [0, 1], or that is not a number, has no 8-bit value and is rejected
/// with an <see cref="UnsupportedImageFeatureException"/>, never clipped (convert such an image with
/// <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/> and <see cref="PixelConversionOptions.OutOfRangeSamples"/>
/// or <see cref="PixelConversionOptions.ToneMapping"/> first). Two frames of different pixel formats can be compared. Nothing is
/// written to the pixels; the frames are leased for the duration of the call, so a frame cannot be measured while a lease is
/// active on it. A frame can be compared with itself.
/// </para>
/// <para>
/// The results are deterministic: SSIM and the hashes depend only on the pixels, not on the hardware. PSNR, SSIMULACRA 2 and
/// Butteraugli use the logarithm, power and cube root functions of the platform, so their last digits can differ between
/// platforms. The temporary buffers are charged to the allocation scope of the image of the frames. An animated image is
/// compared frame by frame.
/// </para>
/// <para>
/// <b>SSIM.</b> The score is computed in every 7×7 window that fits inside the frame, moved one pixel at a time, with
/// uniform weights, the sample variance and covariance, and <c>K1 = 0.01</c>, <c>K2 = 0.03</c>, <c>L = 255</c>: for opaque
/// frames, it is the mean over the three color channels of what scikit-image's
/// <c>structural_similarity(expected, actual, data_range=255, channel_axis=-1)</c> computes. A frame narrower or shorter
/// than 7 pixels uses a window as wide or as tall as the frame. The color channels are premultiplied by alpha, so a fully
/// transparent pixel scores the same whatever color it hides; the alpha channel is scored on its own, and the result is the
/// lower of the color score and the alpha score.
/// </para>
/// <para>
/// <b>Hashes.</b> Both hashes start from an exact, area-weighted luminance thumbnail (BT.601 weights; 9×8 cells for the
/// dHash, 32×32 for the pHash), so a change anywhere in the frame affects it. A dHash bit is set when a cell is brighter than
/// its right neighbor by more than half a luminance level; a pHash bit is set when a low-frequency DCT coefficient of the
/// thumbnail exceeds the median coefficient by more than one level. These tolerances keep flat areas from flipping bits on
/// invisible noise. The hashes describe the structure, not the brightness: a hash distance is therefore the Hamming distance
/// plus the difference between the mean luminances on the same scale (about 4 levels out of 255 per unit). A frame that is
/// not fully opaque is evaluated composited over black and over white, and the larger distance is kept. The distance is
/// capped at <see cref="MaxHashDistance"/>.
/// </para>
/// <para>
/// <b>PSNR.</b> The ratio, in decibels, of the peak value (255) to the mean squared error of the R, G and B channels
/// premultiplied by alpha (as for SSIM): for opaque frames, it is what scikit-image's
/// <c>peak_signal_noise_ratio(expected, actual, data_range=255)</c> computes. The alpha channel gives its own ratio, and
/// the result is the lower of the two. Identical frames have an infinite ratio.
/// </para>
/// <para>
/// <b>SSIMULACRA 2.</b> A port of the <c>ssimulacra2</c> tool of libjxl 0.12.0, a perceptual metric tuned on subjective
/// scores: the frames are compared in the XYB color space of JPEG XL at 6 scales, with an SSIM map and two edge maps (new
/// artifacts and lost details). The planes are 32-bit floats, and the arithmetic of the tool is reproduced (its sRGB curve,
/// cube root and blur, with fused multiply-adds): the scores agree with the 8 decimals it prints. A frame that is not fully
/// opaque is composited over a dark and a bright gray, and the lower score is kept, as the tool does.
/// </para>
/// <para>
/// <b>Butteraugli.</b> A port of the Butteraugli metric of libjxl 0.12.0 (by Jyrki Alakuijala), with the parameters of its
/// <c>butteraugli_main</c> tool for SDR images (80 nits): a psychovisual model of the differences per frequency band,
/// masked by the activity of the frames, that gives a distance for every pixel. The results are within about 0.01% of the
/// tool. A frame that is not fully opaque is composited over black and over white, and every pixel keeps the larger
/// distance, as libjxl does. A frame smaller than 8×8 is extended to 8×8 by repeating its edges, as libjxl's
/// <c>ButteraugliDiffmap</c> does. It is the slowest member, and it needs about 120 bytes per pixel.
/// </para>
/// <para>
/// <b>Parallelism.</b> SSIM, PSNR, SSIMULACRA 2 and Butteraugli use up to
/// <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> workers (the configuration of the image of the expected frame)
/// for frames of at least 65,536 pixels, and the result is identical whatever the number of workers: the floating-point
/// sums are made by bands of 64 rows, added in order. With several workers, each one has its own row buffers.
/// </para>
/// </remarks>
public static class ImageSimilarity
{
    /// <summary>The largest distance between two hashes: a hash has 64 bits.</summary>
    public const int MaxHashDistance = PerceptualHasher.MaxDistance;

    /// <summary>Computes the mean Structural Similarity Index (SSIM) of two frames of the same size.</summary>
    /// <param name="expected">The reference frame.</param>
    /// <param name="actual">The frame compared with the reference.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <returns>A score from <c>-1</c> to <c>1</c>: <c>1</c> for identical pixels, around <c>0</c> for unrelated frames.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expected"/> or <paramref name="actual"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The frames do not have the same size.</exception>
    /// <exception cref="InvalidOperationException">A lease is active on one of the frames.</exception>
    /// <exception cref="ObjectDisposedException">A frame is no longer attached to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The row buffers exceed the allocation limit.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is outside [0, 1] or is not a number.</exception>
    public static double ComputeSsim(ImageFrame expected, ImageFrame actual, CancellationToken cancellationToken = default)
    {
        var (expectedStorage, actualStorage) = GetStorages(expected, actual, cancellationToken);
        using var leases = PixelLeasePair.Acquire(expectedStorage, actualStorage);
        return SsimAccumulator.Compute(expectedStorage.Owner.Scope, expectedStorage, expected.PixelFormat, actualStorage, actual.PixelFormat, GetMaxDegreeOfParallelism(expected), cancellationToken);
    }

    /// <summary>Computes the peak signal-to-noise ratio (PSNR) of two frames of the same size.</summary>
    /// <param name="expected">The reference frame.</param>
    /// <param name="actual">The frame compared with the reference.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <returns>
    /// The ratio in decibels: <see cref="double.PositiveInfinity"/> for identical pixels; about 30 to 50 for a lossy
    /// encoding, higher values meaning smaller differences.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="expected"/> or <paramref name="actual"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The frames do not have the same size.</exception>
    /// <exception cref="InvalidOperationException">A lease is active on one of the frames.</exception>
    /// <exception cref="ObjectDisposedException">A frame is no longer attached to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The row buffers exceed the allocation limit.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is outside [0, 1] or is not a number.</exception>
    public static double ComputePsnr(ImageFrame expected, ImageFrame actual, CancellationToken cancellationToken = default)
    {
        var (expectedStorage, actualStorage) = GetStorages(expected, actual, cancellationToken);
        using var leases = PixelLeasePair.Acquire(expectedStorage, actualStorage);
        return PsnrCalculator.Compute(expectedStorage.Owner.Scope, expectedStorage, expected.PixelFormat, actualStorage, actual.PixelFormat, GetMaxDegreeOfParallelism(expected), cancellationToken);
    }

    /// <summary>Computes the SSIMULACRA 2 score of two frames of the same size, at least 8×8.</summary>
    /// <param name="expected">The reference frame.</param>
    /// <param name="actual">The frame compared with the reference.</param>
    /// <param name="cancellationToken">The token checked between the steps of the computation.</param>
    /// <returns>
    /// A score of at most <c>100</c>: <c>100</c> for identical pixels, about <c>90</c> when the difference is not visible,
    /// <c>70</c> for a high quality, <c>50</c> for a medium quality, and negative values for very different frames.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="expected"/> or <paramref name="actual"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The frames do not have the same size, or are smaller than 8×8.</exception>
    /// <exception cref="InvalidOperationException">A lease is active on one of the frames.</exception>
    /// <exception cref="ObjectDisposedException">A frame is no longer attached to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The planes of the computation exceed the allocation limit.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is outside [0, 1] or is not a number.</exception>
    public static double ComputeSsimulacra2(ImageFrame expected, ImageFrame actual, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        if (expected.Width < Ssimulacra2Calculator.MinimumSize || expected.Height < Ssimulacra2Calculator.MinimumSize)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"SSIMULACRA 2 needs frames of at least {Ssimulacra2Calculator.MinimumSize}x{Ssimulacra2Calculator.MinimumSize} pixels: the expected frame is {expected.Width}x{expected.Height}."), nameof(expected));

        return Compute(expected, actual, Ssimulacra2Calculator.Compute, cancellationToken);
    }

    /// <summary>Computes the Butteraugli distance between two frames of the same size.</summary>
    /// <param name="expected">The reference frame.</param>
    /// <param name="actual">The frame compared with the reference.</param>
    /// <param name="cancellationToken">The token checked between the steps of the computation.</param>
    /// <returns>
    /// The largest distance of the map and its 3-norm: <c>0</c> for identical pixels, below about <c>1</c> when the
    /// difference is not visible.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="expected"/> or <paramref name="actual"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The frames do not have the same size.</exception>
    /// <exception cref="InvalidOperationException">A lease is active on one of the frames.</exception>
    /// <exception cref="ObjectDisposedException">A frame is no longer attached to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The planes of the computation exceed the allocation limit.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is outside [0, 1] or is not a number.</exception>
    public static ButteraugliDistance ComputeButteraugliDistance(ImageFrame expected, ImageFrame actual, CancellationToken cancellationToken = default)
        => Compute(expected, actual, ButteraugliCalculator.Compute, cancellationToken);

    /// <summary>Computes the 64-bit difference hash (dHash) of a frame composited over a black background.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <returns>The hash. Compare hashes with <see cref="System.Numerics.BitOperations.PopCount(ulong)"/> of their exclusive or.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A lease is active on the frame.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The row buffer exceeds the allocation limit.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is outside [0, 1] or is not a number.</exception>
    public static ulong ComputeDifferenceHash(ImageFrame frame, CancellationToken cancellationToken = default) => ComputeHash(frame, perceptual: false, cancellationToken);

    /// <summary>Computes the 64-bit perceptual hash (pHash) of a frame composited over a black background.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <returns>The hash. Compare hashes with <see cref="System.Numerics.BitOperations.PopCount(ulong)"/> of their exclusive or.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A lease is active on the frame.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The row buffer exceeds the allocation limit.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is outside [0, 1] or is not a number.</exception>
    public static ulong ComputePerceptualHash(ImageFrame frame, CancellationToken cancellationToken = default) => ComputeHash(frame, perceptual: true, cancellationToken);

    /// <summary>
    /// Computes the difference hash (dHash) distance between two frames of the same size: the Hamming distance between their
    /// hashes plus the difference between their mean luminances, over black and over white for frames that are not opaque.
    /// </summary>
    /// <param name="expected">The reference frame.</param>
    /// <param name="actual">The frame compared with the reference.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <returns>A distance from <c>0</c> (same structure and brightness) to <see cref="MaxHashDistance"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expected"/> or <paramref name="actual"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The frames do not have the same size.</exception>
    /// <exception cref="InvalidOperationException">A lease is active on one of the frames.</exception>
    /// <exception cref="ObjectDisposedException">A frame is no longer attached to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The row buffers exceed the allocation limit.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is outside [0, 1] or is not a number.</exception>
    public static int ComputeDifferenceHashDistance(ImageFrame expected, ImageFrame actual, CancellationToken cancellationToken = default) => ComputeDistance(expected, actual, perceptual: false, cancellationToken);

    /// <summary>
    /// Computes the perceptual hash (pHash) distance between two frames of the same size: the Hamming distance between their
    /// hashes plus the difference between their mean luminances, over black and over white for frames that are not opaque.
    /// </summary>
    /// <param name="expected">The reference frame.</param>
    /// <param name="actual">The frame compared with the reference.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <returns>A distance from <c>0</c> (same structure and brightness) to <see cref="MaxHashDistance"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expected"/> or <paramref name="actual"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The frames do not have the same size.</exception>
    /// <exception cref="InvalidOperationException">A lease is active on one of the frames.</exception>
    /// <exception cref="ObjectDisposedException">A frame is no longer attached to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The row buffers exceed the allocation limit.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is outside [0, 1] or is not a number.</exception>
    public static int ComputePerceptualHashDistance(ImageFrame expected, ImageFrame actual, CancellationToken cancellationToken = default) => ComputeDistance(expected, actual, perceptual: true, cancellationToken);

    private static ulong ComputeHash(ImageFrame frame, bool perceptual, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var storage = frame.GetStorage();
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = storage.AcquireLease();
        using var reader = Rgba32RowReader.Create(storage.Owner.Scope, frame.PixelFormat, storage.Width);
        return PerceptualHasher.ComputeHash(new PixelSource(lease, reader, frame.PixelFormat), perceptual, cancellationToken);
    }

    private static int ComputeDistance(ImageFrame expected, ImageFrame actual, bool perceptual, CancellationToken cancellationToken)
    {
        var (expectedStorage, actualStorage) = GetStorages(expected, actual, cancellationToken);
        using var leases = PixelLeasePair.Acquire(expectedStorage, actualStorage);
        using var expectedReader = Rgba32RowReader.Create(expectedStorage.Owner.Scope, expected.PixelFormat, expectedStorage.Width);
        using var actualReader = Rgba32RowReader.Create(actualStorage.Owner.Scope, actual.PixelFormat, actualStorage.Width);
        var expectedSource = new PixelSource(leases.First, expectedReader, expected.PixelFormat);
        var actualSource = new PixelSource(leases.Second, actualReader, actual.PixelFormat);
        return PerceptualHasher.ComputeDistance(expectedSource, actualSource, perceptual, cancellationToken);
    }

    private static T Compute<T>(ImageFrame expected, ImageFrame actual, PairMetric<T> metric, CancellationToken cancellationToken)
    {
        var (expectedStorage, actualStorage) = GetStorages(expected, actual, cancellationToken);
        using var leases = PixelLeasePair.Acquire(expectedStorage, actualStorage);
        using var expectedReader = Rgba32RowReader.Create(expectedStorage.Owner.Scope, expected.PixelFormat, expectedStorage.Width);
        using var actualReader = Rgba32RowReader.Create(actualStorage.Owner.Scope, actual.PixelFormat, actualStorage.Width);
        var expectedSource = new PixelSource(leases.First, expectedReader, expected.PixelFormat);
        var actualSource = new PixelSource(leases.Second, actualReader, actual.PixelFormat);
        return metric(expectedStorage.Owner.Scope, expectedSource, actualSource, GetMaxDegreeOfParallelism(expected), cancellationToken);
    }

    /// <summary>Gets the largest number of workers of a comparison: that of the configuration of the image of the expected frame.</summary>
    private static int GetMaxDegreeOfParallelism(ImageFrame expected) => expected.OwnerImage.Configuration.MaxDegreeOfParallelism;

    private static (PixelStorage Expected, PixelStorage Actual) GetStorages(ImageFrame expected, ImageFrame actual, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        var expectedStorage = expected.GetStorage();
        var actualStorage = actual.GetStorage();
        if (expectedStorage.Width != actualStorage.Width || expectedStorage.Height != actualStorage.Height)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The frames do not have the same size: the expected frame is {expectedStorage.Width}x{expectedStorage.Height} and the actual frame is {actualStorage.Width}x{actualStorage.Height}."), nameof(actual));

        cancellationToken.ThrowIfCancellationRequested();
        return (expectedStorage, actualStorage);
    }

    private delegate T PairMetric<T>(AllocationScope scope, PixelSource expected, PixelSource actual, int maxDegreeOfParallelism, CancellationToken cancellationToken);
}
