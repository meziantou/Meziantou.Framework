using Meziantou.Framework.WebPlatform.Css.Cascade;
using System.Globalization;
using Meziantou.Framework.WebPlatform.Dom;
using static Meziantou.Framework.WebPlatform.Svg.SvgPathData;

namespace Meziantou.Framework.WebPlatform.Svg;

/// <summary>
/// The geometry of the basic shapes and the paths (https://svgwg.org/svg2-draft/shapes.html, https://svgwg.org/svg2-draft/paths.html)
/// as path data in user units, and the tests of a point against their fill and their stroke
/// (https://svgwg.org/svg2-draft/painting.html#FillProperties, https://svgwg.org/svg2-draft/painting.html#StrokeShape) for
/// <c>isPointInFill()</c>, <c>isPointInStroke()</c> and the hit testing. The curves are flattened to polylines whose distance
/// to the curve is at most <see cref="FlatnessTolerance"/>; the points at that distance of the boundary are inside.
/// </summary>
internal sealed class SvgShapeGeometry
{
    /// <summary>The maximum distance in user units between a curve and the polyline that approximates it.</summary>
    private const double FlatnessTolerance = 0.002;
    private const int MaxSegmentsPerCurve = 4096;

    private readonly List<SvgPathCommand> _commands;
    private List<Polyline>? _polylines;
    private SvgPathGeometry? _pathGeometry;

    private SvgShapeGeometry(List<SvgPathCommand> commands)
    {
        _commands = commands;
    }

    /// <summary>The path data of the shape (absolute or relative commands).</summary>
    public List<SvgPathCommand> Commands => _commands;

    /// <summary>Whether the path has no segment (its length is zero and it is not rendered).</summary>
    public bool IsEmpty => _commands.Count == 0;

    /// <summary>The distance along the path (see <see cref="SvgPathGeometry"/>).</summary>
    public SvgPathGeometry PathGeometry => _pathGeometry ??= new SvgPathGeometry(_commands);

    /// <summary>The subpaths flattened to polylines.</summary>
    private List<Polyline> Polylines => _polylines ??= Flatten(_commands);

    /// <summary>Returns the geometry of a geometry element (an empty geometry for another element).</summary>
    public static SvgShapeGeometry Get(Element element, SvgOutermostViewport? viewport = null) => new(GetPath(element, viewport));

    /// <summary>Creates the geometry of path data.</summary>
    public static SvgShapeGeometry FromCommands(List<SvgPathCommand> commands) => new(commands);

    /// <summary>
    /// Returns the path data equivalent to a shape, in user units: its geometry properties (or the attributes of the line, the
    /// polyline and the polygon) resolved in its viewport. A shape that is not rendered because of its size has no path data.
    /// </summary>
    public static List<SvgPathCommand> GetPath(Element element, SvgOutermostViewport? viewport = null)
    {
        switch (element.LocalName)
        {
            case "rect":
                return GetRectPath(element, viewport);

            case "circle":
            case "ellipse":
            {
                var (width, height) = SvgUserSpace.GetViewportSize(element, viewport);
                double rx, ry;
                if (element.LocalName == "circle")
                {
                    rx = ry = Math.Max(0, Length(element, "r", SvgUserSpace.GetNormalizedDiagonal((width, height))) ?? 0);
                }
                else
                {
                    // https://svgwg.org/svg2-draft/geometry.html#RX: auto (or a negative value) is the other radius
                    var specifiedRx = Length(element, "rx", width) is >= 0 and var validRx ? validRx : (double?)null;
                    var specifiedRy = Length(element, "ry", height) is >= 0 and var validRy ? validRy : (double?)null;
                    rx = specifiedRx ?? specifiedRy ?? 0;
                    ry = specifiedRy ?? specifiedRx ?? 0;
                }

                if (rx <= 0 || ry <= 0)
                    return [];

                // https://svgwg.org/svg2-draft/shapes.html#CircleElement: the path starts at (cx + r, cy) and goes clockwise
                var cx = Length(element, "cx", width) ?? 0;
                var cy = Length(element, "cy", height) ?? 0;
                return
                [
                    Command('M', cx + rx, cy),
                    Command('A', rx, ry, 0, 0, 1, cx, cy + ry),
                    Command('A', rx, ry, 0, 0, 1, cx - rx, cy),
                    Command('A', rx, ry, 0, 0, 1, cx, cy - ry),
                    Command('A', rx, ry, 0, 0, 1, cx + rx, cy),
                    Command('Z'),
                ];
            }

            case "line":
            {
                var (width, height) = SvgUserSpace.GetViewportSize(element, viewport);
                var x1 = SvgUserSpace.ResolveLength(element, element.GetAnimatedAttributeValue("x1"), width) ?? 0;
                var y1 = SvgUserSpace.ResolveLength(element, element.GetAnimatedAttributeValue("y1"), height) ?? 0;
                var x2 = SvgUserSpace.ResolveLength(element, element.GetAnimatedAttributeValue("x2"), width) ?? 0;
                var y2 = SvgUserSpace.ResolveLength(element, element.GetAnimatedAttributeValue("y2"), height) ?? 0;
                return [Command('M', x1, y1), Command('L', x2, y2)];
            }

            case "polyline":
            case "polygon":
            {
                // https://svgwg.org/svg2-draft/shapes.html#PolylineElement: the points up to the first error, an odd last coordinate is ignored
                var numbers = ParsePoints(element.GetAnimatedAttributeValue("points"));
                var commands = new List<SvgPathCommand>();
                for (var i = 0; i + 1 < numbers.Count; i += 2)
                {
                    commands.Add(Command(i == 0 ? 'M' : 'L', numbers[i], numbers[i + 1]));
                }

                if (element.LocalName == "polygon" && commands.Count > 0)
                {
                    commands.Add(Command('Z'));
                }

                return commands;
            }

            case "path" when element is SvgPathElement path:
                return path.GetUsedPathData();

            default:
                return [];
        }
    }

    /// <summary>https://svgwg.org/svg2-draft/shapes.html#RectElement: the path of a rectangle, with its rounded corners.</summary>
    private static List<SvgPathCommand> GetRectPath(Element element, SvgOutermostViewport? viewport)
    {
        var (width, height) = SvgUserSpace.GetViewportSize(element, viewport);
        var w = Length(element, "width", width) ?? 0;
        var h = Length(element, "height", height) ?? 0;
        if (w <= 0 || h <= 0)
            return [];

        var x = Length(element, "x", width) ?? 0;
        var y = Length(element, "y", height) ?? 0;

        // https://svgwg.org/svg2-draft/geometry.html#RX: auto (or a negative value) is the other radius, clamped to half the size
        var specifiedRx = Length(element, "rx", width) is >= 0 and var validRx ? validRx : (double?)null;
        var specifiedRy = Length(element, "ry", height) is >= 0 and var validRy ? validRy : (double?)null;
        var rx = Math.Min(specifiedRx ?? specifiedRy ?? 0, w / 2);
        var ry = Math.Min(specifiedRy ?? specifiedRx ?? 0, h / 2);
        if (rx <= 0 || ry <= 0)
            return [Command('M', x, y), Command('H', x + w), Command('V', y + h), Command('H', x), Command('Z')];

        return
        [
            Command('M', x + rx, y),
            Command('H', x + w - rx),
            Command('A', rx, ry, 0, 0, 1, x + w, y + ry),
            Command('V', y + h - ry),
            Command('A', rx, ry, 0, 0, 1, x + w - rx, y + h),
            Command('H', x + rx),
            Command('A', rx, ry, 0, 0, 1, x, y + h - ry),
            Command('V', y + ry),
            Command('A', rx, ry, 0, 0, 1, x + rx, y),
            Command('Z'),
        ];
    }

    private static double? Length(Element element, string property, double percentageBasis) => SvgUserSpace.ResolveLength(element, SvgUserSpace.GetComputedValue(element, property), percentageBasis);

    private static SvgPathCommand Command(char type, params double[] values) => new(type, Array.ConvertAll(values, v => (float)v));

    /// <summary>Parses the points attribute up to the first error (https://svgwg.org/svg2-draft/shapes.html#DataTypePoints).</summary>
    private static List<double> ParsePoints(string? value)
    {
        var result = new List<double>();
        if (value is null)
            return result;

        var position = 0;
        SvgParsing.SkipWhitespace(value, ref position);
        while (position < value.Length)
        {
            if (!SvgParsing.TryParseNumber(value, ref position, out var number))
                break;

            result.Add(number);
            SvgParsing.SkipCommaWhitespace(value, ref position);
        }

        return result;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Fill
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether a point is inside the fill of the shape (https://svgwg.org/svg2-draft/painting.html#WindingRule): the subpaths are
    /// closed, with the nonzero or evenodd rule. The points of the boundary are inside.
    /// </summary>
    public bool IsPointInFill(SvgPoint point, bool evenOdd)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            return false;

        var winding = 0;
        var crossings = 0;
        foreach (var polyline in Polylines)
        {
            var points = polyline.Points;
            if (points.Count == 0)
                continue;

            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                if (DistanceToSegment(point, a, b) <= FlatnessTolerance)
                    return true;

                // https://en.wikipedia.org/wiki/Point_in_polygon#Winding_number_algorithm
                if (a.Y <= point.Y)
                {
                    if (b.Y > point.Y && Cross(a, b, point) > 0)
                    {
                        winding++;
                        crossings++;
                    }
                }
                else if (b.Y <= point.Y && Cross(a, b, point) < 0)
                {
                    winding--;
                    crossings++;
                }
            }
        }

        return evenOdd ? (crossings & 1) != 0 : winding != 0;
    }

    /// <summary>
    /// Whether the fill (when <paramref name="fill"/> is true) or the stroke (widened by <paramref name="strokeHalfWidth"/> when
    /// it is positive) of the shape intersects a rectangle, the shape being mapped by <paramref name="toTarget"/> to the
    /// coordinates of the rectangle (<c>getIntersectionList()</c>; the dashes are ignored).
    /// </summary>
    public bool Intersects(SvgAffine toTarget, (double MinX, double MinY, double MaxX, double MaxY) rect, bool fill, bool evenOdd, double strokeHalfWidth)
    {
        var scale = Math.Sqrt(Math.Abs(toTarget.Determinant));
        var expanded = strokeHalfWidth > 0 ? (rect.MinX - (strokeHalfWidth * scale), rect.MinY - (strokeHalfWidth * scale), rect.MaxX + (strokeHalfWidth * scale), rect.MaxY + (strokeHalfWidth * scale)) : rect;
        foreach (var polyline in Polylines)
        {
            var points = polyline.Points;
            var closed = polyline.Closed || fill;
            if (points.Count == 1 && SvgIntersection.Contains(expanded, toTarget.Apply(points[0])))
                return true;

            var segmentCount = closed ? points.Count : points.Count - 1;
            for (var i = 0; i < segmentCount; i++)
            {
                var a = toTarget.Apply(points[i]);
                var b = toTarget.Apply(points[(i + 1) % points.Count]);
                if ((fill && SvgIntersection.SegmentIntersects(a, b, rect)) || (strokeHalfWidth > 0 && SvgIntersection.SegmentIntersects(a, b, expanded)))
                    return true;
            }
        }

        // A rectangle inside the fill
        if (fill && toTarget.Invert() is { } inverse)
            return IsPointInFill(inverse.Apply(rect.MinX, rect.MinY), evenOdd);

        return false;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Stroke (https://svgwg.org/svg2-draft/painting.html#StrokeShape)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether a point is inside the stroke shape of the shape: the segments widened by half the stroke width, the joins and the
    /// caps, after the dashing. <paramref name="transform"/> maps the user space to the space where the stroke width applies
    /// (<c>vector-effect: non-scaling-stroke</c>), the identity otherwise.
    /// </summary>
    public bool IsPointInStroke(SvgPoint point, SvgStrokeStyle stroke, SvgAffine transform)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || stroke.Width <= 0)
            return false;

        point = transform.Apply(point);
        var half = stroke.Width / 2;
        foreach (var polyline in GetStrokedPolylines(stroke))
        {
            var points = polyline.Points;
            if (!transform.IsIdentity)
            {
                points = points.ConvertAll(transform.Apply);
            }

            if (IsInStroke(points, polyline.Closed, point, half, stroke))
                return true;
        }

        return false;
    }

    /// <summary>The polylines of the stroke: the subpaths, or their dashes (https://svgwg.org/svg2-draft/painting.html#StrokeDashing).</summary>
    private List<Polyline> GetStrokedPolylines(SvgStrokeStyle stroke)
    {
        if (stroke.DashArray is not { Length: > 0 } dashes)
            return Polylines;

        // https://svgwg.org/svg2-draft/paths.html#PathLengthAttribute: the lengths of the dashes are in the units of pathLength
        var scale = stroke.PathLength is > 0 and var pathLength ? PathGeometry.TotalLength / pathLength : 1;
        var total = 0d;
        foreach (var dash in dashes)
        {
            total += dash * scale;
        }

        if (total <= 0)
            return Polylines;

        var result = new List<Polyline>();
        foreach (var polyline in Polylines)
        {
            var points = polyline.Points;
            if (polyline.Closed && points.Count > 0)
            {
                points = [.. points, points[0]];
            }

            // Each subpath starts the dash pattern at the dash offset
            var offset = stroke.DashOffset * scale % total;
            if (offset < 0)
            {
                offset += total;
            }

            var index = 0;
            while (offset >= dashes[index] * scale)
            {
                offset -= dashes[index] * scale;
                index = (index + 1) % dashes.Length;
            }

            var remaining = (dashes[index] * scale) - offset;
            var on = index % 2 == 0;
            List<SvgPoint>? current = on && points.Count > 0 ? [points[0]] : null;
            for (var i = 0; i + 1 < points.Count; i++)
            {
                var start = points[i];
                var end = points[i + 1];
                var length = (end - start).Length;
                var position = 0d;
                while (length - position > remaining)
                {
                    position += remaining;
                    var split = SvgPoint.Lerp(start, end, position / length);
                    if (on)
                    {
                        current!.Add(split);
                        result.Add(new Polyline(current, Closed: false));
                        current = null;
                    }
                    else
                    {
                        current = [split];
                    }

                    on = !on;
                    index = (index + 1) % dashes.Length;
                    remaining = dashes[index] * scale;
                }

                remaining -= length - position;
                current?.Add(end);
            }

            if (current is { Count: > 0 })
            {
                result.Add(new Polyline(current, Closed: false));
            }
        }

        return result;
    }

    private static bool IsInStroke(List<SvgPoint> input, bool closed, SvgPoint point, double half, SvgStrokeStyle stroke)
    {
        // The consecutive identical points do not change the direction of the stroke
        var points = new List<SvgPoint>(input.Count);
        foreach (var p in input)
        {
            if (points.Count == 0 || (p - points[^1]).Length > 1e-12)
            {
                points.Add(p);
            }
        }

        if (closed && points.Count > 1 && (points[0] - points[^1]).Length <= 1e-12)
        {
            points.RemoveAt(points.Count - 1);
        }

        if (points.Count == 0)
            return false;

        // https://svgwg.org/svg2-draft/painting.html#LineCaps: a zero-length subpath is stroked with its caps, aligned with the x axis
        if (points.Count == 1)
        {
            var delta = point - points[0];
            return stroke.LineCap switch
            {
                "round" => delta.Length <= half,
                "square" => Math.Abs(delta.X) <= half && Math.Abs(delta.Y) <= half,
                _ => false,
            };
        }

        var count = points.Count;
        var segmentCount = closed ? count : count - 1;
        for (var i = 0; i < segmentCount; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % count];
            var direction = b - a;
            var lengthSquared = (direction.X * direction.X) + (direction.Y * direction.Y);
            var t = (((point.X - a.X) * direction.X) + ((point.Y - a.Y) * direction.Y)) / lengthSquared;
            // The points of the boundary of the stroke are inside (with a tolerance for the rounding errors)
            if (t >= -1e-9 && t <= 1 + 1e-9 && Math.Abs(Cross(a, b, point)) / Math.Sqrt(lengthSquared) <= half + 1e-9)
                return true;
        }

        // https://svgwg.org/svg2-draft/painting.html#LineJoin
        for (var i = closed ? 0 : 1; i < (closed ? count : count - 1); i++)
        {
            var vertex = points[i];
            var incoming = Normalize(vertex - points[(i - 1 + count) % count]);
            var outgoing = Normalize(points[(i + 1) % count] - vertex);
            if (IsInJoin(point, vertex, incoming, outgoing, half, stroke))
                return true;
        }

        // https://svgwg.org/svg2-draft/painting.html#LineCaps
        if (!closed)
        {
            if (IsInCap(point, points[0], Normalize(points[0] - points[1]), half, stroke.LineCap) || IsInCap(point, points[^1], Normalize(points[^1] - points[^2]), half, stroke.LineCap))
                return true;
        }

        return false;
    }

    /// <summary>Whether a point is in the cap at the end of a subpath, whose outward direction is given.</summary>
    private static bool IsInCap(SvgPoint point, SvgPoint end, SvgPoint outward, double half, string cap)
    {
        var delta = point - end;
        switch (cap)
        {
            case "round":
                return delta.Length <= half;
            case "square":
                var along = (delta.X * outward.X) + (delta.Y * outward.Y);
                var across = (delta.X * -outward.Y) + (delta.Y * outward.X);
                return along >= 0 && along <= half && Math.Abs(across) <= half;
            default:
                return false;
        }
    }

    /// <summary>Whether a point is in the join of two segments at a vertex (the area outside the two widened segments).</summary>
    private static bool IsInJoin(SvgPoint point, SvgPoint vertex, SvgPoint incoming, SvgPoint outgoing, double half, SvgStrokeStyle stroke)
    {
        var join = stroke.LineJoin;
        if (join == "round")
            return (point - vertex).Length <= half;

        // The outer side of the join: the normals of the segments on the side of the turn
        var cross = (incoming.X * outgoing.Y) - (incoming.Y * outgoing.X);
        if (Math.Abs(cross) < 1e-12 && (incoming.X * outgoing.X) + (incoming.Y * outgoing.Y) > 0)
            return false;

        var sign = cross > 0 ? -1 : 1;
        var normal1 = new SvgPoint(-incoming.Y, incoming.X) * (half * sign);
        var normal2 = new SvgPoint(-outgoing.Y, outgoing.X) * (half * sign);
        var corner1 = vertex + normal1;
        var corner2 = vertex + normal2;
        if (join is "miter" or "miter-clip" or "arcs")
        {
            // The miter length is the stroke width / sin(θ / 2), θ being the angle between the segments
            var cosTheta = -((incoming.X * outgoing.X) + (incoming.Y * outgoing.Y));
            var sinHalf = Math.Sqrt(Math.Max(0, (1 - cosTheta) / 2));
            if (sinHalf > 0 && 1 / sinHalf <= stroke.MiterLimit)
            {
                var bisector = Normalize(normal1 + normal2);
                var tip = vertex + (bisector * (half / sinHalf));
                return IsInTriangle(point, vertex, corner1, tip) || IsInTriangle(point, vertex, tip, corner2);
            }
        }

        // The bevel join (and the miter join beyond the miter limit)
        return IsInTriangle(point, vertex, corner1, corner2);
    }

    private static bool IsInTriangle(SvgPoint point, SvgPoint a, SvgPoint b, SvgPoint c)
    {
        var d1 = Cross(a, b, point);
        var d2 = Cross(b, c, point);
        var d3 = Cross(c, a, point);
        const double Epsilon = 1e-9;
        var hasNegative = d1 < -Epsilon || d2 < -Epsilon || d3 < -Epsilon;
        var hasPositive = d1 > Epsilon || d2 > Epsilon || d3 > Epsilon;
        return !(hasNegative && hasPositive);
    }

    private static SvgPoint Normalize(SvgPoint vector) => vector.Length is > 0 and var length ? vector * (1 / length) : new SvgPoint(1, 0);

    private static double Cross(SvgPoint a, SvgPoint b, SvgPoint p) => ((b.X - a.X) * (p.Y - a.Y)) - ((b.Y - a.Y) * (p.X - a.X));

    private static double DistanceToSegment(SvgPoint point, SvgPoint a, SvgPoint b)
    {
        var direction = b - a;
        var lengthSquared = (direction.X * direction.X) + (direction.Y * direction.Y);
        if (lengthSquared == 0)
            return (point - a).Length;

        var t = Math.Clamp((((point.X - a.X) * direction.X) + ((point.Y - a.Y) * direction.Y)) / lengthSquared, 0, 1);
        return (point - (a + (direction * t))).Length;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Flattening
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Converts the path to polylines, one per subpath (a subpath of a single moveto is a point, which is stroked with its caps when it is the last one).</summary>
    private static List<Polyline> Flatten(List<SvgPathCommand> commands)
    {
        var result = new List<Polyline>();
        var normalizer = new Normalizer(keepArcs: true);
        List<SvgPoint>? current = null;
        var closed = false;
        var hasSegments = false;
        var position = default(SvgPoint);
        foreach (var command in commands)
        {
            normalizer.Emit(command, (_, segment) =>
            {
                switch (segment.Type)
                {
                    case 'M':
                        Flush();
                        current = [segment.Target];
                        break;

                    case 'Z':
                        current ??= [position];
                        closed = true;
                        hasSegments = true;
                        Flush();

                        // A segment after a closepath starts a new subpath at the same point
                        current = null;
                        break;

                    default:
                        current ??= [position];
                        hasSegments = true;
                        AddSegment(current, position, segment);
                        break;
                }

                position = segment.Target;
            }, 0);
        }

        // https://svgwg.org/svg2-draft/paths.html#PathDataMovetoCommands: the last subpath that is a single moveto is stroked with its caps (a zero-length subpath)
        if (current is not null)
        {
            result.Add(new Polyline(current, closed));
        }

        return result;

        void Flush()
        {
            if (current is not null && (hasSegments || closed))
            {
                result.Add(new Polyline(current, closed));
            }

            current = null;
            closed = false;
            hasSegments = false;
        }
    }

    private static void AddSegment(List<SvgPoint> points, SvgPoint start, NormalizedSegment segment)
    {
        switch (segment.Type)
        {
            case 'C':
            {
                var polygon = (segment.Point1 - start).Length + (segment.Point2 - segment.Point1).Length + (segment.Target - segment.Point2).Length;
                var count = (int)Math.Clamp(Math.Ceiling(Math.Sqrt(polygon / FlatnessTolerance)), 1, MaxSegmentsPerCurve);
                for (var i = 1; i < count; i++)
                {
                    var t = (double)i / count;
                    var u = 1 - t;
                    points.Add((start * (u * u * u)) + (segment.Point1 * (3 * u * u * t)) + (segment.Point2 * (3 * u * t * t)) + (segment.Target * (t * t * t)));
                }

                points.Add(segment.Target);
                break;
            }

            case 'A':
            {
                var arc = segment.Arc;
                var radius = Math.Max(arc.RadiusX, arc.RadiusY);

                // The angle step for which the sagitta of a chord is the tolerance: r (1 - cos(step / 2)) = tolerance
                var step = radius > FlatnessTolerance ? 2 * Math.Acos(1 - (FlatnessTolerance / radius)) : Math.PI / 2;
                var count = (int)Math.Clamp(Math.Ceiling(Math.Abs(arc.SweepAngle) / step), 1, MaxSegmentsPerCurve);
                for (var i = 1; i < count; i++)
                {
                    points.Add(arc.PointAt(arc.StartAngle + (arc.SweepAngle * i / count)));
                }

                points.Add(segment.Target);
                break;
            }

            default:
                points.Add(segment.Target);
                break;
        }
    }

    /// <summary>A subpath flattened to a polyline.</summary>
    private sealed record Polyline(List<SvgPoint> Points, bool Closed);
}

/// <summary>The stroke properties of an element (https://svgwg.org/svg2-draft/painting.html#StrokeProperties), in user units.</summary>
internal sealed record SvgStrokeStyle(double Width, string LineCap, string LineJoin, double MiterLimit, double[]? DashArray, double DashOffset, double? PathLength, bool NonScaling)
{
    /// <summary>Returns the stroke properties of an element (the lengths resolved in its viewport).</summary>
    /// <param name="element">The element.</param>
    /// <param name="viewport">The viewport of the outermost svg element, or null.</param>
    /// <param name="style">The style of the element, or null for its computed style (e.g. the style of an element instance of a use-element shadow tree).</param>
    public static SvgStrokeStyle FromElement(Element element, SvgOutermostViewport? viewport = null, ComputedStyle? style = null)
    {
        string? GetComputedValue(string property) => style is not null ? style.GetComputedValue(property) : SvgUserSpace.GetComputedValue(element, property);

        var diagonal = SvgUserSpace.GetNormalizedDiagonal(SvgUserSpace.GetViewportSize(element, viewport));
        var width = SvgUserSpace.ResolveLength(element, GetComputedValue("stroke-width") ?? "1", diagonal) ?? 1;
        var miterLimit = double.TryParse(GetComputedValue("stroke-miterlimit"), NumberStyles.Float, CultureInfo.InvariantCulture, out var limit) ? limit : 4;
        double[]? dashes = null;
        if (GetComputedValue("stroke-dasharray") is { } dashArray && dashArray is not ("none" or ""))
        {
            var parts = SplitDashArray(dashArray);
            var values = new List<double>(parts.Count * 2);
            foreach (var part in parts)
            {
                if (SvgUserSpace.ResolveLength(element, part, diagonal) is not { } value || value < 0)
                {
                    values.Clear();
                    break;
                }

                values.Add(value);
            }

            // https://svgwg.org/svg2-draft/painting.html#StrokeDashing: an odd number of values is repeated
            if (values.Count % 2 == 1)
            {
                values.AddRange(values);
            }

            dashes = values.Count > 0 ? [.. values] : null;
        }

        var offset = SvgUserSpace.ResolveLength(element, GetComputedValue("stroke-dashoffset"), diagonal) ?? 0;
        var pathLength = element is SvgGeometryElement ? GetPathLength(element, style) : null;
        return new SvgStrokeStyle(Math.Max(0, width), GetComputedValue("stroke-linecap") ?? "butt", GetComputedValue("stroke-linejoin") ?? "miter", miterLimit, dashes, offset, pathLength,
            GetComputedValue("vector-effect") == "non-scaling-stroke");
    }

    /// <summary>
    /// Returns the author path length of a shape in user units (https://svgwg.org/svg2-draft/paths.html#PathLengthCSSProperty): the
    /// used value of its <c>path-length</c> property, whose presentation attribute is <c>pathLength</c> (the attribute of an
    /// element without style), or null for <c>none</c>.
    /// </summary>
    public static double? GetPathLength(Element element, ComputedStyle? style = null)
    {
        style ??= SvgUserSpace.GetStyle(element);
        if (style is null)
            return SvgParsing.TryParseSingleNumber(element.GetAnimatedAttributeValue("pathLength"), out var number) && number >= 0 ? number : null;

        var value = style.GetComputedValue("path-length");
        return value is "none" or "" || SvgUserSpace.ResolveLength(element, value, 0) is not { } length || !(length >= 0) ? null : length;
    }

    /// <summary>Splits the lengths of a dash array, which are separated by commas or white space (a math function is one length).</summary>
    private static List<string> SplitDashArray(string value)
    {
        if (!value.Contains('(', StringComparison.Ordinal))
            return [.. value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)];

        var result = new List<string>();
        foreach (var component in Css.Syntax.CssParser.ParseListOfComponentValues(value))
        {
            if (component is Css.Syntax.CssPreservedToken { Type: Css.Syntax.CssTokenType.Whitespace or Css.Syntax.CssTokenType.Comma })
                continue;

            result.Add(Css.Values.CssSerializer.SerializeValue([component], property: null));
        }

        return result;
    }
}
