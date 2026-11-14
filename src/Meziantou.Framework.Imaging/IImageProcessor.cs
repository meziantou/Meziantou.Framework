namespace Meziantou.Framework.Imaging;

/// <summary>
/// A custom image processor that computes each frame of a new canvas, possibly of another size, from the whole frame
/// before the operation (a crop, a warp, a content-aware resize, ...), applied to every frame of an image transactionally
/// by <see cref="ImageProcessingExtensions.ApplyProcessor{TPixel, TProcessor}(Image, TProcessor, CancellationToken)"/>.
/// </summary>
/// <typeparam name="TPixel">The pixel type of the frames: the pixel type of the image, or a wider working type.</typeparam>
/// <remarks>
/// <para>
/// <see cref="GetOutputSize"/> is called once, before anything is allocated; <see cref="Process"/> is then called once per
/// frame, on the calling thread, for every displayed frame then the poster frame. The new frames replace the old ones only
/// after every frame was processed: when a call throws (or the operation is canceled), the image is left unchanged.
/// </para>
/// <para>
/// Implement the processor as a <see langword="struct"/> or a <see langword="class"/>: the processing loop is compiled for
/// its type, without reflection.
/// </para>
/// </remarks>
public interface IImageProcessor<TPixel>
    where TPixel : unmanaged
{
    /// <summary>Gets the size of the new canvas.</summary>
    /// <param name="size">The size of the canvas of the image.</param>
    /// <returns>The size of every frame of the result. It must not be empty.</returns>
    Size GetOutputSize(Size size);

    /// <summary>Computes one frame of the result.</summary>
    /// <param name="frameIndex">The index of the frame: <c>0</c> to <c>Frames.Count - 1</c> for the displayed frames, and <c>Frames.Count</c> for the poster frame.</param>
    /// <param name="source">The pixels of the frame before the operation. Valid until the method returns.</param>
    /// <param name="destination">
    /// The pixels of the new frame, of the size <see cref="GetOutputSize"/> returned. It initially holds a cleared frame:
    /// transparent black, or opaque black in a pixel format without alpha. Valid until the method returns.
    /// </param>
    /// <param name="cancellationToken">The token of the operation.</param>
    void Process(int frameIndex, PixelRowSource<TPixel> source, PixelAccessor<TPixel> destination, CancellationToken cancellationToken);

    /// <summary>
    /// Gets where a pixel of the canvas lands on the new canvas: the new position of the cursor hotspot of a frame
    /// (<see cref="Metadata.FrameMetadata.Hotspot"/>). The default implementation keeps a pixel where it is when it is
    /// inside the new canvas.
    /// </summary>
    /// <param name="source">The pixel on the canvas of the image.</param>
    /// <param name="sourceSize">The size of the canvas of the image.</param>
    /// <param name="destinationSize">The size of the new canvas.</param>
    /// <param name="destination">The pixel on the new canvas.</param>
    /// <returns><see langword="false"/> when the pixel is not part of the result: the operation then fails for a frame whose hotspot designates it.</returns>
    bool TryMapPoint(Point source, Size sourceSize, Size destinationSize, out Point destination)
    {
        destination = source;
        return source.X < destinationSize.Width && source.Y < destinationSize.Height;
    }
}
