using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The float area of a float defined by <c>shape-outside</c> (https://drafts.csswg.org/css-shapes/#shape-outside-property),
/// in the coordinates of the block formatting context (see <see cref="ExclusionSpace"/>): the line boxes next to the float are
/// shortened by the extent of the shape in their band instead of by its margin box. The shape includes <c>shape-margin</c>;
/// the exclusion space clips it to the margin box of the float.
/// </summary>
internal abstract class FloatShape
{
    /// <summary>
    /// Returns the line-left and line-right edges of the float area in the band from <paramref name="blockStart"/> to
    /// <paramref name="blockEnd"/> (a single position when they are equal), or false when the band does not intersect it.
    /// </summary>
    public abstract bool TryGetLineExtent(double blockStart, double blockEnd, out double lineLeft, out double lineRight);
}

/// <summary>
/// A rectangle with elliptical corners (the boxes, <c>inset()</c>, and <c>circle()</c>/<c>ellipse()</c> whose radii fill
/// it), in line-relative coordinates: <c>X</c> is the inline axis (from line-left), <c>Y</c> the block axis.
/// </summary>
internal sealed class RoundedRectangleShape : FloatShape
{
    private readonly double _left;
    private readonly double _top;
    private readonly double _right;
    private readonly double _bottom;

    // The (inline, block) radii of the corners: line-left/block-start, line-right/block-start, line-right/block-end, line-left/block-end
    private readonly (double X, double Y) _leftTop;
    private readonly (double X, double Y) _rightTop;
    private readonly (double X, double Y) _rightBottom;
    private readonly (double X, double Y) _leftBottom;

    /// <summary>Creates the shape, expanded by <paramref name="margin"/> (https://drafts.csswg.org/css-shapes/#shape-margin-property: the radii grow by the margin).</summary>
    public RoundedRectangleShape(double left, double top, double right, double bottom, (double X, double Y) leftTop, (double X, double Y) rightTop, (double X, double Y) rightBottom, (double X, double Y) leftBottom, double margin)
    {
        _left = left - margin;
        _top = top - margin;
        _right = right + margin;
        _bottom = bottom + margin;
        _leftTop = (leftTop.X + margin, leftTop.Y + margin);
        _rightTop = (rightTop.X + margin, rightTop.Y + margin);
        _rightBottom = (rightBottom.X + margin, rightBottom.Y + margin);
        _leftBottom = (leftBottom.X + margin, leftBottom.Y + margin);
    }

    public override bool TryGetLineExtent(double blockStart, double blockEnd, out double lineLeft, out double lineRight)
    {
        lineLeft = 0;
        lineRight = 0;
        // (a shape without area does not shorten the line boxes, as in Blink and WebKit)
        if (_right <= _left || _bottom <= _top || (blockEnd > blockStart ? blockEnd <= _top || blockStart >= _bottom : blockStart < _top || blockStart >= _bottom))
            return false;

        // The edges of a side are the furthest outside at the position of the band closest to the straight part of the side
        var start = Math.Max(blockStart, _top);
        var end = Math.Max(start, Math.Min(blockEnd, _bottom));
        lineLeft = _left + GetCornerInset(start, end, _leftTop, _leftBottom);
        lineRight = _right - GetCornerInset(start, end, _rightTop, _rightBottom);
        return true;
    }

    /// <summary>Returns the smallest distance between a side and its curved corners in the band from <paramref name="start"/> to <paramref name="end"/>.</summary>
    private double GetCornerInset(double start, double end, (double X, double Y) topRadius, (double X, double Y) bottomRadius)
    {
        var straightStart = _top + topRadius.Y;
        var straightEnd = _bottom - bottomRadius.Y;
        if (end < straightStart)
            return Inset(topRadius, (straightStart - end) / topRadius.Y);

        if (start > straightEnd)
            return Inset(bottomRadius, (start - straightEnd) / bottomRadius.Y);

        return 0;

        static double Inset((double X, double Y) radius, double distance) => radius.X <= 0 || radius.Y <= 0 ? 0 : radius.X * (1 - Math.Sqrt(Math.Max(0, 1 - (distance * distance))));
    }
}

/// <summary>A <c>polygon()</c> (or several closed polygons) in line-relative coordinates, expanded by <c>shape-margin</c> (the area within the margin of the polygons).</summary>
internal sealed class PolygonShape : FloatShape
{
    private readonly (double X, double Y)[][] _contours;
    private readonly double _margin;

    /// <summary>Creates the float area of closed polygons (a <c>polygon()</c>, or the flattened subpaths of a <c>path()</c> or a <c>shape()</c>).</summary>
    public PolygonShape((double X, double Y)[][] contours, double margin)
    {
        _contours = contours;
        _margin = margin;
    }

    public override bool TryGetLineExtent(double blockStart, double blockEnd, out double lineLeft, out double lineRight)
    {
        lineLeft = double.PositiveInfinity;
        lineRight = double.NegativeInfinity;
        var found = false;
        foreach (var vertices in _contours)
        {
            found |= ExtendWithContour(vertices, blockStart, blockEnd, ref lineLeft, ref lineRight);
        }

        return found;
    }

    /// <summary>Extends the extent with the edges of a closed polygon in the band.</summary>
    private bool ExtendWithContour((double X, double Y)[] vertices, double blockStart, double blockEnd, ref double lineLeft, ref double lineRight)
    {
        var found = false;
        for (var i = 0; i < vertices.Length; i++)
        {
            var p = vertices[i];
            var q = vertices[(i + 1) % vertices.Length];
            if (_margin > 0)
            {
                found |= ExtendWithMargin(p, q, blockStart, blockEnd, ref lineLeft, ref lineRight);
                continue;
            }

            // The part of the edge in the band: an edge that only touches the block-start or the block-end of the band is not in
            // it (the line box under a part of the polygon is not shortened by that part); a position is the block-start of a
            // band: it is in the edges that continue after it
            var low = Math.Min(p.Y, q.Y);
            var high = Math.Max(p.Y, q.Y);
            if (blockEnd > blockStart ? high <= blockStart || low >= blockEnd : high <= blockStart || low > blockStart)
                continue;

            found = true;
            if (p.Y == q.Y)
            {
                Extend(p.X, ref lineLeft, ref lineRight);
                Extend(q.X, ref lineLeft, ref lineRight);
                continue;
            }

            var from = Math.Max(low, blockStart);
            var to = Math.Min(high, blockEnd);
            Extend(p.X + ((q.X - p.X) * (from - p.Y) / (q.Y - p.Y)), ref lineLeft, ref lineRight);
            Extend(p.X + ((q.X - p.X) * (to - p.Y) / (q.Y - p.Y)), ref lineLeft, ref lineRight);
        }

        return found;
    }

    /// <summary>
    /// Extends the extent with the points within the margin of an edge: the furthest line-right point of the area around the
    /// edge in the band is the maximum of x + sqrt(margin² - d²) along the edge, where d is the distance of the point of the edge
    /// to the band in the block axis: a concave function, maximized by a ternary search.
    /// </summary>
    private bool ExtendWithMargin((double X, double Y) p, (double X, double Y) q, double blockStart, double blockEnd, ref double lineLeft, ref double lineRight)
    {
        var margin = _margin;
        var low = blockStart - margin;
        var high = Math.Max(blockStart, blockEnd) + margin;
        double from;
        double to;
        // (an edge whose distance to the band is the margin only touches the band: it is not in it)
        if (p.Y == q.Y)
        {
            if (p.Y <= low || p.Y >= high)
                return false;

            from = 0;
            to = 1;
        }
        else
        {
            var t1 = (low - p.Y) / (q.Y - p.Y);
            var t2 = (high - p.Y) / (q.Y - p.Y);
            from = Math.Max(0, Math.Min(t1, t2));
            to = Math.Min(1, Math.Max(t1, t2));
            if (from >= to)
                return false;
        }

        lineRight = Math.Max(lineRight, Maximize(t => X(t) + Spread(t)));
        lineLeft = Math.Min(lineLeft, -Maximize(t => -X(t) + Spread(t)));
        return true;

        double X(double t) => p.X + ((q.X - p.X) * t);

        double Spread(double t)
        {
            var y = p.Y + ((q.Y - p.Y) * t);
            var distance = y < blockStart ? blockStart - y : y > blockEnd ? y - blockEnd : 0;
            return Math.Sqrt(Math.Max(0, (margin * margin) - (distance * distance)));
        }

        double Maximize(Func<double, double> function)
        {
            var a = from;
            var b = to;
            for (var iteration = 0; iteration < 60 && b - a > 1e-9; iteration++)
            {
                var m1 = a + ((b - a) / 3);
                var m2 = b - ((b - a) / 3);
                if (function(m1) < function(m2))
                {
                    a = m1;
                }
                else
                {
                    b = m2;
                }
            }

            return Math.Max(function((a + b) / 2), Math.Max(function(from), function(to)));
        }
    }

    private static void Extend(double x, ref double lineLeft, ref double lineRight)
    {
        lineLeft = Math.Min(lineLeft, x);
        lineRight = Math.Max(lineRight, x);
    }
}

/// <summary>
/// The float area of a shape from an image (https://drafts.csswg.org/css-shapes/#shapes-from-image): the pixels of the image
/// whose alpha is greater than <c>shape-image-threshold</c>, as one rectangle per row of pixels across the block axis (the
/// pixels between the first and the last one of the row: only the extent of the shape in a band matters), in line-relative
/// coordinates, expanded by <c>shape-margin</c> (the area within the margin of the rectangles).
/// </summary>
internal sealed class ImageShape : FloatShape
{
    private readonly (double BlockStart, double BlockEnd, double LineLeft, double LineRight)[] _rows;
    private readonly double _margin;

    public ImageShape((double BlockStart, double BlockEnd, double LineLeft, double LineRight)[] rows, double margin)
    {
        _rows = rows;
        _margin = margin;
    }

    public override bool TryGetLineExtent(double blockStart, double blockEnd, out double lineLeft, out double lineRight)
    {
        lineLeft = double.PositiveInfinity;
        lineRight = double.NegativeInfinity;
        var found = false;
        foreach (var row in _rows)
        {
            // The distance between the band and the row in the block axis: the margin extends the row by the half-chord of the
            // circle of the margin at that distance
            var distance = Math.Max(0, Math.Max(row.BlockStart - Math.Max(blockStart, blockEnd), blockStart - row.BlockEnd));
            if (blockEnd > blockStart ? blockEnd <= row.BlockStart - _margin || blockStart >= row.BlockEnd + _margin : blockStart < row.BlockStart - _margin || blockStart >= row.BlockEnd + _margin)
                continue;

            var extension = _margin > 0 ? Math.Sqrt(Math.Max(0, (_margin * _margin) - (distance * distance))) : 0;
            lineLeft = Math.Min(lineLeft, row.LineLeft - extension);
            lineRight = Math.Max(lineRight, row.LineRight + extension);
            found = true;
        }

        return found;
    }
}

/// <summary>Creates the float areas defined by <c>shape-outside</c> (https://drafts.csswg.org/css-shapes/#shape-outside-property).</summary>
/// <remarks>
/// The basic shapes <c>inset()</c> (and <c>rect()</c>/<c>xywh()</c>, which compute to it), <c>circle()</c>, <c>ellipse()</c>,
/// <c>polygon()</c>, <c>path()</c> and <c>shape()</c> (parsed and resolved by <see cref="BasicShapePaths"/>, as for
/// <c>clip-path</c>; the curves are flattened) and the box values are supported, with <c>shape-margin</c> (an ellipse with a margin is approximated by an
/// ellipse with larger radii, as in Blink). A shape from an image (<c>url()</c>, a gradient, with <c>shape-image-threshold</c>)
/// is extracted from the alpha channel of the image rasterized in the content box by the rendering backend of the document
/// (without backend, the float area is the margin box). TODO: the rounding of <c>polygon()</c>.
/// </remarks>
internal static class FloatShapes
{
    /// <summary>Returns the float area of a placed float, or null when it is its margin box.</summary>
    /// <param name="box">The float.</param>
    /// <param name="fragment">The fragment of the float.</param>
    /// <param name="writingDirection">The writing mode of the block formatting context.</param>
    /// <param name="marginBox">The margin box of the float in the coordinates of the block formatting context.</param>
    /// <param name="containingBlockInlineSize">The inline size of the containing block (the basis of the percentages of <c>shape-margin</c>).</param>
    public static FloatShape? Create(ContainerBox box, BoxFragment fragment, WritingDirection writingDirection, LogicalRect marginBox, double containingBlockInlineSize)
    {
        var style = box.Style;
        var value = style.GetComputedValue("shape-outside");
        if (value is "none" or "")
            return null;

        CssFunction? function = null;
        string? referenceBox = null;
        var components = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        if (components is [var single] && IsImage(single))
            return CreateImageShape(box, fragment, single, value, new CoordinateMapping(writingDirection, fragment, marginBox), writingDirection.IsHorizontal, containingBlockInlineSize);

        foreach (var component in components)
        {
            switch (component)
            {
                case CssFunction shapeFunction when function is null:
                    function = shapeFunction;
                    break;

                case CssPreservedToken { Type: CssTokenType.Ident } ident when referenceBox is null:
                    referenceBox = ident.Token.Value.ToLowerInvariant();
                    break;

                default:
                    // An image (url(), a gradient)
                    return null;
            }
        }

        var zoom = style.Layout.EffectiveZoom;
        var shapeMargin = LengthPercentage.TryParse(style.GetComputedValue("shape-margin"), zoom, nonNegative: true, out var marginValue) ? Math.Max(0, marginValue.Resolve(containingBlockInlineSize)) : 0;

        // https://drafts.csswg.org/css-shapes/#shapes-from-box-values: the reference box, in the coordinates of the border box
        var size = fragment.Size;
        var margin = fragment.Margin;
        var border = fragment.Border;
        var padding = fragment.Padding;
        var borderRadii = BorderRadii.Get(style, size);
        var (rect, radii) = (referenceBox ?? "margin-box") switch
        {
            "border-box" => (new PhysicalRect(0, 0, size.Width, size.Height), borderRadii),
            "padding-box" => (new PhysicalRect(0, 0, size.Width, size.Height).Contract(border), Shrink(borderRadii, border)),
            "content-box" => (new PhysicalRect(0, 0, size.Width, size.Height).Contract(border.Add(padding)), Shrink(borderRadii, border.Add(padding))),
            "margin-box" => (new PhysicalRect(0, 0, size.Width, size.Height).Expand(margin), Grow(borderRadii, margin)),
            _ => (default(PhysicalRect), default(BorderRadii?)),
        };

        if (referenceBox is not (null or "margin-box" or "border-box" or "padding-box" or "content-box"))
            return null;

        var mapping = new CoordinateMapping(writingDirection, fragment, marginBox);
        if (function is null)
        {
            // https://drafts.csswg.org/css-borders-4/#corner-shape-rendering: the edges of a box whose corners are shaped by
            // corner-shape are its contour paths (the margin edge has the outset-adjusted radii): the float area is the polygon
            // of the flattened contour. When the corner areas overlap, the contour is not one polygon: the corners stay elliptical
            if (borderRadii is { } contourRadii && CornerContour.Create(style, new PhysicalRect(0, 0, size.Width, size.Height), contourRadii) is { } contour
                && contour.GetShape(rect) is { Cutouts: null, Polygon: { Count: >= 3 } contourPolygon })
            {
                var vertices = new (double X, double Y)[contourPolygon.Count];
                for (var i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = mapping.Map(contourPolygon[i].X, contourPolygon[i].Y);
                }

                return new PolygonShape([vertices], shapeMargin);
            }

            return mapping.CreateRoundedRectangle(rect, radii ?? default, shapeMargin);
        }

        // The basic shapes are parsed and resolved in the reference box as for clip-path
        if (BasicShapePaths.Resolve(function, rect, zoom) is not { } geometry)
            return null;

        if (geometry.Rectangle is { } rectangle)
            return mapping.CreateRoundedRectangle(rectangle.Rect, rectangle.Radii, shapeMargin);

        // https://drafts.csswg.org/css-shapes/#funcdef-basic-shape-polygon: a polygon needs 3 vertices; the curves of path() and
        // shape() are flattened. The fill rule does not change the extents of the lines.
        List<PhysicalOffset[]> contours = geometry.Polygon is { } polygon ? (polygon.Length >= 3 ? [polygon] : []) : BasicShapePaths.Flatten(geometry.Path!);
        if (contours.Count == 0)
            return null;

        var mapped = new (double X, double Y)[contours.Count][];
        for (var i = 0; i < contours.Count; i++)
        {
            var contour = contours[i];
            mapped[i] = new (double X, double Y)[contour.Length];
            for (var j = 0; j < contour.Length; j++)
            {
                mapped[i][j] = mapping.Map(contour[j].X, contour[j].Y);
            }
        }

        return new PolygonShape(mapped, shapeMargin);
    }

    /// <summary>Whether a component of the value of <c>shape-outside</c> is an &lt;image&gt; (a URL, a gradient or another image function) rather than a basic shape.</summary>
    private static bool IsImage(CssComponentValue component) => component switch
    {
        CssPreservedToken { Type: CssTokenType.Url } => true,
        CssFunction function => InfraAscii.ToAsciiLowercase(function.Name) is var name
            && (name.EndsWith("gradient", StringComparison.Ordinal) || name is "url" or "src" or "image" or "image-set" or "-webkit-image-set" or "cross-fade" or "filter"),
        _ => false,
    };

    /// <summary>The alpha masks of the shape images, per float: a float is placed at each layout pass, its image is rasterized once.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContainerBox, ImageMask> ImageMasks = [];

    /// <summary>The pixels of a shape image that are in the shape, for a style and a size of the content box.</summary>
    private sealed record ImageMask(ComputedStyle Style, string Value, int Width, int Height, double Threshold, bool[] Pixels);

    /// <summary>The largest side of the bitmap a shape image is rasterized into (a larger content box is sampled at a lower resolution).</summary>
    private const int MaxImageShapeSize = 2048;

    /// <summary>
    /// Returns the float area of a shape from an image (https://drafts.csswg.org/css-shapes/#shapes-from-image): "The image is
    /// sized and positioned as if it were a replaced element whose specified width and height are the same as the element's used
    /// content box size", and "the shape is computed to be the path or paths that enclose the area(s) where the opacity of the
    /// specified image is greater than the shape-image-threshold value". Returns null (the margin box) when the image is not
    /// available (not loaded yet, not same-origin without CORS: it is fetched with the "cors" mode) or cannot be rasterized.
    /// </summary>
    private static ImageShape? CreateImageShape(ContainerBox box, BoxFragment fragment, CssComponentValue image, string value, CoordinateMapping mapping, bool horizontal, double containingBlockInlineSize)
    {
        var style = box.Style;
        var zoom = style.Layout.EffectiveZoom;
        var shapeMargin = LengthPercentage.TryParse(style.GetComputedValue("shape-margin"), zoom, nonNegative: true, out var marginValue) ? Math.Max(0, marginValue.Resolve(containingBlockInlineSize)) : 0;
        var threshold = CssValueComputer.TryParseNumber(style.GetComputedValue("shape-image-threshold"), out var thresholdValue) ? Math.Clamp(thresholdValue, 0, 1) : 0;
        var content = new PhysicalRect(0, 0, fragment.Size.Width, fragment.Size.Height).Contract(fragment.Border.Add(fragment.Padding));
        if (content.Width <= 0 || content.Height <= 0)
            return new ImageShape([], shapeMargin);

        var scale = Math.Min(1, MaxImageShapeSize / Math.Max(content.Width, content.Height));
        var width = Math.Max(1, (int)Math.Round(content.Width * scale, MidpointRounding.AwayFromZero));
        var height = Math.Max(1, (int)Math.Round(content.Height * scale, MidpointRounding.AwayFromZero));
        if (!ImageMasks.TryGetValue(box, out var mask) || !ReferenceEquals(mask.Style, style) || mask.Value != value || mask.Width != width || mask.Height != height || mask.Threshold != threshold)
        {
            if (Rasterize(style, image, width, height, threshold) is not { } pixels)
                return null;

            mask = new ImageMask(style, value, width, height, threshold, pixels);
            ImageMasks.AddOrUpdate(box, mask);
        }

        // One rectangle per row of pixels across the block axis: a row of the image in a horizontal writing mode, a column in
        // a vertical one
        var rows = new List<(double BlockStart, double BlockEnd, double LineLeft, double LineRight)>(horizontal ? height : width);
        var pixelWidth = content.Width / width;
        var pixelHeight = content.Height / height;
        var (lineCount, lineLength) = horizontal ? (height, width) : (width, height);
        for (var line = 0; line < lineCount; line++)
        {
            var first = -1;
            var last = -1;
            for (var i = 0; i < lineLength; i++)
            {
                if (horizontal ? mask.Pixels[(line * width) + i] : mask.Pixels[(i * width) + line])
                {
                    first = first < 0 ? i : first;
                    last = i;
                }
            }

            if (first < 0)
                continue;

            var (x0, y0, x1, y1) = horizontal ? (first * pixelWidth, line * pixelHeight, (last + 1) * pixelWidth, (line + 1) * pixelHeight) : (line * pixelWidth, first * pixelHeight, (line + 1) * pixelWidth, (last + 1) * pixelHeight);
            var start = mapping.Map(content.X + x0, content.Y + y0);
            var end = mapping.Map(content.X + x1, content.Y + y1);
            rows.Add((Math.Min(start.Y, end.Y), Math.Max(start.Y, end.Y), Math.Min(start.X, end.X), Math.Max(start.X, end.X)));
        }

        return new ImageShape([.. rows], shapeMargin);
    }

    /// <summary>Rasterizes an image in a bitmap and returns its pixels whose alpha is greater than a threshold, or null when the image is not available or the document has no rendering backend.</summary>
    private static bool[]? Rasterize(ComputedStyle style, CssComponentValue image, int width, int height, double threshold)
    {
        var document = style.Element.NodeDocument;
        if (document.RenderingBackend is not { } backend || PaintedImage.Resolve(document, style, image) is not { } painted)
            return null;

        // A raster image is sampled without interpolation (as in Blink): the edges of the opaque areas of an image scaled to the
        // content box are not blurred into pixels of the shape
        if (painted is PaintedImage.RasterImage raster)
        {
            painted = new PaintedImage.RasterImage(raster.Image, style.Layout.EffectiveZoom, ImageSampling.Pixelated);
        }

        var builder = new DisplayListBuilder();
        painted.Draw(builder, new PhysicalRect(0, 0, width, height), source: null);
        using var surface = backend.CreateSurface(width, height, RenderingSurfaceOptions.Default with { WillReadFrequently = true });
        if (surface is null)
            return null;

        surface.Draw(builder.ToDisplayList(new PhysicalSize(width, height)));
        var pixels = new byte[width * height * 4];
        surface.ReadPixels(0, 0, width, height, pixels);
        var result = new bool[width * height];
        var minimum = threshold * 255;
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = pixels[(i * 4) + 3] > minimum;
        }

        return result;
    }

    /// <summary>The radii of the padding box and of the content box: the border radii minus the borders (and paddings).</summary>
    private static BorderRadii? Shrink(BorderRadii? radii, Sides<double> edges) => radii is not { } value ? null : new BorderRadii(
        (Math.Max(0, value.TopLeft.X - edges.Left), Math.Max(0, value.TopLeft.Y - edges.Top)),
        (Math.Max(0, value.TopRight.X - edges.Right), Math.Max(0, value.TopRight.Y - edges.Top)),
        (Math.Max(0, value.BottomRight.X - edges.Right), Math.Max(0, value.BottomRight.Y - edges.Bottom)),
        (Math.Max(0, value.BottomLeft.X - edges.Left), Math.Max(0, value.BottomLeft.Y - edges.Bottom)));

    /// <summary>
    /// The radii of the margin box (https://drafts.csswg.org/css-shapes/#valdef-shape-box-margin-box): "If the ratio of
    /// border-radius/margin is 1 or more, or margin is negative or zero, then the margin box corner radius is
    /// max(border-radius + margin, 0). If the ratio of border-radius/margin is less than 1, and margin is positive, then the
    /// margin box corner radius is border-radius + margin * (1 + (ratio-1)^3)."
    /// </summary>
    internal static BorderRadii? Grow(BorderRadii? radii, Sides<double> margin) => radii is not { } value ? null : new BorderRadii(
        (Spread(value.TopLeft.X, margin.Left), Spread(value.TopLeft.Y, margin.Top)),
        (Spread(value.TopRight.X, margin.Right), Spread(value.TopRight.Y, margin.Top)),
        (Spread(value.BottomRight.X, margin.Right), Spread(value.BottomRight.Y, margin.Bottom)),
        (Spread(value.BottomLeft.X, margin.Left), Spread(value.BottomLeft.Y, margin.Bottom)));

    private static double Spread(double radius, double margin)
    {
        if (radius <= 0)
            return 0;

        if (margin <= 0 || radius / margin >= 1)
            return Math.Max(radius + margin, 0);

        var ratio = radius / margin;
        return radius + (margin * (1 + Math.Pow(ratio - 1, 3)));
    }

    /// <summary>
    /// The conversion of the physical coordinates of a float (relative to the top-left corner of its border box) to the
    /// line-relative coordinates of the block formatting context.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly struct CoordinateMapping
    {
        private readonly WritingMode _writingMode;
        private readonly double _lineLeft;
        private readonly double _blockStart;
        private readonly PhysicalSize _size;

        public CoordinateMapping(WritingDirection writingDirection, BoxFragment fragment, LogicalRect marginBox)
        {
            _writingMode = writingDirection.WritingMode;
            _lineLeft = marginBox.InlineStart + fragment.Margin.Get(writingDirection.LineLeft);
            _blockStart = marginBox.BlockStart + fragment.Margin.Get(writingDirection.BlockStart);
            _size = fragment.Size;
        }

        /// <summary>Whether the inline axis of the block formatting context is vertical: the physical x and y are swapped.</summary>
        private bool SwapsAxes => _writingMode != WritingMode.HorizontalTb;

        public (double X, double Y) Map(double x, double y) => _writingMode switch
        {
            WritingMode.HorizontalTb => (_lineLeft + x, _blockStart + y),
            WritingMode.VerticalRl or WritingMode.SidewaysRl => (_lineLeft + y, _blockStart + (_size.Width - x)),
            WritingMode.SidewaysLr => (_lineLeft + (_size.Height - y), _blockStart + x),
            _ => (_lineLeft + y, _blockStart + x),
        };

        /// <summary>Converts a physical rectangle with rounded corners to the line-relative coordinates.</summary>
        public RoundedRectangleShape CreateRoundedRectangle(PhysicalRect rect, BorderRadii radii, double margin)
        {
            var corners = new (double X, double Y, (double X, double Y) Radius)[4];
            corners[0] = Corner(rect.Left, rect.Top, radii.TopLeft);
            corners[1] = Corner(rect.Right, rect.Top, radii.TopRight);
            corners[2] = Corner(rect.Right, rect.Bottom, radii.BottomRight);
            corners[3] = Corner(rect.Left, rect.Bottom, radii.BottomLeft);
            var left = double.PositiveInfinity;
            var top = double.PositiveInfinity;
            var right = double.NegativeInfinity;
            var bottom = double.NegativeInfinity;
            foreach (var corner in corners)
            {
                left = Math.Min(left, corner.X);
                top = Math.Min(top, corner.Y);
                right = Math.Max(right, corner.X);
                bottom = Math.Max(bottom, corner.Y);
            }

            (double X, double Y) leftTop = default, rightTop = default, rightBottom = default, leftBottom = default;
            foreach (var corner in corners)
            {
                var isLeft = corner.X == left;
                var isTop = corner.Y == top;
                if (isLeft && isTop)
                {
                    leftTop = corner.Radius;
                }
                else if (isTop)
                {
                    rightTop = corner.Radius;
                }
                else if (isLeft)
                {
                    leftBottom = corner.Radius;
                }
                else
                {
                    rightBottom = corner.Radius;
                }
            }

            return new RoundedRectangleShape(left, top, right, bottom, leftTop, rightTop, rightBottom, leftBottom, margin);
        }

        private (double X, double Y, (double X, double Y) Radius) Corner(double x, double y, (double X, double Y) radius)
        {
            var (mappedX, mappedY) = Map(x, y);
            return (mappedX, mappedY, SwapsAxes ? (radius.Y, radius.X) : radius);
        }
    }
}
