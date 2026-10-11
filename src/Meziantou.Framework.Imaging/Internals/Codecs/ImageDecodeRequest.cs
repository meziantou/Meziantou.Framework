namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What an eager load asks a decoder to produce.</summary>
/// <param name="PixelFormat">The requested pixel format (typed loads), or <see langword="null"/> for the default working representation of the file.</param>
/// <param name="FrameLimit">The maximum number of displayed frames (deliberate prefix selection), or <see langword="null"/> for all frames.</param>
/// <param name="Conversion">The conversion policy applied when the decoded samples are converted to <paramref name="PixelFormat"/>.</param>
internal sealed record ImageDecodeRequest(PixelFormat? PixelFormat, int? FrameLimit, PixelConversionOptions Conversion)
{
    /// <summary>
    /// Gets a value indicating whether the decoded image is a payload embedded in the file being loaded (a frame of an
    /// animated cursor), whose displayed frames the outer decoder charges itself: the frames of this decode are then not
    /// charged to the per-input tracker a second time.
    /// </summary>
    public bool IsEmbeddedPayload { get; init; }


    public static ImageDecodeRequest Create(ImageDecodeOptions options, PixelFormat? pixelFormat)
        => new(pixelFormat, options.FrameLimit, options.Conversion);
}
