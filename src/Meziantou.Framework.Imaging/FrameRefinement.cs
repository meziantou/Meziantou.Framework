namespace Meziantou.Framework.Imaging;

/// <summary>
/// Describes a rendering of a frame that <see cref="ImageReader{TPixel}.ReadFrameRefinementInto"/> wrote to an image: an
/// intermediate rendering, computed from the part of the frame that was received, or the frame itself.
/// </summary>
/// <remarks>
/// A format that stores an image at increasing levels of detail (a progressive JPEG XL file) gives intermediate renderings
/// before the frame; any other input gives the frame at once, with <see cref="IsFinal"/> set. The number of intermediate
/// renderings of a file and their pixels are not part of the contract of the library: only the final rendering is the
/// frame that <see cref="ImageReader{TPixel}.ReadFrameInto"/> returns.
/// </remarks>
public sealed class FrameRefinement
{
    internal FrameRefinement(bool isFinal, int downsampling, int completedPasses)
    {
        IsFinal = isFinal;
        Downsampling = downsampling;
        CompletedPasses = completedPasses;
    }

    /// <summary>
    /// Gets a value indicating whether the rendering is the frame itself: the image then holds what
    /// <see cref="ImageReader{TPixel}.ReadFrameInto"/> gives, and the next read starts the next frame.
    /// </summary>
    public bool IsFinal { get; }

    /// <summary>
    /// Gets the factor by which the detail of the rendering is reduced in each direction, as the file declares it: 8 for a
    /// rendering that only has one value per block of 8x8 pixels, then 4, 2, and 1 for the frame itself. The rendering always
    /// has the size of the frame.
    /// </summary>
    public int Downsampling { get; }

    /// <summary>Gets the number of passes of the frame that the rendering holds: 0 for a rendering that has none of them yet.</summary>
    public int CompletedPasses { get; }
}
