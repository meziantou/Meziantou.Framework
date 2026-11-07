using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The <see cref="DecodedFrameSink"/> of eager loads, shared by every decoder: it
/// appends every displayed frame (and the poster) to one image and writes the decoder rows directly into the requested
/// representation, row by row, without an intermediate full image.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>The destination is the requested pixel format (typed loads) or the default working representation.</description></item>
/// <item><description>
/// The conversion policy is validated before any pixel is allocated (<see cref="PixelConversionPlan"/>: color profile rule,
/// background), and alpha is never discarded silently: a non-opaque row converted to a format without alpha and without a
/// background throws <see cref="UnsupportedImageFeatureException"/>. For untyped loads, a profile that cannot label the
/// default representation (a grayscale profile on gray+alpha pixels decoded to RGBA) is dropped, never applied.
/// </description></item>
/// <item><description>
/// Every frame (and the poster) is charged to the per-input tracker before it is allocated (frames, cumulative displayed
/// pixels); pixel storage is charged to the operation scope, which the returned image keeps.
/// </description></item>
/// <item><description>
/// <see cref="ImageDecodeRequest.FrameLimit"/> is exposed as <see cref="DecodedFrameSink.IsFrameLimitReached"/> and through
/// the result of <see cref="DecodedFrameSink.EndImage"/>: decoders stop after the requested number of displayed frames
/// without examining the rest of the data. The poster is never counted.
/// </description></item>
/// <item><description>On failure, <see cref="DecodedFrameSink.Dispose()"/> releases the partially built image; nothing is leaked.</description></item>
/// </list>
/// Decoders should create it through <see cref="DecodedFrameSink.Create"/> so that the same decoder serves sequential readers.
/// </remarks>
internal sealed class DecodedImageBuilder : DecodedFrameSink
{
    private Image? _image;
    private ImageFrame? _current;
    private bool _firstFrameUsed;
    private bool _built;

    public DecodedImageBuilder(ImageCodecContext context, ImageDecodeRequest request, ImageFormat format, Size canvas, PixelFormat defaultPixelFormat, PixelFormat sourcePixelFormat, IccProfile? iccProfile)
        : base(context, request, format, canvas, defaultPixelFormat, sourcePixelFormat, iccProfile)
    {
        if (context.Sequential is not null)
            throw new InvalidOperationException("A sequential reader decodes frames one by one: create the decoder sink with DecodedFrameSink.Create.");
    }

    public override Image Build(ImageMetadata metadata, AnimationMetadata? animation)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (_image is null || !_firstFrameUsed)
            throw new InvalidOperationException("The decoder produced no displayed frame.");

        _image.CopyImageStateFrom(CreateImageMetadata(metadata), animation);
        _built = true;
        _current = null;
        return _image;
    }

    public override void CopyFromFrame(int frameIndex)
    {
        var current = GetCurrentFrame();

        // The count includes the current frame, which is the last one
        if ((uint)frameIndex >= (uint)(FrameCount - 1))
            throw new ArgumentOutOfRangeException(nameof(frameIndex), frameIndex, "Only an earlier displayed frame can be copied.");

        var source = Image.GetFrameForDecoder(_image!, frameIndex);
        using var leases = PixelLeasePair.Acquire(source.GetStorage(), current.GetStorage());
        leases.First.CopyTo(leases.Second);
    }

    private protected override void OnBeginFrame(FrameDuration duration, Point? hotspot)
    {
        var image = EnsureImage();
        ImageFrame frame;
        if (!_firstFrameUsed)
        {
            frame = Image.GetFrameForDecoder(image, 0);
            _firstFrameUsed = true;
        }
        else
        {
            frame = Image.AppendFrameForDecoder(image);
        }

        frame.MetadataCore.Duration = duration;
        frame.MetadataCore.SetHotspotUnchecked(hotspot);
        _current = frame;
    }

    private protected override void OnBeginPoster() => _current = Image.AttachPosterForDecoder(EnsureImage());

    private protected override ImageFrame GetCurrentFrame() => _current ?? throw new InvalidOperationException("No frame was started.");

    private protected override void OnEndImage()
    {
        if (_current is null)
            throw new InvalidOperationException("No frame was started.");

        _current = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_built)
        {
            _image?.Dispose();
        }

        _image = null;
        _current = null;
        base.Dispose(disposing);
    }

    private Image EnsureImage() => _image ??= Image.CreateForDecoder(DestinationPixelFormat, Context.Configuration, Canvas, Context.Scope);
}
