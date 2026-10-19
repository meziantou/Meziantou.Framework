using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The state of an image request (https://html.spec.whatwg.org/multipage/images.html#img-req-state).</summary>
internal enum ImageRequestState
{
    Unavailable,
    PartiallyAvailable,
    CompletelyAvailable,
    Broken,
}

/// <summary>
/// The natural dimensions of an image (https://drafts.csswg.org/css-images/#natural-dimensions): its natural width, its
/// natural height and its natural aspect ratio (width / height), each of them may be absent.
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct ImageNaturalSize(double? Width, double? Height, double? AspectRatio)
{
    /// <summary>The natural size of a raster image.</summary>
    public static ImageNaturalSize FromPixels(uint width, uint height) => new(width, height, height == 0 ? null : (double)width / height);

    /// <summary>
    /// The natural size of an encoded raster image whose metadata is in EXIF data (a JPEG or PNG image): its density-corrected
    /// dimensions when its EXIF resolution gives them (<see cref="ImageMetadata.GetDensityCorrectedSize"/>), rotated by its
    /// EXIF orientation.
    /// </summary>
    public static ImageNaturalSize FromEncodedPixels(ReadOnlySpan<byte> bytes, uint width, uint height)
    {
        if (ImageMetadata.GetDensityCorrectedSize(bytes, width, height) is var (correctedWidth, correctedHeight))
        {
            (width, height) = (correctedWidth, correctedHeight);
        }

        return ImageMetadata.GetExifOrientation(bytes).SwapsAxes() ? FromPixels(height, width) with { OrientationSwapsAxes = true } : FromPixels(width, height);
    }

    /// <summary>
    /// Whether the dimensions are swapped by the EXIF orientation of the image (a rotation by 90 or 270 degrees): the
    /// dimensions are the ones of <c>image-orientation: from-image</c> (https://drafts.csswg.org/css-images-3/#image-orientation).
    /// </summary>
    public bool OrientationSwapsAxes { get; init; }

    /// <summary>Returns the natural size without the EXIF orientation (<c>image-orientation: none</c>).</summary>
    public ImageNaturalSize WithoutOrientation() => OrientationSwapsAxes ? new ImageNaturalSize(Height, Width, AspectRatio is { } ratio ? 1 / ratio : null) : this;

    /// <summary>Returns the natural size multiplied by a factor (a zoom, or the inverse of a pixel density).</summary>
    public ImageNaturalSize Scale(double factor) => factor == 1 ? this : this with { Width = Width * factor, Height = Height * factor };

    /// <summary>
    /// The default sizing algorithm (https://drafts.csswg.org/css-images/#default-sizing) without specified size: the
    /// natural dimensions, completed with the aspect ratio and the default object size.
    /// </summary>
    public (double Width, double Height) GetConcreteSize(double defaultWidth = 300, double defaultHeight = 150)
    {
        return (Width, Height, AspectRatio) switch
        {
            ({ } width, { } height, _) => (width, height),
            ({ } width, null, { } ratio) => (width, width / ratio),
            ({ } width, null, null) => (width, defaultHeight),
            (null, { } height, { } ratio) => (height * ratio, height),
            (null, { } height, null) => (defaultWidth, height),

            // The contain constraint: the largest size with the aspect ratio that fits in the default object size
            (null, null, { } ratio) => ratio > defaultWidth / defaultHeight ? (defaultWidth, defaultWidth / ratio) : (defaultHeight * ratio, defaultHeight),
            _ => (defaultWidth, defaultHeight),
        };
    }
}

/// <summary>An image request (https://html.spec.whatwg.org/multipage/images.html#image-request).</summary>
internal sealed class ImageRequest(string currentUrl, double currentPixelDensity)
{
    public ImageRequestState State { get; set; }

    public string CurrentUrl { get; set; } = currentUrl;

    public double CurrentPixelDensity { get; } = currentPixelDensity;

    /// <summary>The natural size of the image data, read from the header of the image (nothing is decoded).</summary>
    public ImageNaturalSize NaturalSize { get; set; }

    /// <summary>The image drawn by the painting once the request is completely available (see <see cref="Document.CreatePaintImage"/>), or null.</summary>
    public Rendering.PaintImage? Image { get; set; }

    public bool Aborted { get; set; }

    /// <summary>The key of the image in the list of available images (its URL, CORS mode and origin), set when it is fetched.</summary>
    public AvailableImageKey? Key { get; set; }

    /// <summary>
    /// Whether the request was created to react to a change of the environment (https://html.spec.whatwg.org/multipage/images.html#img-environment-changes):
    /// when its image cannot be obtained, the request is dropped and the current request is kept, without event.
    /// </summary>
    public bool ReactsToEnvironmentChange { get; init; }
}

/// <summary>
/// The key of an image in the list of available images (https://html.spec.whatwg.org/multipage/images.html#list-of-available-images):
/// its URL, the state of the crossorigin attribute (0 no CORS, 1 anonymous, 2 use credentials) and, when it is a CORS
/// request, the origin of the document.
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct AvailableImageKey(string Url, int CorsState, string? Origin);

/// <summary>
/// The images of the img elements (https://html.spec.whatwg.org/multipage/images.html#images-processing-model): the source
/// is selected from the src, srcset and sizes attributes and the source elements of a parent picture element, and fetched
/// by the <see cref="DocumentLoader"/> of the browsing context (<see cref="RequestDestination.Image"/>), the data: and the
/// blob: URLs. An image is available when its response is an image (its signature or its type), and its natural dimensions
/// are read from its header (PNG, GIF, JPEG, BMP, WebP, ICO, SVG). With a rendering backend, a raster image is also opened by
/// the decoder of the backend (an image it cannot read is broken), and its pixels are decoded when they are painted or by
/// <c>decode()</c>.
/// </summary>
public partial class HtmlImageElement
{
    private ImageRequest _currentRequest = new("", 1);
    private ImageRequest? _pendingRequest;
    private int _updateGeneration;
    private bool _updatePending;
    private bool _loadEventQueued;
    private bool _delayingTheLoadEvent;

    // The document whose load event the element delays (its node document when the delay started: the element may be adopted
    // by another document meanwhile)
    private Document? _delayedDocument;
    private List<PendingDecode>? _pendingDecodes;

    /// <summary>Creates an img element (https://html.spec.whatwg.org/multipage/embedded-content.html#dom-image).</summary>
    /// <param name="document">The document of the element (the associated document of the current global object).</param>
    /// <param name="width">The value of the width attribute, or null.</param>
    /// <param name="height">The value of the height attribute, or null.</param>
    /// <returns>The new element.</returns>
    /// <seealso href="https://html.spec.whatwg.org/multipage/embedded-content.html#dom-image"/>
    [IdlMember("Image", "https://html.spec.whatwg.org/multipage/embedded-content.html#dom-image")]
    public static HtmlImageElement Create(Document document, uint? width = null, uint? height = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var image = (HtmlImageElement)ElementFactory.CreateElement(document, "img", Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false);
        if (width is { } w)
        {
            image.SetAttributeValue("width", w.ToString(CultureInfo.InvariantCulture));
        }

        if (height is { } h)
        {
            image.SetAttributeValue("height", h.ToString(CultureInfo.InvariantCulture));
        }

        return image;
    }

    /// <summary>
    /// Gets the natural width of the image (https://html.spec.whatwg.org/multipage/embedded-content.html#dom-img-naturalwidth):
    /// the width of the image data divided by the pixel density of the selected source, or 0 when the image is not available.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/embedded-content.html#dom-img-naturalwidth"/>
    [IdlMember("naturalWidth", "https://html.spec.whatwg.org/multipage/embedded-content.html#dom-img-naturalwidth")]
    public uint NaturalWidth => GetNaturalDimension(horizontal: true);

    /// <summary>Gets the intrinsic height of the image in CSS pixels (0 when the image is not available).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/embedded-content.html#dom-img-naturalheight"/>
    [IdlMember("naturalHeight", "https://html.spec.whatwg.org/multipage/embedded-content.html#dom-img-naturalheight")]
    public uint NaturalHeight => GetNaturalDimension(horizontal: false);

    private uint GetNaturalDimension(bool horizontal)
    {
        if (GetDensityCorrectedNaturalSize() is not { } size)
            return 0;

        return ToCssPixels(horizontal ? size.Width : size.Height);
    }

    /// <summary>Converts a length in CSS pixels to the value of an unsigned long IDL attribute.</summary>
    internal static uint ToCssPixels(double value) => value <= 0 || double.IsNaN(value) ? 0 : value >= uint.MaxValue ? uint.MaxValue : (uint)Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The density-corrected natural width and height of the image (https://html.spec.whatwg.org/multipage/images.html#density-corrected-intrinsic-width-and-height),
    /// or null when the image is not available: its natural dimensions divided by the current pixel density, completed by
    /// the default sizing algorithm with a default object size of 300x150.
    /// </summary>
    /// <summary>The image of the current request when it is available (see <see cref="ImageRequest.Image"/>), for the painting, or null.</summary>
    internal Rendering.PaintImage? GetPaintImage() => _currentRequest is { State: ImageRequestState.CompletelyAvailable or ImageRequestState.PartiallyAvailable } request ? request.Image : null;

    /// <summary>Whether the current request of the image is broken (https://html.spec.whatwg.org/multipage/images.html#img-error): an image without source is not broken (as in the browsers).</summary>
    internal bool IsImageBroken => _currentRequest.State == ImageRequestState.Broken && _currentRequest.CurrentUrl.Length > 0;

    /// <summary>Whether the image data is being updated (the src or srcset changed): the image is not fully decodable until the update is done.</summary>
    internal bool IsImageUpdatePending => _updatePending || _pendingRequest is not null;

    /// <summary>
    /// Returns the image of the current request when it is completely available, for the image sources of the canvases and
    /// of <c>createImageBitmap()</c> (https://html.spec.whatwg.org/multipage/canvas.html#check-the-usability-of-the-image-argument),
    /// with its natural size in image pixels (with its EXIF orientation) and whether it is CORS-same-origin; or null when the
    /// image is broken or not fully decodable.
    /// </summary>
    internal (PaintImage? Image, ImageNaturalSize NaturalSize, bool CorsSameOrigin)? GetBitmapSource()
    {
        if (_currentRequest is not { State: ImageRequestState.CompletelyAvailable } request)
            return null;

        return (request.Image, request.NaturalSize, request.Image?.IsCorsSameOrigin ?? IsCorsSameOrigin(request));
    }

    internal (double Width, double Height)? GetDensityCorrectedNaturalSize()
    {
        if (_currentRequest is not { State: ImageRequestState.CompletelyAvailable or ImageRequestState.PartiallyAvailable } request)
            return null;

        var density = request.CurrentPixelDensity;
        var natural = request.NaturalSize;
        return (natural with { Width = natural.Width / density, Height = natural.Height / density }).GetConcreteSize();
    }

    /// <summary>
    /// The dimensions of the element (https://html.spec.whatwg.org/multipage/embedded-content-other.html#dimension-attributes):
    /// the rendered size of its content box when it is being rendered (see <see cref="GetRenderedSize"/>), else the
    /// density-corrected natural dimensions of the image (when it is available) replaced by the width and height attributes,
    /// as in the browsers.
    /// </summary>
    private (uint Width, uint Height) GetDimensions()
    {
        if (GetRenderedSize() is { } rendered)
            return (ToCssPixels(rendered.Width), ToCssPixels(rendered.Height));

        var natural = GetDensityCorrectedNaturalSize();
        var width = natural is { } size ? ToCssPixels(size.Width) : 0;
        var height = natural is { } size2 ? ToCssPixels(size2.Height) : 0;
        if (GetAttributeValue("width") is { } widthValue && Microsyntaxes.TryParseNonNegativeInteger(widthValue, out var attributeWidth))
        {
            width = (uint)Math.Min(attributeWidth, uint.MaxValue);
        }

        if (GetAttributeValue("height") is { } heightValue && Microsyntaxes.TryParseNonNegativeInteger(heightValue, out var attributeHeight))
        {
            height = (uint)Math.Min(attributeHeight, uint.MaxValue);
        }

        return (width, height);
    }

    private partial uint ComputeWidth() => GetDimensions().Width;

    private partial uint ComputeHeight() => GetDimensions().Height;

    /// <summary>https://html.spec.whatwg.org/multipage/embedded-content.html#dom-img-complete</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/embedded-content.html#dom-img-complete"/>
    [IdlMember("complete", "https://html.spec.whatwg.org/multipage/embedded-content.html#dom-img-complete")]
    public bool Complete
    {
        get
        {
            var src = GetAttributeValue("src");
            var srcset = GetAttributeValue("srcset");
            if ((src is null && srcset is null) || (srcset is null && src?.Length == 0))
                return true;

            // The browsers start the new request synchronously: an image is not complete until the image data is updated
            // (WPT img.complete.html, update-src-complete.html)
            if (_updatePending)
                return false;

            if (_loadEventQueued)
                return true;

            return _pendingRequest is null && _currentRequest.State is ImageRequestState.CompletelyAvailable or ImageRequestState.Broken;
        }
    }

    /// <summary>Gets the URL of the current request (https://html.spec.whatwg.org/multipage/embedded-content.html#dom-img-currentsrc).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/embedded-content.html#dom-img-currentsrc"/>
    [IdlMember("currentSrc", "https://html.spec.whatwg.org/multipage/embedded-content.html#dom-img-currentsrc")]
    public string CurrentSrc => _currentRequest.CurrentUrl;

    /// <summary>
    /// Returns a task that completes when the image is available and decoded (https://html.spec.whatwg.org/multipage/embedded-content.html#dom-img-decode),
    /// or fails with an <c>EncodingError</c> when the image cannot be loaded or decoded, the current request changes or the
    /// document is not fully active. The pixels are decoded by the rendering backend of the browsing context
    /// (<see cref="BrowsingContextOptions.RenderingBackend"/>); without backend, nothing is decoded and an available image succeeds.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/embedded-content.html#dom-img-decode"/>
    [IdlMember("decode", "https://html.spec.whatwg.org/multipage/embedded-content.html#dom-img-decode")]
    public Task Decode()
    {
        if (!NodeDocument.IsFullyActive || NodeDocument.EventLoop is not { } eventLoop)
            return Task.FromException(CreateDecodeError());

        // The checks run in a microtask, after the microtask of update the image data
        var decode = new PendingDecode(new TaskCompletionSource(), eventLoop);
        eventLoop.QueueMicrotask(() =>
        {
            // An image without request (e.g. no src) cannot be decoded either
            if (!NodeDocument.IsFullyActive || _currentRequest.State == ImageRequestState.Broken || (_currentRequest.State == ImageRequestState.Unavailable && !_delayingTheLoadEvent))
            {
                decode.Settle(success: false, synchronously: false);
            }
            else if (_currentRequest.State == ImageRequestState.CompletelyAvailable && _pendingRequest is null)
            {
                decode.Settle(success: DecodeImage(_currentRequest), synchronously: false);
            }
            else
            {
                (_pendingDecodes ??= []).Add(decode);
            }
        });

        return decode.Completion.Task;
    }

    private static DomException CreateDecodeError() => new("The image cannot be decoded", "EncodingError");

    /// <summary>
    /// Decodes the image of an available request (https://html.spec.whatwg.org/multipage/embedded-content.html#dom-img-decode):
    /// returns false when the rendering backend cannot decode its pixels (e.g. its data is corrupted after a valid header). An
    /// image that is not decoded (an SVG image, or any image without rendering backend) needs no decoding.
    /// </summary>
    private bool DecodeImage(ImageRequest request)
    {
        if (NodeDocument.RenderingBackend is not { } backend || request.Image is not { } image || image.GetDecoder(backend) is null)
            return true;

        return image.GetDecodedImage(backend) is not null;
    }

    /// <summary>The current request changes or is mutated, or the document stops being fully active: the pending decodes fail.</summary>
    private void RejectDecodes() => SettleDecodes(success: false, synchronously: false);

    /// <summary>Settles the pending decodes, in a task unless <paramref name="synchronously"/> (when a task of the element is running).</summary>
    private void SettleDecodes(bool success, bool synchronously)
    {
        if (_pendingDecodes is not { } decodes)
            return;

        _pendingDecodes = null;
        foreach (var decode in decodes)
        {
            decode.Settle(success, synchronously);
        }
    }

    /// <summary>A promise returned by <see cref="Decode"/>, and the event loop of its global object.</summary>
    private sealed record PendingDecode(TaskCompletionSource Completion, DomEventLoop EventLoop)
    {
        /// <summary>
        /// Resolves or rejects the promise. The continuations of the task resolve the promise synchronously, so the promise
        /// is settled in a global task (on a shallow stack) unless a task is running.
        /// </summary>
        public void Settle(bool success, bool synchronously)
        {
            if (synchronously)
            {
                SettleNow();
            }
            else
            {
                EventLoop.QueueTask(SettleNow);
            }

            void SettleNow()
            {
                if (success)
                {
                    Completion.TrySetResult();
                }
                else
                {
                    Completion.TrySetException(CreateDecodeError());
                }
            }
        }
    }

    /// <summary>The relevant mutations of the attributes (https://html.spec.whatwg.org/multipage/images.html#relevant-mutations).</summary>
    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is not null)
            return;

        if (localName is "src" or "srcset" or "width" or "sizes"
            || (localName == "crossorigin" && GetCorsSettingsState(oldValue) != GetCorsSettingsState(value))
            || (localName == "referrerpolicy" && GetReferrerPolicyState(oldValue) != GetReferrerPolicyState(value)))
        {
            UpdateImageData();
        }
        else if (localName == "loading")
        {
            // https://html.spec.whatwg.org/multipage/embedded-content.html#attr-img-loading
            LazyLoading.OnLoadingAttributeChanged(this, oldValue, value);
        }
    }

    /// <summary>The state of a CORS settings attribute (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#cors-settings-attribute): 0 no CORS, 1 anonymous, 2 use credentials.</summary>
    private static int GetCorsSettingsState(string? value) => value is null ? 0 : InfraAscii.EqualsIgnoreAsciiCase(value, "use-credentials") ? 2 : 1;

    /// <summary>The state of a referrer policy attribute (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#referrer-policy-attribute): a valid keyword, else the empty string.</summary>
    private static string GetReferrerPolicyState(string? value)
    {
        var keyword = InfraAscii.ToAsciiLowercase(value ?? "");
        return keyword is "no-referrer" or "no-referrer-when-downgrade" or "same-origin" or "origin" or "strict-origin" or "origin-when-cross-origin" or "strict-origin-when-cross-origin" or "unsafe-url" ? keyword : "";
    }

    /// <summary>
    /// The img HTML element insertion steps (https://html.spec.whatwg.org/multipage/embedded-content.html#the-img-element):
    /// the element is inserted into a picture parent. As in the browsers, inserting the picture parent (or another ancestor)
    /// is not a relevant mutation (WPT relevant-mutations.html).
    /// </summary>
    internal override void InsertionSteps()
    {
        base.InsertionSteps();
        if (ParentNode is HtmlPictureElement && IsInsertionRoot)
        {
            UpdateImageData();
        }
    }

    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);

        // The element is removed from a picture parent
        if (isSubtreeRoot && oldAncestor is HtmlPictureElement)
        {
            UpdateImageData();
        }
    }

    /// <summary>
    /// The img HTML element moving steps (https://html.spec.whatwg.org/multipage/embedded-content.html#the-img-element):
    /// moving the element out of a picture parent is a relevant mutation, and so is moving it into a picture parent, as for
    /// the insertion steps (as in the browsers, WPT dom/nodes/moveBefore/relevant-mutations.html).
    /// </summary>
    internal override void MovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.MovingSteps(isSubtreeRoot, oldAncestor);
        if (isSubtreeRoot && (oldAncestor is HtmlPictureElement || ParentNode is HtmlPictureElement))
        {
            UpdateImageData();
        }
    }

    internal override void AdoptingSteps(Document oldDocument)
    {
        base.AdoptingSteps(oldDocument);

        // The node document of the pending decodes is no longer fully active
        if (!NodeDocument.IsFullyActive)
        {
            RejectDecodes();
        }

        UpdateImageData();
    }

    /// <summary>
    /// Update the image data (https://html.spec.whatwg.org/multipage/images.html#update-the-image-data). Nothing happens in a
    /// document that is not fully active (e.g. a document without browsing context).
    /// </summary>
    internal void UpdateImageData()
    {
        var document = NodeDocument;
        if (!document.IsFullyActive || document.EventLoop is not { } eventLoop)
            return;

        // The rest of the previous update (a lazy fetch) is replaced by the new one
        LazyLoading.ClearResumptionSteps(this);
        var generation = ++_updateGeneration;
        if (UpdateImageDataSynchronously(document, generation))
        {
            _updatePending = false;
            InvalidateRendering();
            return;
        }

        _updatePending = true;
        SetDelayingTheLoadEvent(true);
        InvalidateRendering();
        eventLoop.QueueMicrotask(() =>
        {
            // Another instance of the algorithm started after this one
            if (generation != _updateGeneration)
                return;

            _updatePending = false;
            UpdateImageDataSteps(document);
            InvalidateRendering();
        });
    }

    /// <summary>
    /// The synchronous steps of update the image data, when the element does not use srcset or picture: an image of the list
    /// of available images is used at once, and, as in the browsers, the image is cleared at once when the src attribute is
    /// removed (WPT remove-attribute-src.html). Returns whether the algorithm is complete.
    /// </summary>
    private bool UpdateImageDataSynchronously(Document document, int generation)
    {
        if (UsesSrcsetOrPicture())
            return false;

        var src = GetAttributeValue("src");
        if (src is null)
        {
            // There is no selected source, and no event is fired
            AbortRequest(_currentRequest);
            AbortPendingRequest();
            _currentRequest = new ImageRequest("", 1) { State = ImageRequestState.Broken };
            RejectDecodes();
            SetDelayingTheLoadEvent(false);
            return true;
        }

        if (src.Length == 0 || Reflection.EncodingParseUrl(document, src) is not { } url)
            return false;

        var key = GetAvailableImageKey(document, url.Serialize());
        if (document.AvailableImages is not { } availableImages || !availableImages.TryGetValue(key, out var availableImage))
            return false;

        // https://html.spec.whatwg.org/multipage/images.html#update-the-image-data, step 6
        AbortRequest(_currentRequest);
        AbortPendingRequest();
        var request = new ImageRequest("", 1) { State = ImageRequestState.CompletelyAvailable, NaturalSize = availableImage.NaturalSize, Image = availableImage.Image, Key = key };
        _currentRequest = request;
        RejectDecodes();
        _loadEventQueued = false;
        SetDelayingTheLoadEvent(true);
        QueueImageTask(document, () =>
        {
            // As in the browsers, the load event is not fired when another instance of the algorithm started since then
            // (WPT adoption.html): the other instance fires the events
            if (generation != _updateGeneration)
                return;

            request.CurrentUrl = key.Url;
            NodeDocument.ProcessImageThatFinishedLoading(this, key.Url, availableImage.NaturalSize, availableImage.Image is null ? long.MaxValue : availableImage.ContentLength);
            FireImageEvent("load");
        });

        return true;
    }

    private AvailableImageKey GetAvailableImageKey(Document document, string url)
    {
        var corsState = GetCorsSettingsState(GetAttributeValue("crossorigin"));
        return new AvailableImageKey(url, corsState, corsState == 0 ? null : document.SerializeOrigin());
    }

    /// <summary>
    /// Reacts to the changes of the environment (https://html.spec.whatwg.org/multipage/images.html#img-environment-changes),
    /// in a task queued when the environment of the document changed (the size of the viewport, the device pixel ratio...):
    /// when the element uses srcset or picture and has no pending request, the image source is selected again. When only the
    /// pixel density of the selected source changed (the <c>sizes</c> attribute), the image data of the current request is
    /// kept, as when it is obtained from the cache; otherwise the selected source is fetched by a pending request, which
    /// becomes the current request when its image is available (the current request is kept when it is not). The load event
    /// is fired in both cases.
    /// </summary>
    internal void ReactToEnvironmentChanges()
    {
        var document = NodeDocument;
        if (!UsesSrcsetOrPicture() || !document.IsFullyActive || _pendingRequest is not null || _updatePending)
            return;

        var (selectedSource, density) = SelectImageSource();
        if (selectedSource is null || Reflection.EncodingParseUrl(document, selectedSource) is not { } url)
            return;

        var urlString = url.Serialize();
        var current = _currentRequest;
        if (urlString != current.CurrentUrl)
        {
            var request = new ImageRequest(urlString, density) { Key = GetAvailableImageKey(document, urlString), ReactsToEnvironmentChange = true };
            _pendingRequest = request;
            FetchImage(document, url, request);
            return;
        }

        if (density == current.CurrentPixelDensity || current.State != ImageRequestState.CompletelyAvailable)
            return;

        _currentRequest = new ImageRequest(urlString, density) { State = ImageRequestState.CompletelyAvailable, NaturalSize = current.NaturalSize, Image = current.Image, Key = current.Key };
        FireImageEvent("load");
    }

    private void UpdateImageDataSteps(Document document)
    {
        if (UsesSrcsetOrPicture())
        {
            document.MayHaveImagesReactingToEnvironment = true;
        }

        var (selectedSource, density) = SelectImageSource();
        if (selectedSource is null)
        {
            // No source: the current request is broken. The specification fires an error event when the element has a src or
            // uses srcset or picture, but the browsers only fire it when the element has a src attribute: not for an empty
            // srcset nor for a picture without source (WPT null-image-source.html)
            AbortRequest(_currentRequest);
            _currentRequest.State = ImageRequestState.Broken;
            AbortPendingRequest();
            RejectDecodes();
            var fireError = HasAttributeWithLocalName("src");
            var brokenRequest = _currentRequest;
            QueueImageTask(document, () =>
            {
                // The request of the task (a new src may have started another request since then)
                brokenRequest.CurrentUrl = "";
                if (fireError)
                {
                    FireImageEvent("error");
                }
                else
                {
                    SetDelayingTheLoadEvent(false);
                }
            });
            return;
        }

        var url = Reflection.EncodingParseUrl(document, selectedSource);
        if (url is null)
        {
            AbortRequest(_currentRequest);
            _currentRequest.State = ImageRequestState.Broken;
            AbortPendingRequest();
            RejectDecodes();
            _currentRequest.CurrentUrl = selectedSource;
            QueueImageTask(document, () => FireImageEvent("error"));
            return;
        }

        var urlString = url.Serialize();
        if (_pendingRequest is not null && _pendingRequest.CurrentUrl == urlString)
        {
            SetDelayingTheLoadEvent(false);
            return;
        }

        if (urlString == _currentRequest.CurrentUrl && _currentRequest.State == ImageRequestState.PartiallyAvailable)
        {
            AbortPendingRequest();
            SetDelayingTheLoadEvent(false);
            return;
        }

        AbortPendingRequest();
        var request = new ImageRequest(urlString, density) { Key = GetAvailableImageKey(document, urlString) };
        var currentRequestChanged = _currentRequest.State is ImageRequestState.Unavailable or ImageRequestState.Broken;
        if (currentRequestChanged)
        {
            AbortRequest(_currentRequest);
            _currentRequest = request;
        }
        else
        {
            _pendingRequest = request;
        }

        _loadEventQueued = false;

        // https://html.spec.whatwg.org/multipage/images.html#update-the-image-data, steps 24 and 25: a lazy image is fetched
        // when it is about to intersect the viewport (its lazy load resumption steps), and does not delay the load event
        if (LazyLoading.WillLazyLoad(this))
        {
            SetDelayingTheLoadEvent(false);
            LazyLoading.Defer(this, () => FetchImage(NodeDocument, url, request));
        }
        else
        {
            FetchImage(document, url, request);
        }

        // The pending decodes are rejected in a task queued after the one of a response that is already available
        if (currentRequestChanged)
        {
            RejectDecodes();
        }
    }

    /// <summary>Whether the element uses srcset or picture (https://html.spec.whatwg.org/multipage/images.html#use-srcset-or-picture).</summary>
    internal bool UsesSrcsetOrPicture() => HasAttributeWithLocalName("srcset") || ParentNode is HtmlPictureElement;

    private void AbortPendingRequest()
    {
        if (_pendingRequest is { } pending)
        {
            AbortRequest(pending);
            _pendingRequest = null;
        }
    }

    private static void AbortRequest(ImageRequest request) => request.Aborted = true;

    private void FetchImage(Document document, UrlRecord url, ImageRequest request)
    {
        // The image of an image document is the response of the navigation: it is not fetched again
        if (document.TakeMediaDocumentResource(url) is { } navigationResponse)
        {
            QueueImageTask(document, () => ProcessResponse(request, navigationResponse));
            return;
        }

        if (document.BrowsingContext?.Options.DocumentLoader is null && url.Scheme is not ("data" or "blob"))
        {
            // Nothing can be fetched: a network error
            QueueImageTask(document, () => ProcessResponse(request, resource: null));
            return;
        }

        // https://html.spec.whatwg.org/multipage/images.html#update-the-image-data: a potential-CORS request with the
        // referrer policy of the element. An http: image of a document that prohibits mixed security contexts is fetched over
        // https: (https://w3c.github.io/webappsec-mixed-content/#upgrade-algorithm), unless the element uses srcset or picture
        // (the "imageset" initiator); the current URL (currentSrc) is unchanged. The fetch algorithm blocks the mixed content
        // and the requests blocked by the content security policies, and performs the CORS check.
        var fetchRequest = SubresourceFetching.CreateRequest(document, url, RequestDestination.Image, cors: HasAttribute("crossorigin"), ReferrerPolicy, this, imageSet: UsesSrcsetOrPicture(), useCredentials: SubresourceFetching.UsesCredentials(this));
        var timing = ResourceTiming.Start(document, fetchRequest, LocalName);
        Fetching.Fetch(fetchRequest, ProcessFetchResponse, task => QueueImageTask(document, task));

        void ProcessFetchResponse(FetchResponse response)
        {
            if (fetchRequest.IsSentToLoader)
            {
                timing?.Report(response);
            }

            if (document.EventLoop is null)
            {
                SetDelayingTheLoadEvent(false);
                return;
            }

            ProcessResponse(request, response.ToDocumentResource(), fetched: true);
        }
    }

    /// <summary>The steps run when the response of an image request is received (https://html.spec.whatwg.org/multipage/images.html#update-the-image-data, step 26).</summary>
    private void ProcessResponse(ImageRequest request, DocumentResource? resource, bool fetched = false)
    {
        if (request.Aborted)
            return;

        if (ReferenceEquals(request, _pendingRequest) && !request.ReactsToEnvironmentChange)
        {
            // Upgrade the pending request to the current request
            _currentRequest = request;
            _pendingRequest = null;
        }

        // An opaque response (e.g. a no-CORS response of another origin, or redirected through another origin) is not
        // CORS-same-origin: it taints the canvases (https://fetch.spec.whatwg.org/#concept-request-response-tainting). The type
        // of the response of the fetch tells it, whatever the URL of the request: a service worker may answer a cross-origin
        // no-CORS request with a response that is not opaque, and a same-origin request with an opaque response.
        var document = NodeDocument;
        var corsSameOrigin = fetched && resource is not null ? resource.IsCorsSameOrigin : IsCorsSameOrigin(request) && resource?.ResponseType is not (FetchResponseType.Opaque or FetchResponseType.OpaqueRedirect);
        var isImage = TryCreateImage(document, request.CurrentUrl, resource, corsSameOrigin, out var bytes, out var naturalSize, out var image);

        if (request.ReactsToEnvironmentChange)
        {
            // https://html.spec.whatwg.org/multipage/images.html#img-environment-changes: when the image cannot be obtained,
            // "set the pending request to null and abort these steps"; otherwise the pending request becomes the current one
            if (!ReferenceEquals(request, _pendingRequest))
                return;

            _pendingRequest = null;
            if (!isImage)
            {
                SetDelayingTheLoadEvent(false);
                return;
            }

            _currentRequest = request;
        }

        if (!isImage)
        {
            request.State = ImageRequestState.Broken;
            FireImageEvent("error");
            return;
        }

        request.State = ImageRequestState.CompletelyAvailable;
        request.NaturalSize = naturalSize;
        request.Image = image;
        if (request.Key is { } key)
        {
            (document.AvailableImages ??= [])[key] = new AvailableImage(naturalSize, image, bytes.Length);
        }

        // https://w3c.github.io/paint-timing/#sec-modifications-HTML: the associated image request became completely available
        NodeDocument.ProcessImageThatFinishedLoading(this, request.CurrentUrl, naturalSize, bytes.Length);
        FireImageEvent("load");
    }

    /// <summary>
    /// Reads the image of a response (null for a network error): its natural size from its header, and the image the painting
    /// draws (see <see cref="Document.CreatePaintImage"/>). Returns whether the response is a valid image of a supported type.
    /// </summary>
    internal static bool TryCreateImage(Document document, string url, DocumentResource? resource, bool corsSameOrigin, out ReadOnlyMemory<byte> bytes, out ImageNaturalSize naturalSize, out PaintImage? image)
    {
        bytes = resource is null ? default : resource.ContentBytes ?? System.Text.Encoding.UTF8.GetBytes(resource.Content ?? "");
        naturalSize = default;
        image = null;
        var mimeType = resource?.MimeType?.Essence;
        if (resource is null || !ImageHeaders.TryGetImageInfo(bytes.Span, mimeType ?? "", out naturalSize))
            return false;

        image = document.CreatePaintImage(url, naturalSize, bytes, mimeType, corsSameOrigin);

        // https://html.spec.whatwg.org/multipage/images.html#img-req-data: with a rendering backend, the image is broken when the
        // decoder of the backend cannot read it (an unsupported format, a corrupted header); its pixels are decoded when they are
        // needed (an image whose data is corrupted after a valid header is available, but cannot be decoded: decode() rejects).
        // SVG images are rendered by the painting of their document.
        if (document.RenderingBackend is { } backend && ImageHeaders.HasRasterSignature(bytes.Span))
        {
            if (image.GetDecoder(backend) is not { } decoder)
                return false;

            if (naturalSize.Width is null || naturalSize.Height is null)
            {
                // The natural size the header parser could not read (e.g. a format it does not know)
                var swap = image.Orientation.SwapsAxes();
                naturalSize = ImageNaturalSize.FromPixels((uint)(swap ? decoder.Height : decoder.Width), (uint)(swap ? decoder.Width : decoder.Height)) with { OrientationSwapsAxes = swap };
                image = document.CreatePaintImage(url, naturalSize, bytes, mimeType, corsSameOrigin);
            }
        }

        return true;
    }

    private void FireImageEvent(string type)
    {
        // The natural size or the state of the current request changed
        InvalidateRendering();
        if (type == "load")
        {
            _loadEventQueued = true;
        }

        // The pixels are decoded only for the pending decode() promises
        SettleDecodes(type == "load" && (_pendingDecodes is null || DecodeImage(_currentRequest)), synchronously: true);
        FireEvent(new Event(type));
        SetDelayingTheLoadEvent(false);
    }

    private void QueueImageTask(Document document, Action task)
    {
        if (document.EventLoop is null)
        {
            SetDelayingTheLoadEvent(false);
            return;
        }

        document.QueueElementTask(task);
    }

    /// <summary>The img element delays the load event of its document while its image is being fetched.</summary>
    private void SetDelayingTheLoadEvent(bool value)
    {
        if (_delayingTheLoadEvent == value)
            return;

        _delayingTheLoadEvent = value;
        if (value)
        {
            _delayedDocument = NodeDocument;
            _delayedDocument.ImagesDelayingTheLoadEvent++;
            return;
        }

        var document = _delayedDocument ?? NodeDocument;
        _delayedDocument = null;
        document.ImagesDelayingTheLoadEvent--;
        document.TryQueueLoadEvent();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Selecting an image source (https://html.spec.whatwg.org/multipage/images.html#select-an-image-source)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the URL and the pixel density of the source to fetch, or null when there is no source. The media queries and
    /// the device pixel ratio are the ones of <paramref name="environmentDocument"/> (the node document of the element by
    /// default; the document of a speculative parser for its copies of the elements).
    /// </summary>
    internal (string? Url, double Density) SelectImageSource(Document? environmentDocument = null)
    {
        environmentDocument ??= NodeDocument;
        return SelectImageSource(UpdateSourceSet(environmentDocument), environmentDocument);
    }

    /// <summary>
    /// The URL of the image source selected from the source set of a preload (https://html.spec.whatwg.org/multipage/links.html#preload,
    /// step 2): the candidates of <paramref name="srcset"/> (the <c>imagesrcset</c> attribute), sized by <paramref name="sizes"/>
    /// (the <c>imagesizes</c> attribute), and <paramref name="src"/> (the <c>href</c> attribute) as default source. Returns
    /// null when the source set is empty.
    /// </summary>
    internal static string? SelectImageSource(string src, string srcset, string? sizes, Document environmentDocument)
    {
        var environment = StyleEngine.CreateEnvironment(environmentDocument);
        var candidates = SrcsetParser.Parse(srcset);
        var sourceSize = SrcsetParser.ParseSizes(sizes, environment);
        if (src.Length > 0 && !candidates.Exists(c => c.Width is not null || (c.Width is null && c.Density == 1)))
        {
            candidates.Add(new ImageCandidate(src, 1, Width: null));
        }

        return SelectImageSource(Normalize(candidates, sourceSize), environmentDocument).Url;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/images.html#select-an-image-source-from-a-source-set</summary>
    private static (string? Url, double Density) SelectImageSource(List<ImageCandidate> candidates, Document environmentDocument)
    {
        if (candidates.Count == 0)
            return (null, 1);

        // The candidate whose density is the smallest one greater than or equal to the device pixel ratio, else the largest one
        var devicePixelRatio = environmentDocument.BrowsingContext?.Top.Options.Viewport?.DevicePixelRatio ?? 1;
        ImageCandidate? best = null;
        foreach (var candidate in candidates)
        {
            if (best is null)
            {
                best = candidate;
                continue;
            }

            var bestIsEnough = best.Value.Density >= devicePixelRatio;
            var candidateIsEnough = candidate.Density >= devicePixelRatio;
            if ((candidateIsEnough && (!bestIsEnough || candidate.Density < best.Value.Density)) || (!candidateIsEnough && !bestIsEnough && candidate.Density > best.Value.Density))
            {
                best = candidate;
            }
        }

        return (best!.Value.Url, best.Value.Density);
    }

    /// <summary>The source set of the element (https://html.spec.whatwg.org/multipage/images.html#update-the-source-set), normalized (densities).</summary>
    private List<ImageCandidate> UpdateSourceSet(Document environmentDocument)
    {
        var environment = StyleEngine.CreateEnvironment(environmentDocument);
        if (SelectSourceElement(environment) is ({ } source, { } candidates))
        {
            // https://html.spec.whatwg.org/multipage/embedded-content.html#attr-source-sizes: when the img element allows
            // auto-sizes, a source element without sizes attribute is equivalent to sizes=auto
            var sizes = source.GetAttributeValue("sizes") ?? (SrcsetParser.AllowsAutoSizes(this) ? "auto" : null);
            return Normalize(candidates, SrcsetParser.ParseSizes(sizes, environment, this));
        }

        var imgCandidates = GetAttributeValue("srcset") is { } srcset ? SrcsetParser.Parse(srcset) : [];
        var sourceSize = SrcsetParser.ParseSizes(GetAttributeValue("sizes"), environment, this);

        // The src is a candidate of density 1, unless a candidate has a width descriptor or a density of 1
        if (GetAttributeValue("src") is { Length: > 0 } src && !imgCandidates.Exists(c => c.Width is not null || (c.Width is null && c.Density == 1)))
        {
            imgCandidates.Add(new ImageCandidate(src, 1, Width: null));
        }

        return Normalize(imgCandidates, sourceSize);
    }

    /// <summary>
    /// The source element of the parent picture element whose source set is selected by the steps of update the source set
    /// (https://html.spec.whatwg.org/multipage/images.html#update-the-source-set), with its candidates, or null when the
    /// source set of the img element itself is used.
    /// </summary>
    private (HtmlSourceElement Source, List<ImageCandidate> Candidates)? SelectSourceElement(MediaEnvironment environment)
    {
        if (ParentNode is not HtmlPictureElement picture)
            return null;

        foreach (var child in picture.ChildNodes)
        {
            if (ReferenceEquals(child, this))
                break;

            if (child is not HtmlSourceElement source || source.GetAttributeValue("srcset") is not { Length: > 0 } sourceSrcset)
                continue;

            var candidates = SrcsetParser.Parse(sourceSrcset);
            if (candidates.Count == 0)
                continue;

            if (source.GetAttributeValue("media") is { } media && !MediaQueryEvaluator.Matches(media, environment))
                continue;

            if (source.GetAttributeValue("type") is { } type && !IsSupportedImageType(type))
                continue;

            return (source, candidates);
        }

        return null;
    }

    /// <summary>
    /// The dimension attribute source of the element (https://html.spec.whatwg.org/multipage/embedded-content.html#concept-img-dimension-attribute-source):
    /// the source element selected by update the source set when it has a width or a height attribute, otherwise the
    /// element itself. It is computed from the current state of the picture element, in the given environment.
    /// </summary>
    internal Element GetDimensionAttributeSource(MediaEnvironment environment)
    {
        if (SelectSourceElement(environment) is ({ } source, _) && (source.HasAttributeWithLocalName("width") || source.HasAttributeWithLocalName("height")))
            return source;

        return this;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/images.html#normalise-the-source-densities</summary>
    private static List<ImageCandidate> Normalize(List<ImageCandidate> candidates, double sourceSize)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].Width is { } width)
            {
                candidates[i] = candidates[i] with { Density = sourceSize == 0 ? double.PositiveInfinity : width / sourceSize };
            }
        }

        return candidates;
    }

    /// <summary>
    /// The image types supported by the type attribute of the source elements (the types of the user agents). An empty or
    /// whitespace-only type is not a type, as in the browsers (WPT update-the-source-set.html).
    /// </summary>
    internal static bool IsSupportedImageType(string type)
    {
        if (InfraString.IsAsciiWhitespaceOnly(type))
            return true;

        var essence = InfraAscii.ToAsciiLowercase(type.Split(';')[0].Trim());
        return essence is "image/png" or "image/apng" or "image/jpeg" or "image/pjpeg" or "image/gif" or "image/webp" or "image/svg+xml" or "image/bmp" or "image/x-icon" or "image/vnd.microsoft.icon" or "image/avif";
    }
}

/// <summary>A candidate of a source set: its URL, its density (or its width descriptor).</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct ImageCandidate(string Url, double Density, int? Width);

/// <summary>The parsers of the srcset and sizes attributes (https://html.spec.whatwg.org/multipage/images.html#srcset-attributes).</summary>
internal static class SrcsetParser
{
    /// <summary>https://html.spec.whatwg.org/multipage/images.html#parse-a-srcset-attribute</summary>
    public static List<ImageCandidate> Parse(string input)
    {
        var candidates = new List<ImageCandidate>();
        var position = 0;
        while (true)
        {
            // Splitting loop: skip the whitespace and the commas
            while (position < input.Length && (IsWhitespace(input[position]) || input[position] == ','))
            {
                position++;
            }

            if (position >= input.Length)
                return candidates;

            var urlStart = position;
            while (position < input.Length && !IsWhitespace(input[position]))
            {
                position++;
            }

            var url = input[urlStart..position];
            var descriptors = new List<string>();
            if (url.EndsWith(',', StringComparison.Ordinal))
            {
                url = url.TrimEnd(',');
            }
            else
            {
                // Tokenize the descriptors
                while (position < input.Length && IsWhitespace(input[position]))
                {
                    position++;
                }

                var current = new System.Text.StringBuilder();
                var inParens = false;
                while (true)
                {
                    var c = position < input.Length ? input[position] : '\0';
                    var eof = position >= input.Length;
                    if (inParens)
                    {
                        if (eof)
                        {
                            descriptors.Add(current.ToString());
                            break;
                        }

                        if (c == ')')
                        {
                            inParens = false;
                        }

                        current.Append(c);
                    }
                    else if (eof || c == ',')
                    {
                        if (current.Length > 0)
                        {
                            descriptors.Add(current.ToString());
                        }

                        position++;
                        break;
                    }
                    else if (IsWhitespace(c))
                    {
                        if (current.Length > 0)
                        {
                            descriptors.Add(current.ToString());
                            current.Clear();
                        }
                    }
                    else
                    {
                        if (c == '(')
                        {
                            inParens = true;
                        }

                        current.Append(c);
                    }

                    position++;
                }
            }

            if (ParseDescriptors(descriptors) is { } candidate)
            {
                candidates.Add(new ImageCandidate(url, candidate.Density, candidate.Width));
            }
        }
    }

    /// <summary>The descriptors of a candidate: one width (w) or one density (x); the height (h) needs a width; invalid descriptors drop the candidate.</summary>
    private static (double Density, int? Width)? ParseDescriptors(List<string> descriptors)
    {
        int? width = null;
        double? density = null;
        int? height = null;
        foreach (var descriptor in descriptors)
        {
            if (descriptor.Length < 2)
                return null;

            var value = descriptor[..^1];
            switch (descriptor[^1])
            {
                case 'w' when width is null && density is null && Microsyntaxes.TryParseNonNegativeInteger(value, out var w) && w > 0 && IsValidInteger(value):
                    width = (int)w;
                    break;
                case 'x' when width is null && density is null && height is null && TryParseFloat(value, out var x) && x >= 0:
                    density = x;
                    break;
                case 'h' when height is null && density is null && Microsyntaxes.TryParseNonNegativeInteger(value, out var h) && h > 0 && IsValidInteger(value):
                    height = (int)h;
                    break;
                default:
                    return null;
            }
        }

        if (height is not null && width is null)
            return null;

        return (density ?? 1, width);
    }

    private static bool IsValidInteger(string value) => value.Length > 0 && value.All(char.IsAsciiDigit);

    /// <summary>
    /// A valid floating-point number (https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#valid-floating-point-number):
    /// an optional "-", digits and/or a "." followed by digits, then an optional exponent ("e" or "E", an optional sign, digits).
    /// </summary>
    private static bool TryParseFloat(string value, out double result)
    {
        result = 0;
        var position = 0;
        if (position < value.Length && value[position] == '-')
        {
            position++;
        }

        var integerDigits = SkipDigits(value, ref position);
        var fractionDigits = 0;
        if (position < value.Length && value[position] == '.')
        {
            position++;
            fractionDigits = SkipDigits(value, ref position);
            if (fractionDigits == 0)
                return false;
        }

        if (integerDigits == 0 && fractionDigits == 0)
            return false;

        if (position < value.Length && value[position] is 'e' or 'E')
        {
            position++;
            if (position < value.Length && value[position] is '-' or '+')
            {
                position++;
            }

            if (SkipDigits(value, ref position) == 0)
                return false;
        }

        return position == value.Length && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result);

        static int SkipDigits(string value, ref int position)
        {
            var start = position;
            while (position < value.Length && char.IsAsciiDigit(value[position]))
            {
                position++;
            }

            return position - start;
        }
    }

    private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/images.html#parse-a-sizes-attribute: the size of the first source size whose
    /// media condition matches, in CSS px, else 100vw. The auto size is the concrete object size width of an image that is
    /// being rendered and allows auto-sizes (from the layout).
    /// </summary>
    public static double ParseSizes(string? input, MediaEnvironment environment, HtmlImageElement? image = null)
    {
        var defaultSize = environment.Width;
        if (input is null)
            return defaultSize;

        foreach (var unparsedSize in SplitOnCommas(CssParser.ParseListOfComponentValues(input)))
        {
            var values = TrimWhitespace(unparsedSize);
            if (values.Count == 0)
                continue;

            double? autoSize = null;
            var isAuto = values[^1] is CssPreservedToken { Type: CssTokenType.Ident } autoToken && InfraAscii.EqualsIgnoreAsciiCase(autoToken.Token.Value, "auto");
            if (isAuto)
            {
                autoSize = image is not null && AllowsAutoSizes(image) && Css.Cascade.ComputedStyleDeclarations.IsBeingRendered(image) ? GetConcreteObjectWidth(image) : null;
            }

            // A negative length is invalid; the result of a math function is clamped, but an infinite or NaN result is
            // invalid, as in Firefox (WPT parse-a-sizes-attribute-*.html)
            double size;
            if (isAuto)
            {
                size = autoSize ?? -1;
            }
            else if (MediaQueryEvaluator.EvaluateLength(values[^1], environment) is not { } length || !double.IsFinite(length) || (length < 0 && values[^1] is not CssFunction))
            {
                continue;
            }
            else
            {
                size = Math.Max(length, 0);
            }

            values.RemoveAt(values.Count - 1);
            values = TrimWhitespace(values);
            if (values.Count == 0)
            {
                // An auto size that cannot be resolved is ignored
                if (isAuto && autoSize is null)
                    continue;

                return size;
            }

            // A <media-condition> (not a media query: "print" or "not print" are invalid)
            if (MediaQueryEvaluator.MatchesCondition(values, environment) && !(isAuto && autoSize is null))
                return size;
        }

        return defaultSize;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/images.html#allows-auto-sizes</summary>
    internal static bool AllowsAutoSizes(HtmlImageElement image)
    {
        if (!InfraAscii.EqualsIgnoreAsciiCase(image.GetAttributeValue("loading") ?? "", "lazy"))
            return false;

        var sizes = image.GetAttributeValue("sizes") ?? "";
        return InfraAscii.EqualsIgnoreAsciiCase(sizes, "auto") || (sizes.Length >= 5 && InfraAscii.EqualsIgnoreAsciiCase(sizes.AsSpan(0, 5), "auto,"));
    }

    /// <summary>
    /// The concrete object size width of the image (https://html.spec.whatwg.org/multipage/images.html#parse-a-sizes-attribute):
    /// the width of the content box of its replaced box (the image has size containment, see the user agent style sheet, so it
    /// does not depend on the image). The layout is computed when it is out of date.
    /// </summary>
    private static double GetConcreteObjectWidth(HtmlImageElement image)
    {
        var fragments = image.NodeDocument.GetLayoutEngine().GetFragments(image);
        if (fragments.Count > 0 && fragments[0].Box.Kind == Css.Layout.ContainerBoxKind.Replaced)
        {
            var fragment = fragments[0];
            return fragment.ContentBoxRect.Width / fragment.Box.Style.Layout.EffectiveZoom;
        }

        return 300;
    }

    private static List<List<CssComponentValue>> SplitOnCommas(List<CssComponentValue> values)
    {
        var result = new List<List<CssComponentValue>> { new() };
        foreach (var value in values)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Comma })
            {
                result.Add([]);
            }
            else
            {
                result[^1].Add(value);
            }
        }

        return result;
    }

    private static List<CssComponentValue> TrimWhitespace(List<CssComponentValue> values)
    {
        var start = 0;
        var end = values.Count;
        while (start < end && values[start] is CssPreservedToken { Type: CssTokenType.Whitespace })
        {
            start++;
        }

        while (end > start && values[end - 1] is CssPreservedToken { Type: CssTokenType.Whitespace })
        {
            end--;
        }

        return values[start..end];
    }
}

/// <summary>Reads the type and the dimensions of an image from its header (nothing is decoded).</summary>
internal static class ImageHeaders
{
    /// <summary>Returns whether the bytes start with the signature of a raster image format (not an SVG document).</summary>
    public static bool HasRasterSignature(ReadOnlySpan<byte> bytes)
        => bytes.StartsWith(ImageMetadata.PngSignature) || bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8) || bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])
            || bytes.StartsWith("BM"u8) || (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..].StartsWith("WEBP"u8))
            || (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] is 1 or 2 && bytes[3] == 0)
            || (bytes.Length >= 12 && bytes[4..].StartsWith("ftyp"u8));

    /// <summary>
    /// Returns whether the bytes are an image: a known signature (PNG, GIF, JPEG, BMP, WebP, ICO, AVIF), an SVG document,
    /// or another image/* type. The natural size is read from the header (absent dimensions when they cannot be read).
    /// </summary>
    public static bool TryGetImageInfo(ReadOnlySpan<byte> bytes, string contentType, out ImageNaturalSize naturalSize)
    {
        naturalSize = default;
        var essence = InfraAscii.ToAsciiLowercase(contentType.Split(';')[0].Trim());

        // PNG: the IHDR chunk (an image without header is corrupted: its dimensions cannot be obtained)
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            if (bytes.Length >= 16 && !bytes[12..].StartsWith("IHDR"u8))
                return false;

            if (bytes.Length >= 24)
            {
                // The orientation and the resolution of the eXIf chunk (https://w3c.github.io/png/#eXIf)
                naturalSize = ImageNaturalSize.FromEncodedPixels(bytes, ReadUInt32BigEndian(bytes[16..]), ReadUInt32BigEndian(bytes[20..]));
            }

            return true;
        }

        // GIF: the logical screen descriptor
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
        {
            if (bytes.Length >= 10)
            {
                naturalSize = ImageNaturalSize.FromPixels((uint)(bytes[6] | (bytes[7] << 8)), (uint)(bytes[8] | (bytes[9] << 8)));
            }

            return true;
        }

        // JPEG: the first start of frame segment, corrected by the EXIF resolution and rotated by the EXIF orientation
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            if (ReadJpegDimensions(bytes) is var (width, height))
            {
                naturalSize = ImageNaturalSize.FromEncodedPixels(bytes, width, height);
            }

            return true;
        }

        // BMP: the info header
        if (bytes.StartsWith("BM"u8) && bytes.Length >= 26)
        {
            naturalSize = ImageNaturalSize.FromPixels((uint)Math.Abs(ReadInt32LittleEndian(bytes[18..])), (uint)Math.Abs(ReadInt32LittleEndian(bytes[22..])));
            return true;
        }

        // WebP: the VP8, VP8L or VP8X chunk
        if (bytes.Length >= 16 && bytes.StartsWith("RIFF"u8) && bytes[8..].StartsWith("WEBP"u8))
        {
            if (ReadWebPDimensions(bytes) is var (width, height))
            {
                naturalSize = ImageNaturalSize.FromPixels(width, height);
            }

            return true;
        }

        // ICO and CUR: the first image of the directory (0 is 256)
        if (bytes.Length >= 8 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] is 1 or 2 && bytes[3] == 0)
        {
            naturalSize = ImageNaturalSize.FromPixels(bytes[6] == 0 ? 256u : bytes[6], bytes[7] == 0 ? 256u : bytes[7]);
            return true;
        }

        // AVIF and HEIF: the ftyp box, and the image spatial extents property (ispe)
        if (bytes.Length >= 12 && bytes[4..].StartsWith("ftyp"u8))
        {
            var ispe = bytes.IndexOf("ispe"u8);
            if (ispe >= 0 && ispe + 16 <= bytes.Length)
            {
                naturalSize = ImageNaturalSize.FromPixels(ReadUInt32BigEndian(bytes[(ispe + 8)..]), ReadUInt32BigEndian(bytes[(ispe + 12)..]));
            }

            return true;
        }

        if (essence == "image/svg+xml")
            return TryGetSvgNaturalSize(bytes, out naturalSize);

        return false;
    }

    /// <summary>The dimensions of the first start of frame segment of a JPEG image.</summary>
    private static (uint Width, uint Height)? ReadJpegDimensions(ReadOnlySpan<byte> bytes)
    {
        var position = 2;
        while (position + 9 < bytes.Length)
        {
            if (bytes[position] != 0xFF)
                return null;

            var marker = bytes[position + 1];
            if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                position += 2;
                continue;
            }

            var length = (bytes[position + 2] << 8) | bytes[position + 3];

            // The start of frame markers (C0 to CF), except DHT (C4), JPG (C8) and DAC (CC)
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
                return ((uint)((bytes[position + 7] << 8) | bytes[position + 8]), (uint)((bytes[position + 5] << 8) | bytes[position + 6]));

            position += 2 + length;
        }

        return null;
    }

    private static (uint Width, uint Height)? ReadWebPDimensions(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 30)
            return null;

        var chunk = bytes[12..16];
        if (chunk.SequenceEqual("VP8 "u8))
            return ((uint)((bytes[26] | (bytes[27] << 8)) & 0x3FFF), (uint)((bytes[28] | (bytes[29] << 8)) & 0x3FFF));

        if (chunk.SequenceEqual("VP8L"u8))
        {
            var bits = (uint)(bytes[21] | (bytes[22] << 8) | (bytes[23] << 16) | (bytes[24] << 24));
            return ((bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8X"u8))
            return ((uint)(bytes[24] | (bytes[25] << 8) | (bytes[26] << 16)) + 1, (uint)(bytes[27] | (bytes[28] << 8) | (bytes[29] << 16)) + 1);

        return null;
    }

    /// <summary>
    /// An SVG document is an image when it is well-formed and its root is an svg element. Its natural dimensions
    /// (https://svgwg.org/svg2-draft/coords.html#SizingSVGInCSS) are the absolute, non-negative width and height of the
    /// root (a percentage or a negative length is absent), and its natural aspect ratio is the one of the viewBox (whose
    /// width and height must be positive), else the one of the width and height.
    /// </summary>
    private static bool TryGetSvgNaturalSize(ReadOnlySpan<byte> bytes, out ImageNaturalSize naturalSize)
    {
        naturalSize = default;
        try
        {
            var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore, XmlResolver = null };
            using var stream = new MemoryStream(bytes.ToArray());
            using var reader = System.Xml.XmlReader.Create(stream, settings);
            if (reader.MoveToContent() != System.Xml.XmlNodeType.Element || reader.LocalName != "svg")
                return false;

            naturalSize = GetSvgNaturalSize(reader.GetAttribute("width"), reader.GetAttribute("height"), reader.GetAttribute("viewBox"));
            return true;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    /// <summary>Rounds a length within a small epsilon of a whole number.</summary>
    private static double? SnapNearIntegral(double? value) => value is { } v && Math.Abs(v - Math.Round(v, MidpointRounding.AwayFromZero)) < 1e-4 ? Math.Round(v, MidpointRounding.AwayFromZero) : value;

    /// <summary>
    /// Returns the natural dimensions of an svg root element from its width, height and viewBox attributes
    /// (https://svgwg.org/svg2-draft/coords.html#SizingSVGInCSS): the absolute, non-negative width and height (a percentage or a
    /// negative length is absent), and the aspect ratio of the width and height when they are both positive, else the one of
    /// the viewBox (whose width and height must be positive).
    /// </summary>
    public static ImageNaturalSize GetSvgNaturalSize(string? widthValue, string? heightValue, string? viewBox)
    {
        // As in the browsers (https://crbug.com/1392140), a size within a small epsilon of a whole number of pixels is that number
        var width = SnapNearIntegral(ParseSvgLength(widthValue));
        var height = SnapNearIntegral(ParseSvgLength(heightValue));
        double? ratio = width is > 0 && height is > 0 ? width / height : null;
        if (ratio is null && viewBox is not null)
        {
            var parts = viewBox.Split([' ', ',', '\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var viewBoxWidth)
                && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var viewBoxHeight)
                && viewBoxWidth > 0 && viewBoxHeight > 0)
            {
                ratio = viewBoxWidth / viewBoxHeight;
            }
        }

        return new ImageNaturalSize(width, height, ratio);
    }

    /// <summary>Parses an absolute length of the width or height of an SVG root element, in CSS pixels; null for a percentage, a relative length, a negative or an invalid value.</summary>
    private static double? ParseSvgLength(string? value)
    {
        if (value is null)
            return null;

        var text = value.Trim();
        var unitStart = text.Length;
        while (unitStart > 0 && char.IsAsciiLetter(text[unitStart - 1]))
        {
            unitStart--;
        }

        var unit = InfraAscii.ToAsciiLowercase(text[unitStart..]);
        var factor = unit switch
        {
            "" or "px" => 1.0,
            "in" => 96.0,
            "cm" => 96.0 / 2.54,
            "mm" => 96.0 / 25.4,
            "q" => 96.0 / 101.6,
            "pt" => 96.0 / 72,
            "pc" => 16.0,
            "em" => 16.0,
            "ex" => 16.0 * Css.Fonts.BundledFonts.DefaultRatios.XHeight,
            _ => double.NaN,
        };

        if (double.IsNaN(factor) || !double.TryParse(text[..unitStart], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number < 0)
            return null;

        return number * factor;
    }

    private static uint ReadUInt32BigEndian(ReadOnlySpan<byte> bytes) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes);

    private static int ReadInt32LittleEndian(ReadOnlySpan<byte> bytes) => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes);
}

public partial class Document
{
    /// <summary>The list of available images (https://html.spec.whatwg.org/multipage/images.html#list-of-available-images): the natural size and the image of the requests.</summary>
    internal Dictionary<AvailableImageKey, AvailableImage>? AvailableImages { get; set; }

    /// <summary>
    /// Whether an img element of the document has used srcset or picture: the images are then updated when the environment
    /// changes. The style engine detects the changes when the styles are used; the rendering updates of the event loop of the
    /// document check the environment too, so that the images react when nothing else reads the styles.
    /// </summary>
    internal bool MayHaveImagesReactingToEnvironment
    {
        get;
        set
        {
            field = value;
            if (value && !_checksEnvironmentOfImages && EventLoop is { } eventLoop)
            {
                _checksEnvironmentOfImages = true;
                eventLoop.AddDocumentWithImagesReactingToEnvironment(this);
            }
        }
    }

    private bool _checksEnvironmentOfImages;
    private bool _reactToEnvironmentChangesQueued;

    /// <summary>
    /// Queues a task that makes the img elements of the document that use srcset or picture react to a change of the
    /// environment (https://html.spec.whatwg.org/multipage/images.html#img-environment-changes), see
    /// <see cref="HtmlImageElement.ReactToEnvironmentChanges"/>.
    /// </summary>
    internal void QueueReactToEnvironmentChangesOfImages()
    {
        if (!MayHaveImagesReactingToEnvironment || _reactToEnvironmentChangesQueued || EventLoop is null)
            return;

        _reactToEnvironmentChangesQueued = true;
        QueueElementTask(() =>
        {
            _reactToEnvironmentChangesQueued = false;
            if (!IsFullyActive)
                return;

            var images = new List<HtmlImageElement>();
            foreach (var node in ShadowIncludingInclusiveDescendants())
            {
                if (node is HtmlImageElement image && image.UsesSrcsetOrPicture())
                {
                    images.Add(image);
                }
            }

            foreach (var image in images)
            {
                image.ReactToEnvironmentChanges();
            }
        });
    }
}

/// <summary>An image of the list of available images: its natural size, the image the painting draws, and the length of its data (for the paint timing).</summary>
internal sealed record AvailableImage(ImageNaturalSize NaturalSize, PaintImage? Image, long ContentLength);

public partial class HtmlSourceElement
{
    // The relevant mutations of the img elements of a parent picture element (https://html.spec.whatwg.org/multipage/images.html#relevant-mutations)
    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is null && localName is "srcset" or "sizes" or "media" or "type" or "width" or "height" && ParentNode is HtmlPictureElement picture)
        {
            UpdateFollowingImages(picture, this);
        }
    }

    /// <summary>
    /// The source HTML element insertion steps (https://html.spec.whatwg.org/multipage/embedded-content.html#the-source-element):
    /// as in the browsers, inserting the picture parent (or another ancestor) is not a relevant mutation of its img elements,
    /// and the img elements that precede the element are not updated (WPT relevant-mutations.html).
    /// </summary>
    internal override void InsertionSteps()
    {
        base.InsertionSteps();
        InvokeResourceSelectionOfParentMediaElement();
        if (ParentNode is HtmlPictureElement picture && IsInsertionRoot)
        {
            UpdateFollowingImages(picture, this);
        }
    }

    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);

        // The source element was a previous sibling of the img elements (it is removed: all the img elements of the picture are updated)
        if (isSubtreeRoot && oldAncestor is HtmlPictureElement picture)
        {
            UpdateFollowingImages(picture, start: null);
        }
    }

    /// <summary>
    /// The source HTML element moving steps (https://html.spec.whatwg.org/multipage/embedded-content.html#the-source-element):
    /// the img elements of the old picture parent are updated, and the ones that follow the element in its new picture parent,
    /// as for the insertion steps.
    /// </summary>
    internal override void MovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.MovingSteps(isSubtreeRoot, oldAncestor);
        InvokeResourceSelectionOfParentMediaElement();
        if (!isSubtreeRoot)
            return;

        if (oldAncestor is HtmlPictureElement oldPicture)
        {
            UpdateFollowingImages(oldPicture, start: null);
        }

        if (ParentNode is HtmlPictureElement picture && !ReferenceEquals(picture, oldAncestor))
        {
            UpdateFollowingImages(picture, this);
        }
    }

    /// <summary>Updates the image data of the img children of the picture element that follow <paramref name="start"/> (all of them when it is null).</summary>
    private static void UpdateFollowingImages(HtmlPictureElement picture, Node? start)
    {
        var following = start is null;
        foreach (var child in picture.ChildNodes)
        {
            if (ReferenceEquals(child, start))
            {
                following = true;
            }
            else if (following && child is HtmlImageElement image)
            {
                image.UpdateImageData();
            }
        }
    }
}
