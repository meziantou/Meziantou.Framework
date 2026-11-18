using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Svg;

/// <summary>
/// The wrapping areas of an SVG text whose content area is set by <c>shape-inside</c>
/// (https://svgwg.org/svg2-draft/text.html#TextShapeInside): one per shape of <c>shape-inside</c> (a basic shape, or an SVG shape
/// element referenced by a URL), in order (the text that overflows a shape is laid out in the next one). A wrapping area is
/// its content area inset by <c>shape-padding</c>, minus the shapes of <c>shape-subtract</c> (an SVG shape element is expanded
/// by its own <c>shape-margin</c>, a basic shape by the one of the text element).
/// </summary>
/// <remarks>
/// The geometry is in logical coordinates: the inline axis of the lines and the block axis in which the lines are stacked. As
/// in CSS Shapes (https://svgwg.org/svg2-draft/text.html#TextLayoutAutoNotes), the edges of the shapes are sampled every unit
/// of the block axis (as a series of 1×1 floats): a line box fits where every row it covers is inside the area.
/// </remarks>
internal sealed class SvgTextWrappingArea
{
    // The number of rows a line box is sampled at is limited (a very tall line box is sampled with larger steps)
    private const int MaxRows = 256;

    private readonly Shape _content;
    private readonly double _padding;
    private readonly Shape[] _subtracted;

    private SvgTextWrappingArea(Shape content, double padding, Shape[] subtracted)
    {
        _content = content;
        _padding = padding;
        _subtracted = subtracted;
    }

    /// <summary>The start of the area along the block axis: the first line box is placed at or after it.</summary>
    public double BlockStart => _content.BlockMin + _padding;

    /// <summary>The end of the area along the block axis: a line box after it does not fit.</summary>
    public double BlockEnd => _content.BlockMax - _padding;

    /// <summary>
    /// Returns the wrapping areas of a text element, or null when its <c>shape-inside</c> is <c>auto</c> or sets no shape (the
    /// values of CSS Shapes 2, such as <c>display</c> or an image, are not supported).
    /// </summary>
    /// <param name="mode">The writing mode of the text: it maps the user space of the text element to the logical axes.</param>
    public static List<SvgTextWrappingArea>? Get(SvgTextElement root, ComputedStyle rootStyle, SvgOutermostViewport? viewport, WritingMode mode)
    {
        var shapeInside = rootStyle.GetComputedValue("shape-inside");
        if (shapeInside is null or "" or "auto")
            return null;

        var (width, height) = SvgUserSpace.GetViewportSize(root, viewport);
        var viewportBox = new PhysicalRect(0, 0, width, height);
        // A shape that is resolved but empty (e.g. a rectangle of height 0) is a content area where nothing fits: the text is
        // not rendered. A value that sets no shape (e.g. a URL that references no shape element) is ignored.
        var contents = new List<Shape>();
        var hasShape = false;
        foreach (var component in ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(shapeInside)))
        {
            if (GetShape(root, component, viewportBox, viewport, mode, out var resolved) is { } shape)
            {
                contents.Add(shape);
            }

            hasShape |= resolved;
        }

        if (contents.Count == 0)
            return hasShape ? [] : null;

        var padding = SvgUserSpace.ResolveLength(root, rootStyle.GetComputedValue("shape-padding"), width) is { } value && double.IsFinite(value) ? Math.Max(value, 0) : 0;
        var subtracted = new List<Shape>();
        var shapeSubtract = rootStyle.GetComputedValue("shape-subtract");
        if (shapeSubtract is not (null or "" or "none"))
        {
            var rootMargin = GetMargin(root, width);
            foreach (var component in ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(shapeSubtract)))
            {
                if (GetShape(root, component, viewportBox, viewport, mode, out _) is { } shape)
                {
                    subtracted.Add(shape with { Margin = shape.Element is { } element ? GetMargin(element, width) : rootMargin });
                }
            }
        }

        return contents.ConvertAll(content => new SvgTextWrappingArea(content, padding, [.. subtracted]));

        static double GetMargin(Element element, double percentageBasis)
            => SvgUserSpace.ResolveLength(element, SvgUserSpace.GetComputedValue(element, "shape-margin"), percentageBasis) is { } margin && double.IsFinite(margin) ? Math.Max(margin, 0) : 0;
    }

    /// <summary>
    /// Returns the intervals of the inline axis where a line box that covers [<paramref name="blockStart"/>,
    /// <paramref name="blockEnd"/>] of the block axis fits in the area, in increasing order: the segments of a line, which a
    /// shape or a subtracted shape can break (https://svgwg.org/svg2-draft/text.html#TextLayoutAutoNotes, "broken lines").
    /// </summary>
    public List<(double Start, double End)> GetSegments(double blockStart, double blockEnd)
    {
        // The rows of the line box, extended by the padding: a point of the line box is at least the padding away from the
        // edges, so the row at a distance d from the line box is narrowed by sqrt(padding² - d²)
        var allowed = Intersect(_content, blockStart, blockEnd, _padding);
        if (allowed.Count == 0)
            return allowed;

        foreach (var shape in _subtracted)
        {
            allowed = Subtract(allowed, Union(shape, blockStart, blockEnd, shape.Margin));
            if (allowed.Count == 0)
                break;
        }

        allowed.RemoveAll(segment => segment.End - segment.Start <= 0);
        return allowed;
    }

    /// <summary>Returns the intervals that are inside a shape on every row of a band, at least a distance away from its edges.</summary>
    private static List<(double Start, double End)> Intersect(Shape shape, double blockStart, double blockEnd, double distance)
    {
        List<(double Start, double End)>? result = null;
        foreach (var (row, inset) in GetRows(blockStart, blockEnd, distance))
        {
            var intervals = shape.GetIntervals(row);
            var narrowed = new List<(double Start, double End)>(intervals.Count);
            foreach (var (start, end) in intervals)
            {
                if (end - start > 2 * inset)
                {
                    narrowed.Add((start + inset, end - inset));
                }
            }

            result = result is null ? narrowed : Intersect(result, narrowed);
            if (result.Count == 0)
                break;
        }

        return result ?? [];
    }

    /// <summary>Returns the intervals that are inside a shape on a row of a band, or less than a distance away from it.</summary>
    private static List<(double Start, double End)> Union(Shape shape, double blockStart, double blockEnd, double distance)
    {
        var result = new List<(double Start, double End)>();
        foreach (var (row, outset) in GetRows(blockStart, blockEnd, distance))
        {
            foreach (var (start, end) in shape.GetIntervals(row))
            {
                result.Add((start - outset, end + outset));
            }
        }

        result.Sort();
        var merged = new List<(double Start, double End)>(result.Count);
        foreach (var interval in result)
        {
            if (merged.Count > 0 && interval.Start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, interval.End));
            }
            else
            {
                merged.Add(interval);
            }
        }

        return merged;
    }

    /// <summary>
    /// Returns the rows that sample a band extended by a distance on both sides (the middles of its rows of one unit, as the
    /// 1×1 floats of the edges of a shape), and the distance along the inline axis that a disc of this radius centered in the
    /// band reaches on each row.
    /// </summary>
    private static IEnumerable<(double Row, double Reach)> GetRows(double blockStart, double blockEnd, double distance)
    {
        var start = blockStart - distance;
        var end = blockEnd + distance;
        var count = Math.Clamp((int)Math.Ceiling(end - start), 1, MaxRows);
        var step = (end - start) / count;
        for (var i = 0; i < count; i++)
        {
            var row = start + ((i + 0.5) * step);
            var offset = row < blockStart ? blockStart - row : row > blockEnd ? row - blockEnd : 0;
            yield return (row, offset >= distance ? 0 : Math.Sqrt((distance * distance) - (offset * offset)));
        }
    }

    private static List<(double Start, double End)> Intersect(List<(double Start, double End)> a, List<(double Start, double End)> b)
    {
        var result = new List<(double Start, double End)>();
        int i = 0, j = 0;
        while (i < a.Count && j < b.Count)
        {
            var start = Math.Max(a[i].Start, b[j].Start);
            var end = Math.Min(a[i].End, b[j].End);
            if (end > start)
            {
                result.Add((start, end));
            }

            if (a[i].End < b[j].End)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return result;
    }

    private static List<(double Start, double End)> Subtract(List<(double Start, double End)> intervals, List<(double Start, double End)> removed)
    {
        var result = new List<(double Start, double End)>();
        foreach (var (start, end) in intervals)
        {
            var current = start;
            foreach (var (removedStart, removedEnd) in removed)
            {
                if (removedEnd <= current || removedStart >= end)
                    continue;

                if (removedStart > current)
                {
                    result.Add((current, removedStart));
                }

                current = Math.Max(current, removedEnd);
                if (current >= end)
                    break;
            }

            if (current < end)
            {
                result.Add((current, end));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the shape of a value of <c>shape-inside</c> or <c>shape-subtract</c>, or null when it is not a shape or when the
    /// shape is empty (<paramref name="resolved"/> is then true).
    /// </summary>
    private static Shape? GetShape(SvgTextElement root, CssComponentValue component, PhysicalRect viewportBox, SvgOutermostViewport? viewport, WritingMode mode, out bool resolved)
    {
        resolved = false;

        // A basic shape: its lengths are in the user space of the text element, its percentages refer to the viewport
        // (https://svgwg.org/svg2-draft/text.html#TextShapeInside)
        if (component is CssFunction function && !InfraAscii.EqualsIgnoreAsciiCase(function.Name, "url") && !InfraAscii.EqualsIgnoreAsciiCase(function.Name, "src"))
        {
            if (BasicShapePaths.CreatePath(function, viewportBox, zoom: 1) is not { } basicShape)
                return null;

            resolved = true;
            return Shape.Create(basicShape, element: null, mode);
        }

        // A URL that references an SVG shape element: its geometry, transformed by its transform attribute, in the user space
        // of the text element
        if (SvgResources.GetReferencedElement(root, component) is not SvgGeometryElement element)
            return null;

        resolved = true;

        var fillRule = SvgUserSpace.GetComputedValue(element, "fill-rule") == "evenodd" ? FillRule.EvenOdd : FillRule.NonZero;
        var path = BasicShapePaths.Transform(DisplayPath.FromSvg(SvgShapeGeometry.GetPath(element, viewport), fillRule), SvgUserSpace.GetTransform(element, viewport));
        return Shape.Create(path, element, mode);
    }

    /// <summary>A shape in logical coordinates: closed polygons (X is the inline axis, Y the block axis) and a fill rule.</summary>
    private sealed record Shape(List<PhysicalOffset[]> Contours, FillRule FillRule, Element? Element, double BlockMin, double BlockMax)
    {
        /// <summary>The distance the shape is expanded by when it is subtracted (its <c>shape-margin</c>).</summary>
        public double Margin { get; init; }

        public static Shape? Create(DisplayPath path, Element? element, WritingMode mode)
        {
            var contours = BasicShapePaths.Flatten(path);
            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;
            foreach (var contour in contours)
            {
                for (var i = 0; i < contour.Length; i++)
                {
                    // The inline axis is x in horizontal text, y in vertical text (from the bottom in sideways-lr); the block
                    // axis goes to the bottom in horizontal text, to the left in vertical-rl and sideways-rl, to the right
                    // in vertical-lr and sideways-lr
                    var (x, y) = (contour[i].X, contour[i].Y);
                    contour[i] = mode switch
                    {
                        WritingMode.HorizontalTb => new PhysicalOffset(x, y),
                        WritingMode.VerticalLr => new PhysicalOffset(y, x),
                        WritingMode.SidewaysLr => new PhysicalOffset(-y, x),
                        _ => new PhysicalOffset(y, -x),
                    };
                    min = Math.Min(min, contour[i].Y);
                    max = Math.Max(max, contour[i].Y);
                }
            }

            return double.IsFinite(min) && double.IsFinite(max) && max > min ? new Shape(contours, path.FillRule, element, min, max) : null;
        }

        /// <summary>Returns the intervals of the inline axis that are inside the shape on a row of the block axis, in increasing order.</summary>
        public List<(double Start, double End)> GetIntervals(double row)
        {
            var crossings = new List<(double Inline, int Winding)>();
            foreach (var contour in Contours)
            {
                for (var i = 0; i < contour.Length; i++)
                {
                    var a = contour[i];
                    var b = contour[(i + 1) % contour.Length];
                    if (a.Y == b.Y)
                        continue;

                    // Half-open on the rows: a vertex on the row is counted once
                    var (low, high, winding) = a.Y < b.Y ? (a, b, 1) : (b, a, -1);
                    if (row < low.Y || row >= high.Y)
                        continue;

                    crossings.Add((low.X + ((row - low.Y) * (high.X - low.X) / (high.Y - low.Y)), winding));
                }
            }

            crossings.Sort((x, y) => x.Inline.CompareTo(y.Inline));
            var result = new List<(double Start, double End)>();
            var total = 0;
            var start = 0d;
            foreach (var (inline, winding) in crossings)
            {
                var wasInside = IsInside(total);
                total += winding;
                var isInside = IsInside(total);
                if (!wasInside && isInside)
                {
                    start = inline;
                }
                else if (wasInside && !isInside && inline > start)
                {
                    result.Add((start, inline));
                }
            }

            return result;

            bool IsInside(int value) => FillRule == FillRule.EvenOdd ? (value & 1) != 0 : value != 0;
        }
    }
}
