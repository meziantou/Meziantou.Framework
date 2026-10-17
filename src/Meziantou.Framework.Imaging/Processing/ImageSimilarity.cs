using System.Globalization;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// Measures how similar two frames look: the Structural Similarity Index (SSIM), and the distance between two 64-bit
/// perceptual hashes, the difference hash (dHash) and the perceptual hash (pHash).
/// </summary>
/// <remarks>
/// <para>
/// Every member reads the pixels as 8-bit RGBA, whatever the pixel format, with the conversion of
/// <see cref="Image.CloneAs{TPixel}"/>: 16-bit samples are rounded to 8 bits, gray samples are copied to the three color
/// channels, and formats without alpha are opaque. Two frames of different pixel formats can be compared. Nothing is
/// written to the pixels; the frames are leased for the duration of the call, so a frame cannot be measured while a lease is
/// active on it. A frame can be compared with itself.
/// </para>
/// <para>
/// The results are deterministic: they depend only on the pixels, not on the hardware. The temporary row buffers are
/// charged to the allocation scope of the image of the frames. An animated image is compared frame by frame.
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
    public static double ComputeSsim(ImageFrame expected, ImageFrame actual, CancellationToken cancellationToken = default)
    {
        var (expectedStorage, actualStorage) = GetStorages(expected, actual, cancellationToken);
        using var leases = PixelLeasePair.Acquire(expectedStorage, actualStorage);
        using var expectedReader = Rgba32RowReader.Create(expectedStorage.Owner.Scope, expected.PixelFormat, expectedStorage.Width);
        using var actualReader = Rgba32RowReader.Create(actualStorage.Owner.Scope, actual.PixelFormat, actualStorage.Width);
        using var accumulator = new SsimAccumulator(expectedStorage.Owner.Scope, expectedStorage.Width, expectedStorage.Height);
        var expectedLease = leases.First;
        var actualLease = leases.Second;
        for (var y = 0; y < expectedStorage.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            accumulator.AddRow(expectedReader.GetRow(expectedLease, y), actualReader.GetRow(actualLease, y));
        }

        return accumulator.ComputeMeanSsim();
    }

    /// <summary>Computes the 64-bit difference hash (dHash) of a frame composited over a black background.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <returns>The hash. Compare hashes with <see cref="System.Numerics.BitOperations.PopCount(ulong)"/> of their exclusive or.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A lease is active on the frame.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The row buffer exceeds the allocation limit.</exception>
    public static ulong ComputeDifferenceHash(ImageFrame frame, CancellationToken cancellationToken = default) => ComputeHash(frame, perceptual: false, cancellationToken);

    /// <summary>Computes the 64-bit perceptual hash (pHash) of a frame composited over a black background.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <returns>The hash. Compare hashes with <see cref="System.Numerics.BitOperations.PopCount(ulong)"/> of their exclusive or.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A lease is active on the frame.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    /// <exception cref="ImageResourceLimitException">The row buffer exceeds the allocation limit.</exception>
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
}
