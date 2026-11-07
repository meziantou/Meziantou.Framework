using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The paints of the nodes of a document found in its display list (see <see cref="ContentfulPaintScanner"/>), for the paint
/// timing (https://w3c.github.io/paint-timing/).
/// </summary>
internal sealed class ContentfulPaintScan
{
    /// <summary>Whether something other than the default background of the canvas is painted (https://w3c.github.io/paint-timing/#first-paint).</summary>
    public bool HasFirstPaint { get; set; }

    /// <summary>Whether a paintable element of the document is contentful (https://w3c.github.io/paint-timing/#first-contentful-paint).</summary>
    public bool HasContentfulPaint { get; set; }

    /// <summary>
    /// The nodes whose non-empty text is painted (a text node, or an element for its generated content and the text of its form
    /// control), with whether a glyph of the text is painted with a visible color (the text, its shadow or its stroke).
    /// </summary>
    public Dictionary<Node, bool> Texts { get; } = [];

    /// <summary>The elements that paint an image (a replaced image, a poster frame, a CSS image), with the bounds of the painted images by URL (the empty string when the URL is not known, e.g. an SVG image).</summary>
    public Dictionary<Element, Dictionary<string, PhysicalRect>> Images { get; } = [];
}

/// <summary>
/// Finds the contentful paints of a document in its display list: the "paint" of the paint timing
/// (https://w3c.github.io/paint-timing/#paint) is the painting of the document into its display list by the rendering update,
/// whether or not a <see cref="RenderingBackend"/> rasterizes it. A drawing operation is the paint of the node of its
/// <see cref="DisplayItem.Source"/>; the operations of the nested documents (whose nodes belong to another document) and the
/// default background of the canvas (no node) are ignored. An operation is paintable
/// (https://w3c.github.io/paint-timing/#paintable) when it is not in a group whose opacity is 0 and its bounds, with the
/// transforms, intersect the scrolling area of the document (the clips do not apply: the clipped contents can be scrolled into
/// view); the painting omits the boxes whose visibility is not visible and the stacking contexts whose back face is hidden. It
/// is contentful (https://w3c.github.io/paint-timing/#contentful) when it draws glyphs of a non-empty text (also in a
/// transparent color: the painting emits the glyphs of a transparent text for this purpose), an image (a raster image, an SVG
/// image) or the graphics of an SVG element.
/// </summary>
internal static class ContentfulPaintScanner
{
    /// <param name="list">The display list of the document.</param>
    /// <param name="document">The document.</param>
    /// <param name="scrollingArea">The scrolling area of the viewport, in the coordinates of the display list.</param>
    /// <param name="scrollport">The scrollport of the viewport in the display list (the viewport without the gutters of its scrollbars).</param>
    public static ContentfulPaintScan Scan(DisplayList list, Document document, PhysicalRect scrollingArea, PhysicalRect scrollport)
    {
        var result = new ContentfulPaintScan();
        var viewport = scrollport;
        var state = new ScanState(DisplayMatrix.Identity, Hidden: false, Clip: null, ImageOwner: null, ImageRect: null);
        var stack = new Stack<ScanState>();
        foreach (var item in list.Items)
        {
            switch (item)
            {
                case SaveItem save:
                    stack.Push(state);

                    // The content of another document drawn for an element (an SVG image) is the paint of the image of the element
                    if (save.Source is Element owner && ReferenceEquals(owner.NodeDocument, document))
                    {
                        state = state with { ImageOwner = owner, ImageRect = null };
                    }

                    continue;

                case SaveLayerItem layer:
                    stack.Push(state);
                    state = state with { Hidden = state.Hidden || layer.Opacity <= 0 };
                    continue;

                case RestoreItem:
                    if (stack.Count > 0)
                    {
                        state = stack.Pop();
                    }

                    continue;

                case TransformItem transformItem:
                    state = state with { Transform = state.Transform.PreConcat(transformItem.Matrix) };
                    continue;

                case ClipRectItem clip:
                {
                    var clipBounds = TransformBounds(state.Transform, clip.Rect);

                    // The clip of the document to the scrollport of the viewport (see Painter.Paint) is the viewport, not a clip
                    // of the contents: the painted rectangles of the images are not reduced to it
                    if (state.Clip is null && clip.Source is null && IsScrollportClip(clipBounds, scrollport))
                        continue;

                    // The rectangle of an SVG image drawn for an element is its clip
                    if (state.ImageOwner is not null && state.ImageRect is null && ReferenceEquals(clip.Source, state.ImageOwner))
                    {
                        state = state with { ImageRect = clipBounds };
                    }

                    state = state with { Clip = Intersect(state.Clip, clipBounds) };
                    continue;
                }

                case ClipRoundedRectItem clip:
                    state = state with { Clip = Intersect(state.Clip, TransformBounds(state.Transform, clip.Rect.Rect)) };
                    continue;

                case ClipPathItem clip:
                    state = state with { Clip = Intersect(state.Clip, TransformBounds(state.Transform, clip.Path.GetBounds())) };
                    continue;
            }

            if (state.Hidden || GetBounds(item) is not { } localBounds)
                continue;

            // The paintable bounding rect is not clipped (the clipped contents can be scrolled into view); a box scaled to nothing (e.g.
            // rotated by 90 degrees around an axis of the plane) is not paintable
            var bounds = TransformBounds(state.Transform, localBounds);
            if (bounds.Width < MinimumExtent || bounds.Height < MinimumExtent || !Intersects(bounds, scrollingArea))
                continue;

            if (state.ImageOwner is { } imageOwner && (item.Source is null || !ReferenceEquals(item.Source.NodeDocument, document)))
            {
                result.HasFirstPaint = true;
                result.HasContentfulPaint = true;
                AddImage(result, imageOwner, "", Intersect(state.Clip, state.ImageRect ?? bounds), viewport);
                continue;
            }

            if (item.Source is not { } source || !ReferenceEquals(source.NodeDocument, document))
                continue;

            result.HasFirstPaint = true;
            switch (item)
            {
                case DrawGlyphsItem glyphs when HasNonWhiteSpace(glyphs.Run.Text):
                {
                    var visible = !glyphs.Paint.Color.IsTransparent || glyphs.Paint.Shader is not null;
                    result.Texts[source] = (result.Texts.TryGetValue(source, out var previous) && previous) || visible;
                    result.HasContentfulPaint = true;
                    break;
                }

                case DrawImageItem image when source is Element element:
                    AddImage(result, element, image.Image.Url, Intersect(state.Clip, bounds), viewport);
                    result.HasContentfulPaint = true;
                    break;

                default:
                    if (GetPaint(item) is { Shader: { } shader } && source is Element shaderElement && GetImageUrl(shader) is { } url)
                    {
                        AddImage(result, shaderElement, url, Intersect(state.Clip, bounds), viewport);
                        result.HasContentfulPaint = true;
                    }

                    break;
            }

            // https://w3c.github.io/paint-timing/#contentful: "an svg element with rendered descendants"
            if (source is SvgElement and not SvgSvgElement { ParentNode: not SvgElement })
            {
                result.HasContentfulPaint = true;
            }
        }

        return result;
    }

    /// <summary>The extent below which a painted rectangle is empty.</summary>
    private const double MinimumExtent = 1e-3;

    /// <summary>The state of the drawing operations: the current transform, whether they are in a group whose opacity is 0, the bounds of the clip (in the coordinates of the display list), and the element whose SVG image is drawn with the rectangle of the image.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct ScanState(DisplayMatrix Transform, bool Hidden, PhysicalRect? Clip, Element? ImageOwner, PhysicalRect? ImageRect);

    /// <summary>Returns the part of a rectangle in a clip (an empty rectangle when they do not intersect).</summary>
    private static PhysicalRect Intersect(PhysicalRect? clip, PhysicalRect rect)
        => clip is not { } clipRect ? rect : IntersectionGeometry.TryIntersect(clipRect, rect, out var result) ? result : new PhysicalRect(rect.X, rect.Y, 0, 0);

    private static bool IsScrollportClip(PhysicalRect clip, PhysicalRect scrollport)
        => Math.Abs(clip.X - scrollport.X) < 1 && Math.Abs(clip.Y - scrollport.Y) < 1 && Math.Abs(clip.Width - scrollport.Width) < 1 && Math.Abs(clip.Height - scrollport.Height) < 1;

    /// <summary>Records the paint of an image by an element: its visible rectangle (clipped), when it intersects the viewport (as in the browsers, an image is painted for the paint timing when it is visible).</summary>
    private static void AddImage(ContentfulPaintScan result, Element element, string url, PhysicalRect bounds, PhysicalRect viewport)
    {
        if (bounds.Width < MinimumExtent || bounds.Height < MinimumExtent || !Intersects(bounds, viewport))
            return;

        if (!result.Images.TryGetValue(element, out var images))
        {
            images = new Dictionary<string, PhysicalRect>(StringComparer.Ordinal);
            result.Images[element] = images;
        }

        images[url] = images.TryGetValue(url, out var previous) ? previous.Union(bounds) : bounds;
    }

    /// <summary>Returns the URL of the image a shader draws (the empty string for an SVG image), or null when it draws no image (a gradient, a color).</summary>
    private static string? GetImageUrl(DisplayShader shader) => shader switch
    {
        ImageShader image => image.Image.Url,
        DisplayListShader => "",
        TiledShader tiled => GetImageUrl(tiled.Content),
        FilteredShader filtered => GetImageUrl(filtered.Content),
        _ => null,
    };

    internal static DisplayPaint? GetPaint(DisplayItem item) => item switch
    {
        DrawPaintItem paint => paint.Paint,
        DrawRectItem rect => rect.Paint,
        DrawRoundedRectItem rounded => rounded.Paint,
        DrawRingItem ring => ring.Paint,
        DrawPathItem path => path.Paint,
        DrawLineItem line => line.Paint,
        _ => null,
    };

    /// <summary>Returns the bounds of a drawing operation in its coordinates, or null when it is not known (the whole clip is painted).</summary>
    internal static PhysicalRect? GetBounds(DisplayItem item)
    {
        switch (item)
        {
            case DrawRectItem rect:
                return rect.Rect;
            case DrawRoundedRectItem rounded:
                return rounded.Rect.Rect;
            case DrawRingItem ring:
                return ring.Outer.Rect;
            case DrawPathItem path:
                return path.Path.GetBounds();
            case DrawLineItem line:
                return new PhysicalRect(Math.Min(line.Start.X, line.End.X), Math.Min(line.Start.Y, line.End.Y), Math.Max(1, Math.Abs(line.End.X - line.Start.X)), Math.Max(1, Math.Abs(line.End.Y - line.Start.Y)));
            case DrawImageItem image:
                return image.DestinationRect;
            case DrawGlyphsItem glyphs:
            {
                // The glyphs are within one em around their origins (the ascent above the baseline, the descent below it)
                var positions = glyphs.Run.Positions;
                if (positions.Length == 0)
                    return null;

                var size = glyphs.Run.Font.Size;
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var position in positions)
                {
                    minX = Math.Min(minX, position.X);
                    maxX = Math.Max(maxX, position.X);
                    minY = Math.Min(minY, position.Y);
                    maxY = Math.Max(maxY, position.Y);
                }

                return new PhysicalRect(minX, minY - size, maxX - minX + (size * glyphs.Run.ScaleX), maxY - minY + (size * 1.25));
            }

            case DrawPaintItem:
                return new PhysicalRect(-1e7, -1e7, 2e7, 2e7);
            default:
                return null;
        }
    }

    internal static PhysicalRect TransformBounds(DisplayMatrix transform, PhysicalRect rect)
    {
        if (transform.IsTranslation)
            return rect.Translate(new PhysicalOffset(transform.E, transform.F));

        var p1 = transform.Apply(new PhysicalOffset(rect.X, rect.Y));
        var p2 = transform.Apply(new PhysicalOffset(rect.X + rect.Width, rect.Y));
        var p3 = transform.Apply(new PhysicalOffset(rect.X + rect.Width, rect.Y + rect.Height));
        var p4 = transform.Apply(new PhysicalOffset(rect.X, rect.Y + rect.Height));
        var minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        var minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        var maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        var maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));
        return new PhysicalRect(minX, minY, maxX - minX, maxY - minY);
    }

    private static bool Intersects(PhysicalRect a, PhysicalRect b) => a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    /// <summary>https://w3c.github.io/paint-timing/#non-empty: the text contains a character that is not a document white space character.</summary>
    internal static bool HasNonWhiteSpace(string text)
    {
        foreach (var c in text)
        {
            if (c is not (' ' or '\t' or '\n' or '\f' or '\r'))
                return true;
        }

        return false;
    }
}
