namespace Meziantou.Framework.Imaging;

/// <summary>Immutable configuration shared by an image and the operations performed on it.</summary>
/// <remarks>
/// An image captures the configuration it was created or loaded with; operations on the image use that snapshot.
/// The configuration is immutable, so a single instance can be shared freely across threads.
/// </remarks>
public sealed class ImageConfiguration
{
    /// <summary>Gets the default configuration: <see cref="ImageResourceLimits.Default"/> limits and one worker.</summary>
    public static ImageConfiguration Default { get; } = new();

    /// <summary>Gets the resource limits. Defaults to <see cref="ImageResourceLimits.Default"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public ImageResourceLimits Limits
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = ImageResourceLimits.Default;

    /// <summary>Gets the environment of the fonts loaded with this configuration.</summary>
    internal Internals.ImageFontEnvironment FontEnvironment => field ??= new Internals.ImageFontEnvironment(this);

    /// <summary>
    /// Gets the maximum number of workers an internal operation may use concurrently. Defaults to 1, meaning operations run on
    /// the calling thread only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Currently, <see cref="ImageProcessingExtensions.Resize"/>, the transformations,
    /// <see cref="ImageProcessingExtensions.Convolve(Image, ConvolutionOptions, CancellationToken)"/>, the blurs
    /// (<see cref="ImageProcessingExtensions.GaussianBlur(Image, double, ConvolutionEdgeMode, ConvolutionWorkingSpace, CancellationToken)"/>,
    /// <see cref="ImageProcessingExtensions.BoxBlur(Image, int, ConvolutionEdgeMode, ConvolutionWorkingSpace, CancellationToken)"/>,
    /// <see cref="ImageProcessingExtensions.MedianBlur(Image, int, ConvolutionEdgeMode, CancellationToken)"/>),
    /// <see cref="ImageProcessingExtensions.Sharpen(Image, double, double, double, ConvolutionEdgeMode, ConvolutionWorkingSpace, CancellationToken)"/> and
    /// <see cref="ImageProcessingExtensions.DetectEdges(Image, EdgeDetector, bool, ConvolutionEdgeMode, CancellationToken)"/> use it: the output rows
    /// of a large frame (at least 65,536 output pixels and 32 rows per worker; nearest-neighbor resizing excepted) are split
    /// into at most this many bands processed concurrently, the calling thread included. The result is identical whatever
    /// the value. Each worker needs its own row scratch, charged to the image's
    /// <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/> budget. The HEIC and AVIF decoders use it too: the tiles of a
    /// grid of at least 65,536 samples are decoded by up to this many workers, each holding the decoder and the picture of its
    /// tile, with the pixels and the errors of a decoding by one worker. The HEIC and AVIF encoders code the tiles of such a
    /// grid (<see cref="Formats.HeicEncoder.GridTileSize"/>, <see cref="Formats.AvifEncoder.GridTileSize"/>) by up to this
    /// many workers, each with encoders of its own, and write the same bytes as one worker. The AVIF decoder also runs the
    /// post-processing filters of an AV1 frame of at least 65,536 pixels (the loop filter, CDEF, the super-resolution
    /// upscaling and the loop restoration) by bands of rows, with the pixels of a decoding by one worker, unless the frame is
    /// a tile decoded alongside other tiles. They take about 25% to 40% of the decoding time of a 3840x2160 image that uses
    /// CDEF, which then decodes about 1.3 to 1.45 times faster with 8 workers. Each worker needs its own rows, about 140
    /// bytes per pixel of the width of the frame, charged to <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/>. The
    /// other decoders (the HEVC in-loop filters of a HEIC image that is not a grid included), the encoders and the other
    /// operations run on the calling thread.
    /// </para>
    /// <para>
    /// Keep the default in services that already process several images concurrently: the bound applies per operation, so
    /// <c>n</c> concurrent operations may use up to <c>n</c> times this many threads.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxDegreeOfParallelism
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 1;
}
