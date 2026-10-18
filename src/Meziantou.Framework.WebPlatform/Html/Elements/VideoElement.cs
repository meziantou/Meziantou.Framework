using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Media;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Dom;

// The video element (https://html.spec.whatwg.org/multipage/media.html#the-video-element): its poster frame, its natural
// dimensions and what it represents (the poster frame or the frame of the video the player gives).
public partial class HtmlVideoElement
{
    /// <summary>The steps of the fetch of the poster frame deferred by <c>loading="lazy"</c> (https://html.spec.whatwg.org/multipage/media.html#poster-lazy-load-resumption-steps), or null.</summary>
    private Action? _posterLazyLoadResumptionSteps;
    private int _posterGeneration;

    /// <summary>
    /// Gets the natural width of the video in CSS pixels (https://html.spec.whatwg.org/multipage/media.html#dom-video-videowidth),
    /// or 0 when the dimensions are not known (the ready state is <c>HAVE_NOTHING</c>, or the resource has no video).
    /// </summary>
    [IdlMember("videoWidth", "https://html.spec.whatwg.org/multipage/media.html#dom-video-videowidth")]
    public uint VideoWidth => ReadyState == HaveNothing ? 0 : (uint)MediaVideoWidth;

    /// <summary>
    /// Gets the natural height of the video in CSS pixels (https://html.spec.whatwg.org/multipage/media.html#dom-video-videoheight),
    /// or 0 when the dimensions are not known (the ready state is <c>HAVE_NOTHING</c>, or the resource has no video).
    /// </summary>
    [IdlMember("videoHeight", "https://html.spec.whatwg.org/multipage/media.html#dom-video-videoheight")]
    public uint VideoHeight => ReadyState == HaveNothing ? 0 : (uint)MediaVideoHeight;

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is null && localName == "poster")
        {
            FetchPosterFrame();
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/media.html#attr-video-poster: when the poster attribute is set, changed or
    /// removed, the poster frame is determined. The fetch of the image is deferred while the <c>loading</c> attribute is in
    /// the Lazy state (until the element intersects the viewport, or the attribute changes).
    /// </summary>
    private void FetchPosterFrame()
    {
        var document = NodeDocument;
        var generation = ++_posterGeneration;
        _posterLazyLoadResumptionSteps = null;
        if (PosterNaturalSize is not null)
        {
            PosterNaturalSize = null;
            PosterImage = null;
            document.InvalidateLayout();
        }

        if (GetAttributeValue("poster") is not { Length: > 0 } poster || Reflection.EncodingParseUrl(document, poster) is not { } url)
            return;

        if (LazyLoading.WillLazyLoad(this))
        {
            _posterLazyLoadResumptionSteps = Fetch;
            LazyLoading.StartIntersectionObserving(this);
            return;
        }

        Fetch();

        void Fetch()
        {
            if (generation != _posterGeneration)
                return;

            SubresourceFetching.Fetch(document, SubresourceFetching.CreateRequest(document, url, RequestDestination.Image, cors: false, referrerPolicy: "", this), resource =>
            {
                // https://html.spec.whatwg.org/multipage/media.html#poster-frame: the natural size of the video is the one of its poster frame
                if (generation == _posterGeneration && resource is not null && ImageHeaders.TryGetImageInfo(EmbeddedResources.GetBytes(resource), resource.MimeType?.Essence ?? "", out var naturalSize))
                {
                    PosterNaturalSize = naturalSize;
                    var bytes = resource.ContentBytes ?? System.Text.Encoding.UTF8.GetBytes(resource.Content ?? "");
                    PosterImage = NodeDocument.CreatePaintImage(url.Serialize(), naturalSize, bytes, resource.MimeType?.Essence);

                    // https://w3c.github.io/paint-timing/#sec-modifications-HTML: the poster frame is the associated image request
                    NodeDocument.ProcessImageThatFinishedLoading(this, url.Serialize(), naturalSize, bytes.Length);
                    NodeDocument.InvalidateLayout();
                }
            });
        }
    }

    /// <summary>
    /// The poster frame of an element created in a document where nothing is fetched (e.g. the contents of a <c>template</c>
    /// element) is determined when the element is adopted by another document, relative to its new document.
    /// </summary>
    internal override void AdoptingSteps(Document oldDocument)
    {
        base.AdoptingSteps(oldDocument);
        if (PosterNaturalSize is null && oldDocument.BrowsingContext is null && HasAttributeWithLocalName("poster"))
        {
            FetchPosterFrame();
        }
    }

    /// <summary>Runs the poster lazy load resumption steps, if any: the element intersects the viewport, or its <c>loading</c> attribute is no longer in the Lazy state.</summary>
    internal void RunPosterLazyLoadResumptionSteps()
    {
        if (_posterLazyLoadResumptionSteps is not { } steps)
            return;

        _posterLazyLoadResumptionSteps = null;
        steps();
    }

    /// <summary>Whether the fetch of the poster frame is deferred: the element stays observed by the lazy load intersection observer.</summary>
    internal bool HasPosterLazyLoadResumptionSteps => _posterLazyLoadResumptionSteps is not null;

    private protected override void OnLoadingAttributeChanged(string? oldValue, string? value)
    {
        if (LazyLoading.IsLazy(oldValue) && !LazyLoading.IsLazy(value))
        {
            RunPosterLazyLoadResumptionSteps();
        }
    }

    /// <summary>The natural size of the poster frame (https://html.spec.whatwg.org/multipage/media.html#poster-frame) once it is fetched, or null.</summary>
    internal ImageNaturalSize? PosterNaturalSize { get; private set; }

    /// <summary>The image of the poster frame painted in the box of the element (see <see cref="Document.CreatePaintImage"/>), or null.</summary>
    internal PaintImage? PosterImage { get; private set; }

    /// <summary>Whether video data is available: the ready state is at least <c>HAVE_CURRENT_DATA</c> and the media resource has a video channel.</summary>
    private bool HasVideoData => ReadyState >= HaveCurrentData && MediaVideoWidth > 0 && MediaVideoHeight > 0;

    /// <summary>
    /// Whether the element represents its poster frame (https://html.spec.whatwg.org/multipage/media.html#the-video-element):
    /// there is a poster frame, and no video data is available, or the show poster flag is set (the element is paused at
    /// the first frame of the video).
    /// </summary>
    private bool RepresentsPosterFrame => PosterNaturalSize is not null && (!HasVideoData || ShowPoster);

    /// <summary>
    /// The natural dimensions of the playback area of the element: the ones of the poster frame when the element represents
    /// it, else the ones of the video resource when they are available, else none (the default object size is 300x150).
    /// </summary>
    internal ImageNaturalSize GetNaturalSize()
    {
        if (RepresentsPosterFrame)
            return PosterNaturalSize!.Value;

        if (ReadyState != HaveNothing && MediaVideoWidth > 0 && MediaVideoHeight > 0)
            return ImageNaturalSize.FromPixels((uint)MediaVideoWidth, (uint)MediaVideoHeight);

        return default;
    }

    /// <summary>
    /// The image painted in the box of the element: the poster frame, or the frame of the video at the current playback
    /// position (<see cref="MediaPlayer.GetVideoFrame"/>), or null (transparent black).
    /// </summary>
    internal PaintImage? GetPaintImage()
    {
        if (RepresentsPosterFrame)
            return PosterImage;

        return GetVideoFrameImage();
    }

    /// <summary>The frame of the video at the current playback position as an image of the rendering backend, or null when there is no frame or no backend.</summary>
    internal PaintImage? GetVideoFrameImage()
    {
        if (!HasVideoData || Player?.GetVideoFrame() is not { } frame || NodeDocument.RenderingBackend is not { } backend)
            return null;

        if (frame.PaintImage is null || !ReferenceEquals(frame.PaintImageBackend, backend))
        {
            frame.PaintImageBackend = backend;
            frame.PaintImage = backend.CreateImage(frame.Width, frame.Height, frame.Pixels.Span) is { } image
                ? new PaintImage(CurrentSrc, backend, image) { IsCorsSameOrigin = IsMediaDataCorsSameOrigin }
                : null;
        }

        return frame.PaintImage;
    }

    /// <summary>The frame of the video at the current playback position (<c>createImageBitmap()</c>), or null.</summary>
    internal MediaVideoFrame? GetVideoFrame() => HasVideoData ? Player?.GetVideoFrame() : null;

    /// <summary>
    /// Returns the playback quality metrics of the video (https://w3c.github.io/media-playback-quality/#dom-htmlvideoelement-getvideoplaybackquality):
    /// the numbers of frames reported by the player of the media backend (<see cref="MediaPlayer.TotalVideoFrames"/>,
    /// <see cref="MediaPlayer.DroppedVideoFrames"/>; 0 without player), at the current time.
    /// </summary>
    [IdlMember("getVideoPlaybackQuality", "https://w3c.github.io/media-playback-quality/#dom-htmlvideoelement-getvideoplaybackquality")]
    public VideoPlaybackQuality GetVideoPlaybackQuality()
    {
        var total = Math.Max(Player?.TotalVideoFrames ?? 0, 0);
        var dropped = Math.Clamp(Player?.DroppedVideoFrames ?? 0, 0, total);
        return new VideoPlaybackQuality(RelevantGlobal?.Performance.Now() ?? 0, (uint)Math.Min(dropped, uint.MaxValue), (uint)Math.Min(total, uint.MaxValue));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The cues displayed over the video (https://w3c.github.io/webvtt/#rendering)
    // ----------------------------------------------------------------------------------------------------------------

    private WebVtt.WebVttDisplay? _cueDisplay;

    /// <summary>
    /// The display of the WebVTT text tracks of the element: the cues and the regions rendered over the video by the layout
    /// (see <see cref="Css.Layout.WebVttCueLayout"/>), or null when the element has no text track.
    /// </summary>
    internal WebVtt.WebVttDisplay? CueDisplay => HasTextTracks ? _cueDisplay ??= new WebVtt.WebVttDisplay(this) : _cueDisplay;

    /// <summary>
    /// The rules for updating the display of WebVTT text tracks (https://w3c.github.io/webvtt/#rules-for-updating-the-display-of-webvtt-text-tracks)
    /// are run by the next layout of the document, with the size of the rendering area of the video.
    /// </summary>
    internal override void OnCueRenderingChanged()
    {
        var document = NodeDocument;
        document.InvalidateLayout();
        document.OnCueRenderingChanged();
    }

    /// <summary>
    /// The current playback position changed: the :past and :future pseudo-classes of the WebVTT node objects of the cues that
    /// have timestamps may match differently (https://w3c.github.io/webvtt/#the-past-and-future-pseudo-classes).
    /// </summary>
    private protected override void OnCuePlaybackPositionChanged()
    {
        if (_cueDisplay is { HasTimestamps: true })
        {
            NodeDocument.IncrementElementStateVersion();
        }
    }

    /// <summary>The natural dimensions the layout of the document last used for the element.</summary>
    private ImageNaturalSize _renderedNaturalSize;

    /// <summary>What the element represents changed: the layout is updated when its natural dimensions change, else the element is painted again.</summary>
    private protected override void OnRenderingChanged()
    {
        var naturalSize = GetNaturalSize();
        if (naturalSize != _renderedNaturalSize)
        {
            _renderedNaturalSize = naturalSize;
            NodeDocument.InvalidateLayout();
        }
        else
        {
            NodeDocument.InvalidatePaint();
        }
    }
}

/// <summary>The playback quality metrics of a video element (https://w3c.github.io/media-playback-quality/#videoplaybackquality).</summary>
[IdlInterface("VideoPlaybackQuality", "https://w3c.github.io/media-playback-quality/#videoplaybackquality")]
public sealed class VideoPlaybackQuality
{
    internal VideoPlaybackQuality(double creationTime, uint droppedVideoFrames, uint totalVideoFrames)
    {
        CreationTime = creationTime;
        DroppedVideoFrames = droppedVideoFrames;
        TotalVideoFrames = totalVideoFrames;
    }

    /// <summary>Gets the time the object was created, in milliseconds since the time origin of the window.</summary>
    [IdlMember("creationTime", "https://w3c.github.io/media-playback-quality/#dom-videoplaybackquality-creationtime")]
    public double CreationTime { get; }

    /// <summary>Gets the number of video frames that were dropped instead of being displayed.</summary>
    [IdlMember("droppedVideoFrames", "https://w3c.github.io/media-playback-quality/#dom-videoplaybackquality-droppedvideoframes")]
    public uint DroppedVideoFrames { get; }

    /// <summary>Gets the number of video frames that were displayed or dropped.</summary>
    [IdlMember("totalVideoFrames", "https://w3c.github.io/media-playback-quality/#dom-videoplaybackquality-totalvideoframes")]
    public uint TotalVideoFrames { get; }

    /// <summary>Gets the number of corrupted video frames: always 0 (the attribute is deprecated).</summary>
    [IdlMember("corruptedVideoFrames", "https://w3c.github.io/media-playback-quality/#dom-videoplaybackquality-corruptedvideoframes")]
    public uint CorruptedVideoFrames => 0;
}
