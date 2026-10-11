using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The <see cref="DecodedFrameSink"/> of sequential readers: each image is written into the target
/// of the current reader call and handed over at <see cref="DecodedFrameSink.EndImage"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// <c>ReadFrame</c>/<c>ReadPosterFrame</c>: a new single-frame still image created in the reader scope (it stays charged to it
/// until the caller disposes it, even after the reader is disposed), with the frame duration and the image-wide metadata,
/// without animation settings.
/// </description></item>
/// <item><description>
/// <c>ReadFrameInto</c>: the caller's destination, whose pixel storage is reused (it stays charged to its own scope); its
/// duration and hotspot are set when the frame is bound and its metadata when the frame ends. On failure it may be partially updated
/// but stays structurally valid. At the clean end of input it is never touched.
/// </description></item>
/// <item><description>A poster met while a displayed frame is requested is decoded into a scratch image and discarded.</description></item>
/// </list>
/// The target is bound lazily, on the first row access or at the end of the image, so decoders may begin an image in their
/// header callback; writing pixels before the reader requested an image is a decoder bug.
/// </remarks>
internal sealed class SequentialFrameSink : DecodedFrameSink
{
    private readonly SequentialDecodeSession _session;
    private ImageMetadata _metadata;
    private bool _imageOpen;
    private bool _isPoster;
    private bool _bound;
    private bool _discard;
    private FrameDuration _duration;
    private Point? _hotspot;
    private Image? _ownedImage;
    private Image? _destination;
    private ImageFrame? _target;

    public SequentialFrameSink(SequentialDecodeSession session, ImageCodecContext context, ImageDecodeRequest request, ImageFormat format, Size canvas, PixelFormat defaultPixelFormat, PixelFormat sourcePixelFormat, IccProfile? iccProfile)
        : base(context, request, format, canvas, defaultPixelFormat, sourcePixelFormat, iccProfile)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _metadata = CreateImageMetadata(session.HeaderMetadata);
    }

    public override void UpdateMetadata(ImageMetadata metadata)
    {
        base.UpdateMetadata(metadata);
        _metadata = CreateImageMetadata(metadata);
    }

    public override Image Build(ImageMetadata metadata, AnimationMetadata? animation)
        => throw new InvalidOperationException("A sequential reader returns its frames one by one; there is no image to build.");

    private protected override void OnBeginFrame(FrameDuration duration, Point? hotspot) => OpenImage(isPoster: false, duration, hotspot);

    private protected override void OnBeginPoster() => OpenImage(isPoster: true, FrameDuration.Zero, hotspot: null);

    private protected override ImageFrame GetCurrentFrame()
    {
        Bind();
        return _target!;
    }

    private protected override void OnEndImage()
    {
        if (!_imageOpen)
            throw new InvalidOperationException("No frame was started.");

        Bind();
        var image = _destination ?? _ownedImage!;
        var isDestination = _destination is not null;
        var discard = _discard;
        _ownedImage = null;
        _destination = null;
        _target = null;
        _imageOpen = false;
        _bound = false;
        _discard = false;
        if (discard)
        {
            // A poster skipped by the caller: decoded (and validated) but never returned
            image.Dispose();
            return;
        }

        image.CopyImageStateFrom(_metadata.Clone(), animation: null);
        _session.OnImageCompleted(image, _isPoster, isDestination);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // A partially decoded owned image (the caller's destination is never disposed)
            _ownedImage?.Dispose();
            _ownedImage = null;
            _destination = null;
            _target = null;
        }

        base.Dispose(disposing);
    }

    private void OpenImage(bool isPoster, FrameDuration duration, Point? hotspot)
    {
        _session.EnsureNoPendingEvent();
        if (_imageOpen)
        {
            // The decoder began an image without ending the previous one: release it, the previous image is incomplete
            _ownedImage?.Dispose();
            _ownedImage = null;
            _destination = null;
            _target = null;
        }

        _imageOpen = true;
        _isPoster = isPoster;
        _duration = duration;
        _hotspot = hotspot;
        _bound = false;
        _discard = false;
    }

    private void Bind()
    {
        if (_bound)
            return;

        if (!_imageOpen)
            throw new InvalidOperationException("No frame was started.");

        switch (_session.Request)
        {
            case SequentialRequest.None:
                throw new InvalidOperationException("The decoder produced pixels before the reader requested an image: the container walker must yield after the header.");

            case SequentialRequest.Poster when !_isPoster:
                throw new InvalidOperationException("The header announced a separate poster frame, but the decoder produced a displayed frame first.");

            case SequentialRequest.Frame when !_isPoster && _session.Destination is { } destination:
                _destination = destination;
                _target = Image.GetFrameForDecoder(destination, 0);
                break;

            default:
                _discard = _isPoster && _session.Request == SequentialRequest.Frame;
                _ownedImage = Image.CreateForDecoder(DestinationPixelFormat, Context.Configuration, Canvas, Context.Scope);
                _target = Image.GetFrameForDecoder(_ownedImage, 0);
                break;
        }

        // Always assigned: a destination reused by ReadFrameInto must not keep the hotspot of its previous content
        _target.MetadataCore.Duration = _duration;
        _target.MetadataCore.SetHotspotUnchecked(_hotspot);
        _bound = true;
    }
}
