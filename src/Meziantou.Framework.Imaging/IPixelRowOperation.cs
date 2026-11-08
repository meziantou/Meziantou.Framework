namespace Meziantou.Framework.Imaging;

/// <summary>
/// A custom image processor that edits each row of pixels in place, applied to every frame of an image by
/// <see cref="ImageProcessingExtensions.ApplyRowOperation{TPixel, TOperation}(Image, TOperation, CancellationToken)"/>.
/// </summary>
/// <typeparam name="TPixel">The pixel type of the rows: the pixel type of the image, or a wider working type.</typeparam>
/// <remarks>
/// Implement the operation as a <see langword="struct"/>: the processing loop is then compiled for it, without an
/// interface call per row. <see cref="Invoke"/> is called from several threads at once when
/// <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> is greater than 1, for different rows of the same instance: it
/// must not change the state of the operation.
/// </remarks>
public interface IPixelRowOperation<TPixel>
    where TPixel : unmanaged
{
    /// <summary>Edits one row.</summary>
    /// <param name="y">The index of the row in the frame.</param>
    /// <param name="row">The pixels of the row, read and written in place. Valid until the method returns.</param>
    void Invoke(int y, Span<TPixel> row);
}
