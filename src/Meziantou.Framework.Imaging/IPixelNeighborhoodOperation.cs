namespace Meziantou.Framework.Imaging;

/// <summary>
/// A custom image processor that computes each row from the unmodified pixels of the frame (a neighborhood filter, such as
/// a blur or an edge detector), applied to every frame of an image by
/// <see cref="ImageProcessingExtensions.ApplyNeighborhoodOperation{TPixel, TOperation}(Image, TOperation, CancellationToken)"/>.
/// </summary>
/// <typeparam name="TPixel">The pixel type of the rows: the pixel type of the image, or a wider working type.</typeparam>
/// <remarks>
/// Implement the operation as a <see langword="struct"/>: the processing loop is then compiled for it, without an
/// interface call per row. <see cref="Invoke"/> is called from several threads at once when
/// <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> is greater than 1, for different rows of the same instance: it
/// must not change the state of the operation.
/// </remarks>
public interface IPixelNeighborhoodOperation<TPixel>
    where TPixel : unmanaged
{
    /// <summary>Computes one row.</summary>
    /// <param name="y">The index of the row in the frame.</param>
    /// <param name="source">The pixels of the frame before the operation, in rows. Valid until the method returns.</param>
    /// <param name="destination">The row to write. It initially holds the pixels of the row before the operation. Valid until the method returns.</param>
    void Invoke(int y, PixelRowSource<TPixel> source, Span<TPixel> destination);
}
