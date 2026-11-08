namespace Meziantou.Framework.Imaging;

public static partial class ImageProcessingExtensions
{
    /// <summary>Applies a custom row operation to every displayed frame and to the poster frame, in place.</summary>
    /// <typeparam name="TPixel">
    /// The pixel type of the rows the operation receives: the pixel type of the image, <see cref="Rgba64"/> for an image of
    /// an integer format, or <see cref="RgbaFloat"/> for any image. The rows of the image are converted to it without loss.
    /// </typeparam>
    /// <typeparam name="TOperation">The type of the operation. A <see langword="struct"/> avoids an interface call per row.</typeparam>
    /// <param name="image">The image to modify.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests, checked before each row. On cancellation, some rows may already be modified.</param>
    /// <remarks>
    /// <para>
    /// The rows of a large frame (at least 65,536 pixels and 32 rows per worker) are split into bands processed by up to
    /// <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> workers, which call the operation concurrently. When
    /// <typeparamref name="TPixel"/> is not the pixel type of the image, each worker converts a row to it, calls the
    /// operation, and stores the row back in the pixel format of the image: floating-point samples outside the range of an
    /// integer format are clipped, and the alpha of a format without alpha must stay opaque. The row buffers are charged to
    /// <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/>.
    /// </para>
    /// <para>Like the built-in pixel operations, it keeps the size, the frames and the metadata, and removes a stale EXIF thumbnail.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct, or cannot hold the samples of the image. Nothing is changed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The operation produced a row that the pixel format of the image cannot store: a sample that is not a number, or a transparent pixel in a format without alpha.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void ApplyRowOperation<TPixel, TOperation>(this Image image, TOperation operation, CancellationToken cancellationToken = default)
        where TPixel : unmanaged
        where TOperation : IPixelRowOperation<TPixel>
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(operation);
        image.ApplyRowOperationFrames<TPixel, TOperation>(operation, cancellationToken);
    }

    /// <summary>
    /// Applies a custom neighborhood operation to every displayed frame and to the poster frame: each row is computed from
    /// the pixels of the frame before the operation.
    /// </summary>
    /// <typeparam name="TPixel">
    /// The pixel type of the rows the operation receives: the pixel type of the image, <see cref="Rgba64"/> for an image of
    /// an integer format, or <see cref="RgbaFloat"/> for any image. The rows of the image are converted to it without loss.
    /// </typeparam>
    /// <typeparam name="TOperation">The type of the operation. A <see langword="struct"/> avoids an interface call per row.</typeparam>
    /// <param name="image">The image to modify.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests, checked before each row. On cancellation, some rows may already be modified.</param>
    /// <remarks>
    /// <para>
    /// Each frame is copied, in the pixel format of <typeparamref name="TPixel"/>, to a working image of the size of the
    /// canvas charged to <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/> (one at a time), which
    /// <see cref="PixelRowSource{TPixel}"/> reads. The rows are then computed like those of
    /// <see cref="ApplyRowOperation{TPixel, TOperation}(Image, TOperation, CancellationToken)"/>: by bands of up to
    /// <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> workers, and stored back with the same rules.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct, or cannot hold the samples of the image. Nothing is changed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The operation produced a row that the pixel format of the image cannot store.</exception>
    /// <exception cref="ImageResourceLimitException">The working image exceeds the allocation limit. Nothing is changed.</exception>
    /// <exception cref="InvalidOperationException">A pixel lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void ApplyNeighborhoodOperation<TPixel, TOperation>(this Image image, TOperation operation, CancellationToken cancellationToken = default)
        where TPixel : unmanaged
        where TOperation : IPixelNeighborhoodOperation<TPixel>
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(operation);
        image.ApplyNeighborhoodOperationFrames<TPixel, TOperation>(operation, cancellationToken);
    }
}
