using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using static Meziantou.Framework.WebPlatform.Svg.SvgPathData;

namespace Meziantou.Framework.WebPlatform.Svg;

/// <summary>
/// The bounding boxes of the SVG elements (https://svgwg.org/svg2-draft/coords.html#BoundingBoxes) from the geometry of the
/// elements: the basic shapes and the paths (with the exact extrema of the curves and the arcs), their stroke (caps, joins
/// and miter limit), the glyph cells of the texts (see <see cref="SvgTextLayout"/>), and the containers (the union of the
/// children in the user space, with their transform). The markers and the clipping are not supported.
/// </summary>
internal static class SvgBoundingBox
{
    private const int MaxDepth = 64;
    private const int CurveSamples = 64;

    /// <summary>
    /// https://svgwg.org/svg2-draft/types.html#__svg__SVGGraphicsElement__getBBox: the bounding box in the user space of the
    /// element. An element that is not rendered (it is not connected, it or one of its ancestors has display: none, or it is
    /// in a non-rendered element such as defs) or whose geometry is empty has a zero bounding box
    /// (https://github.com/w3c/svgwg/issues/1018).
    /// </summary>
    public static DomRect Compute(SvgElement element, bool fill, bool stroke, SvgOutermostViewport? viewport = null)
    {
        if (!IsRendered(element))
            return new DomRect();

        var box = GetBox(element, fill, stroke, depth: 0, viewport);
        return box is { } b ? new DomRect(b.MinX, b.MinY, b.MaxX - b.MinX, b.MaxY - b.MinY) : new DomRect();
    }

    /// <summary>The bounding box of the content of an element that is not rendered (e.g. a clipPath element), in its user space, or null.</summary>
    public static (double MinX, double MinY, double MaxX, double MaxY)? ComputeContent(SvgElement element, SvgOutermostViewport? viewport = null, bool stroke = false)
        => element.IsConnected && GetBox(element, fill: true, stroke, depth: 0, viewport) is { } box ? (box.MinX, box.MinY, box.MaxX, box.MaxY) : null;

    /// <summary>
    /// Whether the element is rendered (see <see cref="BoxGeneration"/>): it is connected, it generates an SVG box (neither it
    /// nor its ancestors have display: none, it is not a never-rendered element such as a symbol or in one such as defs, it
    /// is in an svg element, and its conditional processing attributes do not prevent its rendering).
    /// </summary>
    private static bool IsRendered(SvgElement element) => SvgUserSpace.GetStyle(element) is { } style && BoxGeneration.Get(style).IsBeingRendered && SvgConditionalProcessing.IsRendered(element);

    private static string? GetComputedValue(Element element, string property) => SvgUserSpace.GetComputedValue(element, property);

    private static double? ResolveLength(Element element, string? value, double percentageBasis) => SvgUserSpace.ResolveLength(element, value, percentageBasis);

    /// <summary>Returns the bounding box of an element in its user space, or null when it has no geometry (it does not contribute to the bounding box of its parent).</summary>
    private static Box? GetBox(Element element, bool fill, bool stroke, int depth, SvgOutermostViewport? viewport)
    {
        if (depth > MaxDepth || element is not SvgElement || GetComputedValue(element, "display") == "none")
            return null;

        switch (element.LocalName)
        {
            case "rect":
            case "image":
            case "foreignObject":
            {
                var (width, height) = SvgUserSpace.GetViewportSize(element, viewport);
                var w = ResolveLength(element, GetComputedValue(element, "width"), width);
                var h = ResolveLength(element, GetComputedValue(element, "height"), height);

                // https://svgwg.org/svg2-draft/embedded.html#ImageElement: the auto size of an image is its natural size (a
                // negative size is an error: auto)
                if (element is SvgImageElement image && (w is null or < 0 || h is null or < 0) && image.GetUsedSize(w is >= 0 ? w : null, h is >= 0 ? h : null, GetComputedValue(element, "image-orientation") != "none") is { } used)
                {
                    (w, h) = used;
                }

                if (w is not > 0 || h is not > 0)
                    return null;

                var x = ResolveLength(element, GetComputedValue(element, "x"), width) ?? 0;
                var y = ResolveLength(element, GetComputedValue(element, "y"), height) ?? 0;
                var box = new Box(x, y, x + w.Value, y + h.Value);
                return element.LocalName == "rect" ? WithStroke(element, box, fill, stroke, viewport) : fill ? box : null;
            }

            case "circle":
            case "ellipse":
            {
                var (width, height) = SvgUserSpace.GetViewportSize(element, viewport);
                double rx, ry;
                if (element.LocalName == "circle")
                {
                    rx = ry = ResolveLength(element, GetComputedValue(element, "r"), SvgUserSpace.GetNormalizedDiagonal((width, height))) ?? 0;
                }
                else
                {
                    // auto is the other radius (https://svgwg.org/svg2-draft/geometry.html#RX)
                    // A negative radius is an error: it is auto
                    var specifiedRx = ResolveLength(element, GetComputedValue(element, "rx"), width) is >= 0 and var validRx ? validRx : (double?)null;
                    var specifiedRy = ResolveLength(element, GetComputedValue(element, "ry"), height) is >= 0 and var validRy ? validRy : (double?)null;
                    rx = specifiedRx ?? specifiedRy ?? 0;
                    ry = specifiedRy ?? specifiedRx ?? 0;
                }

                if (rx <= 0 || ry <= 0)
                    return null;

                var cx = ResolveLength(element, GetComputedValue(element, "cx"), width) ?? 0;
                var cy = ResolveLength(element, GetComputedValue(element, "cy"), height) ?? 0;
                return WithStroke(element, new Box(cx - rx, cy - ry, cx + rx, cy + ry), fill, stroke, viewport);
            }

            case "line":
            case "polyline":
            case "polygon":
                return SvgShapeGeometry.GetPath(element, viewport) is { Count: > 0 } commands ? GetPathBox(element, commands, fill, stroke, viewport) : null;

            case "path":
                return GetPathBox(element, ((SvgPathElement)element).GetUsedPathData(), fill, stroke, viewport);

            case "use":
                return GetUseBox((SvgElement)element, fill, stroke, depth, viewport);

            case "text" or "tspan" or "textPath":
                // https://svgwg.org/svg2-draft/coords.html#BoundingBoxes: the glyph cells of the text (the stroke is not included)
                return fill && SvgTextLayout.Get(element, viewport, inResource: true)?.GetBoundingBox(element) is { } textBox ? new Box(textBox.MinX, textBox.MinY, textBox.MaxX, textBox.MaxY) : null;

            case "g" or "a" or "svg" or "switch" or "symbol" or "clipPath":
            {
                Box? result = null;
                for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
                {
                    if (GetBox(child, fill, stroke, depth + 1, viewport) is not { } childBox)
                        continue;

                    result = Box.Union(result, childBox.Transform(SvgUserSpace.GetTransformToParent(child, viewport)));

                    // https://svgwg.org/svg2-draft/struct.html#SwitchElement: only the first direct child that is rendered
                    if (element.LocalName == "switch")
                        break;
                }

                return result;
            }

            default:
                // The other elements are not rendered
                return null;
        }
    }

    /// <summary>https://svgwg.org/svg2-draft/struct.html#UseElement: the bounding box of the referenced element, translated by x and y.</summary>
    private static Box? GetUseBox(SvgElement use, bool fill, bool stroke, int depth, SvgOutermostViewport? viewport)
    {
        if (GetUseTarget(use) is not { } referenced || GetBox(referenced, fill, stroke, depth + 1, viewport) is not { } box)
            return null;

        return box.Transform(SvgUserSpace.GetTransform(referenced, viewport).Then(SvgUserSpace.GetUseTranslation(use, viewport)));
    }

    /// <summary>
    /// Returns the element referenced by a use element in its document (or fragment) or in its external resource document
    /// (<see cref="SvgUseElement.GetReferencedElement"/>), or null (a missing reference, or one that contains the use element).
    /// </summary>
    public static Element? GetUseTarget(Element use)
    {
        var referenced = use is SvgUseElement useElement ? useElement.GetReferencedElement() : SvgLinking.GetReferencedElement(use);
        return referenced is null || referenced.IsInclusiveAncestorOf(use) ? null : referenced;
    }

    /// <summary>
    /// Returns the bounding box of an element of an SVG document fragment in the coordinates of the content box of its
    /// outermost svg element (https://drafts.csswg.org/cssom-view/#dom-element-getclientrects: "the bounding box of the element
    /// as defined by the SVG specification, applying the transforms that apply to the element and its ancestors"): its object
    /// bounding box (or its stroke bounding box when <paramref name="stroke"/> is true), transformed by its transform, the
    /// transforms and viewports of its ancestors and the viewBox and the zoom of the outermost svg element. Returns null when the
    /// element has no geometry.
    /// </summary>
    public static PhysicalQuad? GetBoundingQuad(SvgElement element, SvgOutermostViewport viewport, bool stroke = false)
    {
        if (!IsRendered(element) || GetBox(element, fill: true, stroke, depth: 0, viewport) is not { } box || SvgUserSpace.GetTransformToOutermostContentBox(element, out _, viewport) is not { } transform)
            return null;

        return new PhysicalQuad(ToOffset(transform.Apply(box.MinX, box.MinY)), ToOffset(transform.Apply(box.MaxX, box.MinY)), ToOffset(transform.Apply(box.MaxX, box.MaxY)), ToOffset(transform.Apply(box.MinX, box.MaxY)));

        static PhysicalOffset ToOffset(SvgPoint point) => new(point.X, point.Y);
    }

    /// <summary>
    /// Returns the size of the reference box of the transforms of an SVG element without CSS box
    /// (https://drafts.csswg.org/css-transforms-1/#transform-box, see <see cref="SvgUserSpace.GetTransformReferenceBox"/>). The
    /// viewport of the outermost svg element is the content box of its box (<paramref name="viewport"/>, from the layout).
    /// </summary>
    public static (double Width, double Height) GetTransformReferenceBox(SvgElement element, string transformBox, SvgOutermostViewport viewport)
    {
        var box = SvgUserSpace.GetTransformReferenceBox(element, transformBox, viewport);
        return (box.Width, box.Height);
    }

    /// <summary>The fill and stroke bounding boxes of a shape whose stroke box is its fill box expanded by half the stroke width (a rectangle with square or round corners, an ellipse).</summary>
    private static Box? WithStroke(Element element, Box box, bool fill, bool stroke, SvgOutermostViewport? viewport)
    {
        var result = fill ? box : (Box?)null;
        if (stroke && GetStrokeWidth(element, viewport) is { } strokeWidth)
        {
            var half = strokeWidth / 2;
            result = Box.Union(result, new Box(box.MinX - half, box.MinY - half, box.MaxX + half, box.MaxY + half));
        }

        return result;
    }

    /// <summary>The stroke width, or null when the element has no stroke.</summary>
    private static double? GetStrokeWidth(Element element, SvgOutermostViewport? viewport)
    {
        if (GetComputedValue(element, "stroke") is null or "none")
            return null;

        var value = GetComputedValue(element, "stroke-width") ?? "1";
        var first = value.Split(',')[0].Trim();
        return ResolveLength(element, first, SvgUserSpace.GetNormalizedDiagonal(SvgUserSpace.GetViewportSize(element, viewport))) is > 0 and var strokeWidth ? strokeWidth : null;
    }

    /// <summary>
    /// The bounding box of a path: the points of its segments, with the extrema of the curves and the arcs. A subpath that
    /// is a single moveto only contributes when it is the last one (https://svgwg.org/svg2-draft/coords.html#BoundingBoxes:
    /// the zero-length segments are included). The stroke box is computed from the flattened path, with its caps and joins.
    /// </summary>
    private static Box? GetPathBox(Element element, List<SvgPathCommand> commands, bool fill, bool stroke, SvgOutermostViewport? viewport)
    {
        var subpaths = Flatten(commands);
        if (subpaths.Count == 0)
            return null;

        Box? result = null;
        if (fill)
        {
            for (var i = 0; i < subpaths.Count; i++)
            {
                var subpath = subpaths[i];
                if (!subpath.HasSegments && i != subpaths.Count - 1)
                    continue;

                foreach (var point in subpath.Points)
                {
                    result = Box.Union(result, new Box(point.X, point.Y, point.X, point.Y));
                }
            }
        }

        if (stroke && GetStrokeWidth(element, viewport) is { } strokeWidth)
        {
            var cap = GetComputedValue(element, "stroke-linecap") ?? "butt";
            var join = GetComputedValue(element, "stroke-linejoin") ?? "miter";
            var miterLimit = double.TryParse(GetComputedValue(element, "stroke-miterlimit"), NumberStyles.Float, CultureInfo.InvariantCulture, out var limit) ? limit : 4;
            foreach (var subpath in subpaths)
            {
                if (subpath.HasSegments)
                {
                    result = Box.Union(result, GetStrokeBox(subpath, strokeWidth / 2, cap, join, miterLimit));
                }
            }
        }

        return result;
    }

    private static Box? GetStrokeBox(Subpath subpath, double half, string cap, string join, double miterLimit)
    {
        // The consecutive identical points do not change the direction of the stroke
        var points = new List<SvgPoint>(subpath.Points.Count);
        foreach (var point in subpath.Points)
        {
            if (points.Count == 0 || (point - points[^1]).Length > 1e-9)
            {
                points.Add(point);
            }
        }

        if (subpath.Closed && points.Count > 1 && (points[0] - points[^1]).Length <= 1e-9)
        {
            points.RemoveAt(points.Count - 1);
        }

        Box? result = null;
        void Add(SvgPoint point) => result = Box.Union(result, new Box(point.X, point.Y, point.X, point.Y));
        void AddCircle(SvgPoint center) => result = Box.Union(result, new Box(center.X - half, center.Y - half, center.X + half, center.Y + half));

        // https://svgwg.org/svg2-draft/painting.html#StrokeProperties: a zero-length subpath is stroked with its caps (aligned with the x axis)
        if (points.Count == 1)
        {
            if (cap == "round")
            {
                AddCircle(points[0]);
            }
            else if (cap == "square")
            {
                AddCircle(points[0]);
            }

            return result;
        }

        var count = points.Count;
        var segmentCount = subpath.Closed ? count : count - 1;
        for (var i = 0; i < segmentCount; i++)
        {
            var start = points[i];
            var end = points[(i + 1) % count];
            var normal = Normal(end - start) * half;
            Add(start + normal);
            Add(start - normal);
            Add(end + normal);
            Add(end - normal);
        }

        // The joins
        for (var i = subpath.Closed ? 0 : 1; i < (subpath.Closed ? count : count - 1); i++)
        {
            var vertex = points[i];
            var incoming = Direction(vertex - points[(i - 1 + count) % count]);
            var outgoing = Direction(points[(i + 1) % count] - vertex);
            if (join.Contains("round", StringComparison.Ordinal))
            {
                AddCircle(vertex);
                continue;
            }

            if (join.Contains("bevel", StringComparison.Ordinal))
                continue;

            // The miter join (https://svgwg.org/svg2-draft/painting.html#LineJoin): its length is half the stroke width / sin(θ/2)
            var normal1 = new SvgPoint(-incoming.Y, incoming.X);
            var normal2 = new SvgPoint(-outgoing.Y, outgoing.X);
            var dot = (normal1.X * normal2.X) + (normal1.Y * normal2.Y);
            if (1 + dot <= 1e-12)
                continue;

            var miter = (normal1 + normal2) * (half / (1 + dot));
            if (miter.Length / half > miterLimit)
                continue;

            var cross = (incoming.X * outgoing.Y) - (incoming.Y * outgoing.X);
            Add(cross > 0 ? vertex - miter : vertex + miter);
        }

        // The caps
        if (!subpath.Closed && cap != "butt")
        {
            var first = Direction(points[1] - points[0]);
            var last = Direction(points[^1] - points[^2]);
            if (cap == "round")
            {
                AddCircle(points[0]);
                AddCircle(points[^1]);
            }
            else
            {
                var normalFirst = new SvgPoint(-first.Y, first.X) * half;
                var normalLast = new SvgPoint(-last.Y, last.X) * half;
                Add(points[0] - (first * half) + normalFirst);
                Add(points[0] - (first * half) - normalFirst);
                Add(points[^1] + (last * half) + normalLast);
                Add(points[^1] + (last * half) - normalLast);
            }
        }

        return result;

        static SvgPoint Direction(SvgPoint vector) => vector.Length is > 0 and var length ? vector * (1 / length) : new SvgPoint(1, 0);

        static SvgPoint Normal(SvgPoint vector)
        {
            var direction = Direction(vector);
            return new SvgPoint(-direction.Y, direction.X);
        }
    }

    /// <summary>Converts the path to subpaths of points: the curves and the arcs are sampled, including their extrema, so that the bounding box of the points is the exact bounding box of the path.</summary>
    private static List<Subpath> Flatten(List<SvgPathCommand> commands)
    {
        var subpaths = new List<Subpath>();
        var normalizer = new Normalizer(keepArcs: true);
        var current = default(SvgPoint);
        foreach (var command in commands)
        {
            normalizer.Emit(command, (_, segment) =>
            {
                switch (segment.Type)
                {
                    case 'M':
                        subpaths.Add(new Subpath([segment.Target]));
                        break;

                    case 'Z':
                        if (subpaths.Count > 0)
                        {
                            var closed = subpaths[^1];
                            closed.HasSegments = true;
                            closed.Closed = true;
                            closed.Points.Add(segment.Target);

                            // A segment after a closepath starts a new subpath at the same point
                            subpaths.Add(new Subpath([segment.Target]) { IsImplicit = true });
                        }

                        break;

                    default:
                        if (subpaths.Count == 0)
                        {
                            subpaths.Add(new Subpath([current]));
                        }

                        var subpath = subpaths[^1];
                        subpath.HasSegments = true;
                        AddSegmentPoints(subpath.Points, current, segment);
                        break;
                }

                current = segment.Target;
            }, 0);
        }

        // The implicit subpaths after a closepath that have no segment are not subpaths
        subpaths.RemoveAll(subpath => subpath.IsImplicit && !subpath.HasSegments);
        return subpaths;
    }

    private static void AddSegmentPoints(List<SvgPoint> points, SvgPoint start, NormalizedSegment segment)
    {
        switch (segment.Type)
        {
            case 'L':
                points.Add(segment.Target);
                break;

            case 'C':
            {
                var parameters = new List<double>();
                for (var i = 1; i < CurveSamples; i++)
                {
                    parameters.Add((double)i / CurveSamples);
                }

                AddCubicExtrema(parameters, start.X, segment.Point1.X, segment.Point2.X, segment.Target.X);
                AddCubicExtrema(parameters, start.Y, segment.Point1.Y, segment.Point2.Y, segment.Target.Y);
                parameters.Sort();
                foreach (var t in parameters)
                {
                    points.Add(CubicPoint(start, segment.Point1, segment.Point2, segment.Target, t));
                }

                points.Add(segment.Target);
                break;
            }

            case 'A':
            {
                var arc = segment.Arc;
                var angles = new List<double>();
                for (var i = 1; i < CurveSamples; i++)
                {
                    angles.Add(arc.StartAngle + (arc.SweepAngle * i / CurveSamples));
                }

                // The angles where the derivative of x or y is zero
                foreach (var extremum in new[] { Math.Atan2(-arc.Sin * arc.RadiusY, arc.Cos * arc.RadiusX), Math.Atan2(arc.Cos * arc.RadiusY, arc.Sin * arc.RadiusX) })
                {
                    for (var k = -4; k <= 4; k++)
                    {
                        var angle = extremum + (k * Math.PI);
                        var offset = (angle - arc.StartAngle) / arc.SweepAngle;
                        if (offset is > 0 and < 1)
                        {
                            angles.Add(angle);
                        }
                    }
                }

                angles.Sort((a, b) => ((a - arc.StartAngle) / arc.SweepAngle).CompareTo((b - arc.StartAngle) / arc.SweepAngle));
                foreach (var angle in angles)
                {
                    points.Add(arc.PointAt(angle));
                }

                points.Add(segment.Target);
                break;
            }

            default:
                points.Add(segment.Target);
                break;
        }
    }

    /// <summary>Adds the parameters in (0, 1) where the derivative of a cubic Bézier is zero.</summary>
    private static void AddCubicExtrema(List<double> parameters, double p0, double p1, double p2, double p3)
    {
        // B'(t) / 3 = a t² + b t + c
        var a = -p0 + (3 * p1) - (3 * p2) + p3;
        var b = 2 * (p0 - (2 * p1) + p2);
        var c = p1 - p0;
        if (Math.Abs(a) < 1e-12)
        {
            if (Math.Abs(b) > 1e-12)
            {
                AddParameter(-c / b);
            }

            return;
        }

        var discriminant = (b * b) - (4 * a * c);
        if (discriminant < 0)
            return;

        var root = Math.Sqrt(discriminant);
        AddParameter((-b + root) / (2 * a));
        AddParameter((-b - root) / (2 * a));

        void AddParameter(double t)
        {
            if (t is > 0 and < 1)
            {
                parameters.Add(t);
            }
        }
    }

    private static SvgPoint CubicPoint(SvgPoint p0, SvgPoint p1, SvgPoint p2, SvgPoint p3, double t)
    {
        var u = 1 - t;
        return (p0 * (u * u * u)) + (p1 * (3 * u * u * t)) + (p2 * (3 * u * t * t)) + (p3 * (t * t * t));
    }

    private sealed class Subpath(List<SvgPoint> points)
    {
        public List<SvgPoint> Points { get; } = points;

        public bool HasSegments { get; set; }

        public bool Closed { get; set; }

        public bool IsImplicit { get; init; }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct Box(double MinX, double MinY, double MaxX, double MaxY)
    {
        public static Box Union(Box? a, Box b) => a is { } box ? new Box(Math.Min(box.MinX, b.MinX), Math.Min(box.MinY, b.MinY), Math.Max(box.MaxX, b.MaxX), Math.Max(box.MaxY, b.MaxY)) : b;

        public static Box? Union(Box? a, Box? b) => b is { } box ? Union(a, box) : a;

        public Box Transform(SvgAffine matrix)
        {
            if (matrix.IsIdentity)
                return this;

            var corners = new[] { matrix.Apply(MinX, MinY), matrix.Apply(MaxX, MinY), matrix.Apply(MinX, MaxY), matrix.Apply(MaxX, MaxY) };
            return new Box(corners.Min(c => c.X), corners.Min(c => c.Y), corners.Max(c => c.X), corners.Max(c => c.Y));
        }
    }
}
