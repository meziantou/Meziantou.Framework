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

    /// <summary>
    /// Gets the maximum number of workers an internal operation may use concurrently. Defaults to 1, meaning operations run on
    /// the calling thread only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Currently, <see cref="ImageProcessingExtensions.Resize"/> and
    /// <see cref="ImageProcessingExtensions.Convolve(Image, ConvolutionOptions, CancellationToken)"/> use it: the output rows
    /// of a large frame (at least 65,536 output pixels and 32 rows per worker; nearest-neighbor resizing excepted) are split
    /// into at most this many bands processed concurrently, the calling thread included. The result is identical whatever
    /// the value. Each worker needs its own row scratch, charged to the image's
    /// <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/> budget. Decoding, encoding and the other operations run on the
    /// calling thread.
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
