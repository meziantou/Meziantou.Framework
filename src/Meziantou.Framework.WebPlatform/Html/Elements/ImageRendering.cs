using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The rendering of the img elements (https://html.spec.whatwg.org/multipage/rendering.html#images-3): the natural size of
/// the replaced box, the alternative text of a broken image, and the geometry exposed to scripts.
/// </summary>
public partial class HtmlImageElement
{
    /// <summary>
    /// The size of the broken image icon (https://html.spec.whatwg.org/multipage/rendering.html#images-3: "icons could be 16
    /// pixels by 16 pixels square"), the natural size of an image that failed to load and has no alt attribute.
    /// </summary>
    private const double BrokenImageIconSize = 16;

    /// <summary>Whether the box tree of the last layout rendered the element as its alternative text (see <see cref="GetAltTextRendering"/>).</summary>
    internal bool BoxTreeRendersAltText { get; set; }

    /// <summary>
    /// Whether the user agent "has reason to believe that the image will become available and be rendered in due course": an
    /// update of the image data is pending or the image is being fetched.
    /// </summary>
    private bool IsImageExpectedToChange => _updatePending || _pendingRequest is not null || (_currentRequest.State == ImageRequestState.Unavailable && _currentRequest.CurrentUrl.Length > 0);

    /// <summary>Whether the element represents an image (https://html.spec.whatwg.org/multipage/images.html#img-available).</summary>
    private bool RepresentsImage => _currentRequest.State is ImageRequestState.CompletelyAvailable or ImageRequestState.PartiallyAvailable;

    /// <summary>
    /// Returns the alternative text of the element when it is rendered as a non-replaced phrasing element whose content is the
    /// text (https://html.spec.whatwg.org/multipage/rendering.html#images-3): "If the element is an img element that represents
    /// some text and the user agent does not expect this to change", else null (the element is a replaced element). An image
    /// that is not available and whose alt attribute is not empty, in a document in quirks mode, is a replaced element when it
    /// has natural dimensions (from the dimension attributes or the CSS rules).
    /// </summary>
    internal string? GetAltTextRendering(ComputedStyle style)
    {
        if (RepresentsImage || IsImageExpectedToChange || GetAttributeValue("alt") is not { Length: > 0 } alt)
            return null;

        if (NodeDocument.Mode == DocumentMode.Quirks && (HasAttributeWithLocalName("width") || HasAttributeWithLocalName("height") || !style.Layout.Box.Width.IsAuto || !style.Layout.Box.Height.IsAuto))
            return null;

        return alt;
    }

    /// <summary>
    /// Returns the natural dimensions of the replaced box of the element (https://html.spec.whatwg.org/multipage/rendering.html#images-3),
    /// in CSS pixels: the density-corrected natural dimensions of the image when it is available (rotated by its EXIF orientation
    /// unless <c>image-orientation</c> is <c>none</c>), else 0 (the image is being obtained, or the element represents nothing),
    /// or the size of the broken image icon when the image failed to load and the element has no alt attribute.
    /// </summary>
    internal ImageNaturalSize GetRenderedNaturalSize(bool fromImageOrientation)
    {
        if (RepresentsImage)
        {
            var request = _currentRequest;
            var natural = request.NaturalSize;

            // https://drafts.csswg.org/css-images-3/#valdef-image-orientation-none: "none" does not apply to the images that
            // are not CORS-same-origin
            if (!fromImageOrientation && natural.OrientationSwapsAxes && IsCorsSameOrigin(request))
            {
                natural = natural.WithoutOrientation();
            }

            return natural.Scale(1 / request.CurrentPixelDensity);
        }

        if (!IsImageExpectedToChange && GetAttributeValue("alt") is null && _currentRequest.CurrentUrl.Length > 0)
        {
            // The icon gives no natural aspect ratio: the one of the dimension attributes applies (WPT img-aspect-ratio.html)
            return new ImageNaturalSize(BrokenImageIconSize, BrokenImageIconSize, null);
        }

        return new ImageNaturalSize(0, 0, null);
    }

    /// <summary>Whether the response of an image request is CORS-same-origin: a CORS request, or a URL of the origin of the document.</summary>
    private bool IsCorsSameOrigin(ImageRequest request)
    {
        if (request.Key is { CorsState: not 0 })
            return true;

        if (UrlParser.Parse(request.Key?.Url ?? request.CurrentUrl, baseUrl: null) is not { } url)
            return false;

        if (url.Scheme == "data")
            return true;

        var origin = NodeDocument.Origin;
        return !origin.IsOpaque && OriginRecord.IsSameOrigin(origin, OriginRecord.FromUrl(url));
    }

    /// <summary>
    /// Marks the layout of the document as out of date after a change of the image data (the natural size, the state of the
    /// request): the box tree is built again when the element starts or stops being rendered as its alternative text.
    /// </summary>
    private void InvalidateRendering()
    {
        var document = NodeDocument;
        if (document.LayoutEngineOrNull is null)
            return;

        var rendersAltText = ComputedStyleDeclarations.IsBeingRendered(this) && GetAltTextRendering(document.GetStyleEngine().GetStyle(this)) is not null;
        document.InvalidateLayout(rebuildBoxTree: rendersAltText != BoxTreeRendersAltText);
    }

    /// <summary>
    /// The rendered size of the content box of the element (https://html.spec.whatwg.org/multipage/embedded-content-other.html#dimension-attributes:
    /// "the rendered width and height of the image, in CSS pixels, if the image is being rendered"), in the coordinates of the
    /// element (divided by its effective zoom), or null when it does not generate a box. The layout is computed when it is out of date.
    /// </summary>
    private (double Width, double Height)? GetRenderedSize()
    {
        if (!ComputedStyleDeclarations.IsBeingRendered(this))
            return null;

        var fragments = NodeDocument.GetLayoutEngine().GetFragments(this);
        if (fragments.Count == 0)
            return null;

        // The fragments of an alternative text split over several lines are put side by side
        double width = 0;
        double height = 0;
        foreach (var fragment in fragments)
        {
            var content = fragment.ContentBoxRect;
            width += content.Width;
            height = Math.Max(height, content.Height);
        }

        var zoom = fragments[0].Box.Style.Layout.EffectiveZoom;
        return (width / zoom, height / zoom);
    }

    /// <summary>
    /// Returns the position of the border box of the first box of the element relative to the initial containing block
    /// (https://drafts.csswg.org/cssom-view/#dom-htmlimageelement-x), in the coordinates of the root (the effective zoom is not
    /// removed, as in the browsers), or (0, 0) when it does not generate a box. The transforms and the scroll positions are ignored.
    /// </summary>
    private (int X, int Y) GetLayoutPosition()
    {
        if (!ComputedStyleDeclarations.IsBeingRendered(this))
            return (0, 0);

        var fragments = NodeDocument.GetLayoutEngine().GetFragments(this);
        if (fragments.Count == 0)
            return (0, 0);

        var offset = CssomViewGeometry.GetPositionInInitialContainingBlock(fragments[0]);
        return ((int)offset.X, (int)offset.Y);
    }
}
