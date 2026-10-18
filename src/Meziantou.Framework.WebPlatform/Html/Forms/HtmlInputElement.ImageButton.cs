using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The image of an input element in the Image Button state (https://html.spec.whatwg.org/multipage/input.html#image-button-state-(type=image)):
/// the image given by its src attribute is fetched, and the element represents it once it is available (a replaced element
/// rendered as an image, https://html.spec.whatwg.org/multipage/rendering.html#images-3); otherwise it represents a submit
/// button labeled with its alternative text.
/// </summary>
public partial class HtmlInputElement
{
    /// <summary>The request of the image of the image button, or null when no image was fetched.</summary>
    private ImageRequest? _imageButtonRequest;

    /// <summary>The value of the src attribute the last time the element was in the Image Button state (null before).</summary>
    private string? _imageButtonSource;

    private bool _imageButtonDelaysLoad;
    private Document? _imageButtonDelayedDocument;

    /// <summary>Whether the box tree of the last layout rendered the element as its alternative text (see <see cref="GetImageButtonAltTextRendering"/>).</summary>
    internal bool BoxTreeRendersImageButtonAltText { get; set; }

    /// <summary>Whether the image is being fetched: the user agent "has reason to believe that the image will become available and be rendered in due course".</summary>
    private bool IsImageButtonImageExpectedToChange => _imageButtonRequest is { State: ImageRequestState.Unavailable };

    /// <summary>
    /// Returns the alternative text of an image button rendered as a non-replaced phrasing element whose content is the text,
    /// else null (the element is a replaced element: its image, or a button labeled with the text). The rendering rules
    /// (https://html.spec.whatwg.org/multipage/rendering.html#images-3) give this rendering to the img elements only; as in Chrome
    /// and Firefox, an image button whose image is not available and is not being fetched, and whose alt attribute is not
    /// empty, is rendered as its text (WPT input-image-inline-alt.html), with the same exception in quirks mode.
    /// </summary>
    internal string? GetImageButtonAltTextRendering(ComputedStyle style)
    {
        if (_type != InputType.Image || ImageButtonRepresentsImage || IsImageButtonImageExpectedToChange || GetAttributeValue("alt") is not { Length: > 0 } alt)
            return null;

        if (NodeDocument.Mode == DocumentMode.Quirks && (HasAttributeWithLocalName("width") || HasAttributeWithLocalName("height") || !style.Layout.Box.Width.IsAuto || !style.Layout.Box.Height.IsAuto))
            return null;

        return alt;
    }

    /// <summary>
    /// The label of the button the element represents when it does not represent an image: "a submit button whose label is
    /// given by the value of the alt attribute" while the image is being fetched (no text without alt attribute, as the img
    /// elements); otherwise, as in Chrome, the alt attribute, else the value attribute, else "Submit".
    /// </summary>
    internal string GetImageButtonLabel() => IsImageButtonImageExpectedToChange ? GetAttributeValue("alt") ?? "" : GetAttributeValue("alt") ?? GetAttributeValue("value") ?? "Submit";

    /// <summary>Whether the element is in the Image Button state and its image is available: it is rendered as the image.</summary>
    internal bool ImageButtonRepresentsImage => _type == InputType.Image && _imageButtonRequest is { State: ImageRequestState.CompletelyAvailable };

    /// <summary>The image the painting draws when the element represents an image, or null.</summary>
    internal PaintImage? GetImageButtonPaintImage() => ImageButtonRepresentsImage ? _imageButtonRequest!.Image : null;

    /// <summary>
    /// Returns the natural dimensions of the image of the element (see <see cref="ImageButtonRepresentsImage"/>), in CSS pixels,
    /// rotated by its EXIF orientation unless <c>image-orientation</c> is <c>none</c> (for a CORS-same-origin image).
    /// </summary>
    internal ImageNaturalSize GetImageButtonNaturalSize(bool fromImageOrientation)
    {
        if (!ImageButtonRepresentsImage)
            return default;

        var request = _imageButtonRequest!;
        var natural = request.NaturalSize;
        return !fromImageOrientation && natural.OrientationSwapsAxes && request.Image?.IsCorsSameOrigin is true ? natural.WithoutOrientation() : natural;
    }

    /// <summary>
    /// Fetches the image when "the input element's type attribute is first set to the Image Button state [...] and the src
    /// attribute is present", when it "is changed back to the Image Button state, and the src attribute is present, and its
    /// value has changed since the last time the type attribute was in the Image Button state", or when "the type attribute
    /// is in the Image Button state, and the src attribute is set or changed".
    /// </summary>
    private void OnImageButtonMutation(bool typeChanged)
    {
        if (_type != InputType.Image)
            return;

        var src = GetAttributeValue("src");
        if (typeChanged && (src is null || src == _imageButtonSource) && _imageButtonRequest is not null)
        {
            // The state changed back without new source: the image of the previous request is kept
            _imageButtonSource = src;
            InvalidateImageButtonRendering();
            return;
        }

        _imageButtonSource = src;
        FetchImageButtonImage(src);
    }

    /// <summary>
    /// The steps that fetch the image (https://html.spec.whatwg.org/multipage/input.html#image-button-state-(type=image)): a
    /// request whose destination is "image", initiator type "input" and credentials mode "include"; the load event is fired
    /// when the image is available, the error event when the fetch fails or the response is not a supported image. Fetching the
    /// image delays the load event of the document. Nothing is fetched when the src attribute is absent or empty: the element
    /// then represents a button (as in the browsers, the image of the previous source is not kept).
    /// </summary>
    private void FetchImageButtonImage(string? src)
    {
        if (_imageButtonRequest is { } previous)
        {
            previous.Aborted = true;
            _imageButtonRequest = null;
            SetImageButtonDelayingTheLoadEvent(false);
            InvalidateImageButtonRendering();
        }

        var document = NodeDocument;
        if (string.IsNullOrEmpty(src) || !document.IsFullyActive || document.EventLoop is null || Reflection.EncodingParseUrl(document, src) is not { } url)
            return;

        var request = new ImageRequest(url.Serialize(), 1) { Key = new AvailableImageKey(url.Serialize(), CorsState: 0, Origin: null) };
        _imageButtonRequest = request;
        SetImageButtonDelayingTheLoadEvent(true);

        // As in Chrome and Safari, an image of the list of available images (https://html.spec.whatwg.org/multipage/images.html#list-of-available-images)
        // is available at once (e.g. an image preloaded by an img element), the load event is fired by a task
        if (document.AvailableImages is { } availableImages && availableImages.TryGetValue(request.Key.Value, out var availableImage))
        {
            request.State = ImageRequestState.CompletelyAvailable;
            request.NaturalSize = availableImage.NaturalSize;
            request.Image = availableImage.Image;
            InvalidateImageButtonRendering();
            document.QueueElementTask(() =>
            {
                if (request.Aborted)
                    return;

                document.ProcessImageThatFinishedLoading(this, request.CurrentUrl, availableImage.NaturalSize, availableImage.Image is null ? long.MaxValue : availableImage.ContentLength);
                FireImageButtonEvent("load");
            });
            return;
        }

        InvalidateImageButtonRendering();
        if (document.BrowsingContext?.Options.DocumentLoader is null && url.Scheme is not ("data" or "blob"))
        {
            // Nothing can be fetched: a network error
            document.QueueElementTask(() => ProcessImageButtonResponse(request, resource: null));
            return;
        }

        var fetchRequest = SubresourceFetching.CreateRequest(document, url, RequestDestination.Image, cors: false, referrerPolicy: "", this);
        var timing = ResourceTiming.Start(document, fetchRequest, LocalName);
        Fetching.Fetch(fetchRequest, ProcessFetchResponse, task => document.QueueElementTask(task));

        void ProcessFetchResponse(FetchResponse response)
        {
            if (fetchRequest.IsSentToLoader)
            {
                timing?.Report(response);
            }

            if (document.EventLoop is null)
            {
                SetImageButtonDelayingTheLoadEvent(false);
                return;
            }

            ProcessImageButtonResponse(request, response.ToDocumentResource());
        }
    }

    internal override void AdoptingSteps(Document oldDocument)
    {
        base.AdoptingSteps(oldDocument);

        // As for the img elements, the image is fetched again for the new node document (the request of the old one is aborted;
        // the elements created by the fragment parsing algorithm in an inert document are fetched when they are adopted)
        if (_type == InputType.Image && (_imageButtonRequest is not null || GetAttributeValue("src") is not null))
        {
            _imageButtonSource = GetAttributeValue("src");
            FetchImageButtonImage(_imageButtonSource);
        }
    }

    /// <summary>The steps run with the response of the image (null for a network error): the load or error event is fired at the element.</summary>
    private void ProcessImageButtonResponse(ImageRequest request, DocumentResource? resource)
    {
        if (request.Aborted)
            return;

        var document = NodeDocument;
        if (!HtmlImageElement.TryCreateImage(document, request.CurrentUrl, resource, resource?.IsCorsSameOrigin ?? false, out var bytes, out var naturalSize, out var image))
        {
            request.State = ImageRequestState.Broken;
            FireImageButtonEvent("error");
            return;
        }

        request.State = ImageRequestState.CompletelyAvailable;
        request.NaturalSize = naturalSize;
        request.Image = image;
        if (request.Key is { } key)
        {
            (document.AvailableImages ??= [])[key] = new AvailableImage(naturalSize, image, bytes.Length);
        }

        // https://w3c.github.io/paint-timing/#sec-modifications-HTML: the image of the element finished loading (element timing)
        document.ProcessImageThatFinishedLoading(this, request.CurrentUrl, naturalSize, bytes.Length);
        FireImageButtonEvent("load");
    }

    private void FireImageButtonEvent(string type)
    {
        InvalidateImageButtonRendering();
        FireEvent(new Event(type));
        SetImageButtonDelayingTheLoadEvent(false);
    }

    /// <summary>
    /// The natural size or the state of the image (or the alternative text) changed: the replaced box of the element is laid out
    /// again; the box tree is built again when the element starts or stops being rendered as its alternative text, or when that
    /// text may have changed.
    /// </summary>
    private void InvalidateImageButtonRendering(bool altTextChanged = false)
    {
        var document = NodeDocument;
        if (document.LayoutEngineOrNull is null)
            return;

        var rendersAltText = _type == InputType.Image && ComputedStyleDeclarations.IsBeingRendered(this) && GetImageButtonAltTextRendering(document.GetStyleEngine().GetStyle(this)) is not null;
        document.InvalidateLayout(rebuildBoxTree: rendersAltText != BoxTreeRendersImageButtonAltText || (altTextChanged && rendersAltText));
    }

    /// <summary>"Fetching the image must delay the load event of the element's node document".</summary>
    private void SetImageButtonDelayingTheLoadEvent(bool value)
    {
        if (_imageButtonDelaysLoad == value)
            return;

        _imageButtonDelaysLoad = value;
        if (value)
        {
            _imageButtonDelayedDocument = NodeDocument;
            _imageButtonDelayedDocument.ImagesDelayingTheLoadEvent++;
            return;
        }

        var document = _imageButtonDelayedDocument ?? NodeDocument;
        _imageButtonDelayedDocument = null;
        document.ImagesDelayingTheLoadEvent--;
        document.TryQueueLoadEvent();
    }

    /// <summary>
    /// The dimensions of an image button (https://html.spec.whatwg.org/multipage/input.html#dom-input-width): the rendered size
    /// of its content box when it is being rendered, else the natural dimensions of its image (the default sizing algorithm
    /// with a default object size of 300x150 completes the missing ones) replaced by the width and height attributes; 0 for
    /// the other states.
    /// </summary>
    private (uint Width, uint Height) GetImageButtonDimensions()
    {
        if (_type != InputType.Image)
            return (0, 0);

        if (ComputedStyleDeclarations.IsBeingRendered(this) && NodeDocument.GetLayoutEngine().GetFragments(this) is [var fragment, ..])
        {
            var content = fragment.ContentBoxRect;
            var zoom = fragment.Box.Style.Layout.EffectiveZoom;
            return (HtmlImageElement.ToCssPixels(content.Width / zoom), HtmlImageElement.ToCssPixels(content.Height / zoom));
        }

        uint width = 0;
        uint height = 0;
        if (ImageButtonRepresentsImage)
        {
            var (naturalWidth, naturalHeight) = _imageButtonRequest!.NaturalSize.GetConcreteSize();
            width = HtmlImageElement.ToCssPixels(naturalWidth);
            height = HtmlImageElement.ToCssPixels(naturalHeight);
        }

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
}
