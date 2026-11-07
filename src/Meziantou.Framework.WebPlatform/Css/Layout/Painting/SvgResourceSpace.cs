using System.Globalization;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The coordinates in which an SVG resource (a filter, a mask, a clipPath element, https://drafts.fxtf.org/css-masking/,
/// https://drafts.fxtf.org/filter-effects/) is applied to an element: its user space (in user units) and its object bounding
/// box, mapped to the coordinates of the painting by a uniform scale and a translation. For an element with a CSS box, the user
/// space has its origin at the top left corner of the border box and the object bounding box is the border box
/// (https://drafts.fxtf.org/css-masking/#element-attrdef-clippath-clippathunits); the user units are CSS px multiplied by the
/// effective zoom of the element.
/// </summary>
/// <param name="BoundingBox">The object bounding box, in user units.</param>
/// <param name="Viewport">The size of the viewport that the percentages of the lengths in user space refer to, in user units.</param>
/// <param name="Origin">The position of the origin of the user space in the coordinates of the painting.</param>
/// <param name="Scale">The size of a user unit in the coordinates of the painting.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct SvgResourceSpace(PhysicalRect BoundingBox, PhysicalSize Viewport, PhysicalOffset Origin, double Scale)
{
    /// <summary>Returns the resource space of an element with a CSS box whose border box is at <paramref name="borderBox"/> in the coordinates of the painting.</summary>
    public static SvgResourceSpace ForBox(PhysicalRect borderBox, double zoom)
    {
        zoom = zoom > 0 ? zoom : 1;
        var size = new PhysicalSize(borderBox.Width / zoom, borderBox.Height / zoom);
        return new SvgResourceSpace(new PhysicalRect(PhysicalOffset.Zero, size), size, borderBox.Offset, zoom);
    }

    /// <summary>
    /// Returns the resource space of an element with a CSS box whose object bounding box is another rectangle than its border box
    /// (e.g. the bounding box of the box and its descendants), both in the coordinates of the painting. The percentages of the
    /// lengths in user space refer to the border box (as in the browsers).
    /// </summary>
    public static SvgResourceSpace ForBox(PhysicalRect borderBox, PhysicalRect boundingBox, double zoom)
    {
        zoom = zoom > 0 ? zoom : 1;
        var box = new PhysicalRect((boundingBox.X - borderBox.X) / zoom, (boundingBox.Y - borderBox.Y) / zoom, boundingBox.Width / zoom, boundingBox.Height / zoom);
        return new SvgResourceSpace(box, new PhysicalSize(borderBox.Width / zoom, borderBox.Height / zoom), borderBox.Offset, zoom);
    }

    /// <summary>
    /// Returns the resource space of a foreignObject element from the one of its box (https://svgwg.org/svg2-draft/embedded.html#ForeignObjectElement):
    /// the origin of its user space and the viewport are the ones of its nearest SVG viewport (<paramref name="viewport"/>, in
    /// the coordinates of the painting), its object bounding box is at its <c>x</c> and <c>y</c>.
    /// </summary>
    public SvgResourceSpace InSvgViewport(PhysicalRect viewport)
    {
        var position = new PhysicalOffset((Origin.X - viewport.X) / Scale, (Origin.Y - viewport.Y) / Scale);
        return new SvgResourceSpace(BoundingBox.Translate(position), new PhysicalSize(viewport.Width / Scale, viewport.Height / Scale), viewport.Offset, Scale);
    }

    /// <summary>Maps a point of the user space to the coordinates of the painting.</summary>
    public PhysicalOffset ToPainting(double x, double y) => new(Origin.X + (x * Scale), Origin.Y + (y * Scale));

    /// <summary>Maps a rectangle of the user space to the coordinates of the painting.</summary>
    public PhysicalRect ToPainting(PhysicalRect rect) => new(Origin.X + (rect.X * Scale), Origin.Y + (rect.Y * Scale), rect.Width * Scale, rect.Height * Scale);

    /// <summary>The transform from the user space to the coordinates of the painting.</summary>
    public SvgAffine UserToPainting => new(Scale, 0, 0, Scale, Origin.X, Origin.Y);

    /// <summary>
    /// Resolves the region of a resource element (<c>x</c>, <c>y</c>, <c>width</c>, <c>height</c>) in user units: fractions or
    /// percentages of the object bounding box for <c>objectBoundingBox</c> units, else lengths of the user space (percentages of the viewport).
    /// </summary>
    public PhysicalRect ResolveRegion(Element element, bool objectBoundingBox, string defaultX, string defaultY, string defaultWidth, string defaultHeight)
    {
        var x = ResolveCoordinate(element, element.GetAnimatedAttributeValue("x") ?? defaultX, objectBoundingBox, horizontal: true, position: true);
        var y = ResolveCoordinate(element, element.GetAnimatedAttributeValue("y") ?? defaultY, objectBoundingBox, horizontal: false, position: true);
        var width = ResolveCoordinate(element, element.GetAnimatedAttributeValue("width") ?? defaultWidth, objectBoundingBox, horizontal: true, position: false);
        var height = ResolveCoordinate(element, element.GetAnimatedAttributeValue("height") ?? defaultHeight, objectBoundingBox, horizontal: false, position: false);
        return new PhysicalRect(x, y, width, height);
    }

    /// <summary>Resolves a coordinate or a size of a resource element in user units (see <see cref="ResolveRegion"/>).</summary>
    public double ResolveCoordinate(Element element, string value, bool objectBoundingBox, bool horizontal, bool position)
    {
        value = value.Trim();
        var box = BoundingBox;
        if (objectBoundingBox)
        {
            double fraction;
            if (value.EndsWith('%', StringComparison.Ordinal) && double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage))
            {
                fraction = percentage / 100;
            }
            else if (SvgUserSpace.ResolveLength(element, value, 1) is { } number)
            {
                // A number is a fraction of the bounding box; the percentages of a math function too (calc(25% + 0.25) is 0.5)
                fraction = number;
            }
            else
            {
                return 0;
            }

            var size = horizontal ? box.Width : box.Height;
            return position ? (horizontal ? box.X : box.Y) + (fraction * size) : fraction * size;
        }

        var basis = horizontal ? Viewport.Width : Viewport.Height;
        return SvgUserSpace.ResolveLength(element, value, basis) ?? 0;
    }

    /// <summary>Whether the units attribute of a resource element is <c>objectBoundingBox</c> (<paramref name="defaultIsObjectBoundingBox"/> when it is missing or invalid).</summary>
    public static bool IsObjectBoundingBox(Element element, string attribute, bool defaultIsObjectBoundingBox) => element.GetAnimatedAttributeValue(attribute) switch
    {
        "objectBoundingBox" => true,
        "userSpaceOnUse" => false,
        _ => defaultIsObjectBoundingBox,
    };
}

/// <summary>The references of the properties to the SVG resource elements (<c>url(#id)</c>).</summary>
internal static class SvgResources
{
    /// <summary>
    /// Returns the element referenced by a <c>url()</c> component of a property of <paramref name="element"/>: an element of its
    /// document (a local reference, https://drafts.csswg.org/css-values-4/#local-urls, or a URL of the document with a fragment), or
    /// an element of the resource document of another URL once it is loaded (https://svgwg.org/svg2-draft/linking.html#processingURL:
    /// the document is fetched when the property is required for rendering the element, see
    /// <see cref="Css.Om.CssImageFetching"/>; a document without browsing context, e.g. an SVG image or a resource document, is
    /// processed in the secure static mode and fetches nothing). Returns null when it references no element (no fragment, a
    /// document that is not loaded, no element with this id).
    /// </summary>
    public static Element? GetReferencedElement(Element element, Css.Syntax.CssComponentValue value)
    {
        if (!Css.Values.CssUrlModifiers.TryGetUrl(value, out var url, out var modifiers))
            return null;

        // https://svgwg.org/svg2-draft/linking.html#processingURL: the leading and trailing ASCII white space is stripped (by the URL parser)
        url = url.Trim(' ', '\t', '\n', '\f', '\r');
        var hash = url.IndexOf('#', StringComparison.Ordinal);
        if (hash < 0 || hash == url.Length - 1)
            return null;

        // The fragment is percent-decoded (https://html.spec.whatwg.org/multipage/browsing-the-web.html#find-a-potential-indicated-element)
        var id = url[(hash + 1)..];
        if (id.Contains('%', StringComparison.Ordinal))
        {
            id = System.Text.Encoding.UTF8.GetString(Urls.PercentEncoding.PercentDecode(id));
        }

        var document = element.NodeDocument;
        if (hash > 0)
        {
            if (Urls.UrlParser.Parse(url, document.DocumentBaseUrl) is not { } parsed)
                return null;

            // A reference to an element of another document: the element of its resource document
            if (parsed.Serialize(excludeFragment: true) != document.UrlRecord.Serialize(excludeFragment: true))
                return document.BrowsingContext is not null && document.SvgResourceDocuments?.GetForProperty(document, parsed, modifiers) is { } resource ? resource.GetElementById(id) : null;
        }

        return element.GetRootNode() is ShadowRoot shadowRoot && shadowRoot.GetElementById(id) is { } scoped ? scoped : document.GetElementById(id);
    }
}
