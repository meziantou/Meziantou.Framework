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

    /// <summary>
    /// Applies a custom processor to every displayed frame and to the poster frame, transactionally: each frame is replaced by
    /// a frame of the size the processor returns, computed from the whole frame before the operation.
    /// </summary>
    /// <typeparam name="TPixel">
    /// The pixel type of the frames the processor receives: the pixel type of the image, <see cref="Rgba64"/> for an image of
    /// an integer format, or <see cref="RgbaFloat"/> for any image. The frames of the image are converted to it without loss.
    /// </typeparam>
    /// <typeparam name="TProcessor">The type of the processor.</typeparam>
    /// <param name="image">The image to modify.</param>
    /// <param name="processor">The processor.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests, checked between frames and once more before the new frames replace the old ones.</param>
    /// <remarks>
    /// <para>
    /// Like the built-in geometry operations (crop, resize, rotate, transform), the new frames are allocated and computed
    /// before they replace the old ones: when the processor throws, a limit is exceeded or the operation is canceled, the
    /// size, the pixels, the frames and the metadata of the image are left unchanged. The new frames are charged to
    /// <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/> together with the old ones, and the canvas to the size
    /// limits. When <typeparamref name="TPixel"/> is not the pixel type of the image, a working copy of the source frame and
    /// a working destination (one frame at a time) are charged too, and the destination is stored back in the pixel format
    /// of the image like the rows of
    /// <see cref="ApplyRowOperation{TPixel, TOperation}(Image, TOperation, CancellationToken)"/>: floating-point samples
    /// outside the range of an integer format are clipped, and the alpha of a format without alpha must stay opaque.
    /// </para>
    /// <para>
    /// The EXIF dimensions are updated and the EXIF thumbnail removed. The cursor hotspot of a frame moves to the pixel
    /// <see cref="IImageProcessor{TPixel}.TryMapPoint"/> gives; the operation fails when that pixel is not part of the result.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Processing.Tests/ProcessingTests.cs" ref:region="ApplyProcessor_Mirror" ref:dedent="true">
    /// [Fact]
    /// public void ApplyProcessor_Mirror()
    /// {
    ///     // A 3x2 image: 1 2 3 / 4 5 6
    ///     using var image = new Image&lt;Gray8&gt;(3, 2);
    ///     for (var i = 0; i &lt; 6; i++)
    ///     {
    ///         image.Frames[0][i % 3, i / 3] = new Gray8((byte)(i + 1));
    ///     }
    ///
    ///     // The processor transposes every frame: the 3x2 canvas becomes 2x3, and the result replaces the frames at once
    ///     image.ApplyProcessor&lt;Gray8, TransposeProcessor&lt;Gray8&gt;&gt;(default);
    ///
    ///     Assert.Equal(new Size(2, 3), image.Size);
    ///     Assert.Equal(new Gray8(4), image.Frames[0][1, 0]);
    ///     Assert.Equal(new Gray8(3), image.Frames[0][0, 2]);
    /// }
    ///
    /// /// &lt;summary&gt;Swaps the rows and the columns of every frame.&lt;/summary&gt;
    /// public readonly struct TransposeProcessor&lt;TPixel&gt; : IImageProcessor&lt;TPixel&gt;
    ///     where TPixel : unmanaged
    /// {
    ///     public Size GetOutputSize(Size size) =&gt; new(size.Height, size.Width);
    ///
    ///     public void Process(int frameIndex, PixelRowSource&lt;TPixel&gt; source, PixelAccessor&lt;TPixel&gt; destination, CancellationToken cancellationToken)
    ///     {
    ///         for (var y = 0; y &lt; destination.Height; y++)
    ///         {
    ///             var row = destination.GetRowSpan(y);
    ///             for (var x = 0; x &lt; row.Length; x++)
    ///             {
    ///                 row[x] = source.GetRow(x)[y];
    ///             }
    ///         }
    ///     }
    ///
    ///     // The hotspot of a cursor follows its pixel
    ///     public bool TryMapPoint(Point source, Size sourceSize, Size destinationSize, out Point destination)
    ///     {
    ///         destination = new Point(source.Y, source.X);
    ///         return true;
    ///     }
    /// }
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="processor"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct, or cannot hold the samples of the image. Nothing is changed.</exception>
    /// <exception cref="InvalidOperationException">The processor returned an empty size, or a pixel lease is active. Nothing is changed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The processor produced a frame that the pixel format of the image cannot store, or the cursor hotspot of a frame is not part of the result. The image is unchanged.</exception>
    /// <exception cref="ImageResourceLimitException">The new canvas exceeds the size limits, or the new frames and the working storages exceed the allocation limit. The image is unchanged.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The image is unchanged.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public static void ApplyProcessor<TPixel, TProcessor>(this Image image, TProcessor processor, CancellationToken cancellationToken = default)
        where TPixel : unmanaged
        where TProcessor : IImageProcessor<TPixel>
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(processor);
        image.ApplyProcessorFrames<TPixel, TProcessor>(processor, "apply a processor", cancellationToken);
    }
}
