using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The boolean operations of <see cref="PathOperations.Combine"/> on the areas of two paths.</summary>
internal enum PathOperation : byte
{
    /// <summary>The points in one of the areas.</summary>
    Union,

    /// <summary>The points in the first area that are not in the second one.</summary>
    Difference,

    /// <summary>The points in both areas.</summary>
    Intersection,
}

/// <summary>
/// Operations on the areas of the paths, computed by the library so that the backends only fill and clip paths: the area
/// of a stroke (the outline of the stroke as a path), the boolean operations on two areas, and the area of a path moved
/// outwards or inwards by a distance. They are the geometry of <c>border-shape</c> (see <see cref="BorderShapeGeometry"/>),
/// whose areas are the union and the difference of a shape and of its stroke. The curves are flattened (to 0.05px).
/// </summary>
internal static class PathOperations
{
    // The distance between a curve and its flattened segments
    private const double Tolerance = 0.05;
    private const double Epsilon = 1e-9;

    /// <summary>A subpath of a flattened path: its points, and whether it is closed (the last point is connected to the first one).</summary>
    private readonly record struct Contour(List<PhysicalOffset> Points, bool IsClosed);

    /// <summary>An edge of an area, from top to bottom: the direction of its contour (1 when it goes down) and the operand it is an edge of.</summary>
    private readonly record struct Edge(double X0, double Y0, double X1, double Y1, sbyte Winding, bool IsSecond)
    {
        public double GetX(double y) => y <= Y0 ? X0 : y >= Y1 ? X1 : X0 + ((y - Y0) * (X1 - X0) / (Y1 - Y0));
    }

    /// <summary>
    /// Returns the area of the stroke of a path as a path: the quadrilaterals of the segments and the areas of the joins
    /// (https://svgwg.org/svg2-draft/painting.html#StrokeShape), all in the same direction, so their union is the area of
    /// the path with the non-zero fill rule. The open subpaths have butt caps; the dashes are ignored.
    /// </summary>
    public static DisplayPath CreateStrokeOutline(DisplayPath path, StrokeStyle stroke)
    {
        var result = new DisplayPath { FillRule = FillRule.NonZero };
        var half = stroke.Width / 2;
        if (!(half > 0))
            return result;

        foreach (var contour in Flatten(path))
        {
            var points = contour.Points;
            var count = points.Count;
            if (count < 2)
                continue;

            var segments = contour.IsClosed ? count : count - 1;
            for (var i = 0; i < segments; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % count];
                var direction = Normalize(b - a);
                var normal = new PhysicalOffset(direction.Y * half, -direction.X * half);
                AddPolygon(result, [a + normal, b + normal, b - normal, a - normal]);
            }

            for (var i = contour.IsClosed ? 0 : 1; i < (contour.IsClosed ? count : count - 1); i++)
            {
                AddJoin(result, points[i], Normalize(points[i] - points[(i + count - 1) % count]), Normalize(points[(i + 1) % count] - points[i]), half, stroke);
            }
        }

        return result;
    }

    /// <summary>Adds the area of a join between two segments (their directions are unit vectors), on the outer side of the turn.</summary>
    private static void AddJoin(DisplayPath path, PhysicalOffset vertex, PhysicalOffset incoming, PhysicalOffset outgoing, double half, StrokeStyle stroke)
    {
        var cross = (incoming.X * outgoing.Y) - (incoming.Y * outgoing.X);
        var dot = (incoming.X * outgoing.X) + (incoming.Y * outgoing.Y);
        if (Math.Abs(cross) < 1e-12)
            return;

        // The offset points of the two segments on the outer side of the turn
        var side = cross > 0 ? half : -half;
        var normal1 = new PhysicalOffset(incoming.Y * side, -incoming.X * side);
        var normal2 = new PhysicalOffset(outgoing.Y * side, -outgoing.X * side);
        var a = vertex + normal1;
        var b = vertex + normal2;
        AddPolygon(path, [vertex, a, b]);
        switch (stroke.Join)
        {
            case LineJoin.Miter:
            {
                // The ratio of the miter length to the stroke width is 1 / sin(angle / 2) = 1 / cos(turn / 2)
                var cosHalfSquared = (1 + dot) / 2;
                if (cosHalfSquared > 0 && 1 / Math.Sqrt(cosHalfSquared) <= stroke.MiterLimit)
                {
                    var scale = 1 / (1 + dot);
                    AddPolygon(path, [a, new PhysicalOffset(vertex.X + ((normal1.X + normal2.X) * scale), vertex.Y + ((normal1.Y + normal2.Y) * scale)), b]);
                }

                break;
            }

            case LineJoin.Round:
            {
                var start = Math.Atan2(normal1.Y, normal1.X);
                var sweep = Math.Atan2((normal1.X * normal2.Y) - (normal1.Y * normal2.X), (normal1.X * normal2.X) + (normal1.Y * normal2.Y));
                var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / GetArcStep(half)));
                var fan = new PhysicalOffset[steps + 1];
                for (var i = 0; i <= steps; i++)
                {
                    var angle = start + (sweep * i / steps);
                    fan[i] = new PhysicalOffset(vertex.X + (half * Math.Cos(angle)), vertex.Y + (half * Math.Sin(angle)));
                }

                AddPolygon(path, fan);
                break;
            }
        }
    }

    /// <summary>The angle of the segments of an arc of a radius within the flattening tolerance.</summary>
    private static double GetArcStep(double radius) => radius <= Tolerance ? Math.PI / 2 : 2 * Math.Acos(1 - (Tolerance / radius));

    /// <summary>Adds a closed polygon in the direction of the positive areas (clockwise, the y axis points down).</summary>
    private static void AddPolygon(DisplayPath path, ReadOnlySpan<PhysicalOffset> points)
    {
        var area = 0d;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            area += (a.X * b.Y) - (b.X * a.Y);
        }

        if (Math.Abs(area) < 1e-12)
            return;

        if (area > 0)
        {
            path.MoveTo(points[0].X, points[0].Y);
            for (var i = 1; i < points.Length; i++)
            {
                path.LineTo(points[i].X, points[i].Y);
            }
        }
        else
        {
            path.MoveTo(points[^1].X, points[^1].Y);
            for (var i = points.Length - 2; i >= 0; i--)
            {
                path.LineTo(points[i].X, points[i].Y);
            }
        }

        path.Close();
    }

    private static PhysicalOffset Normalize(PhysicalOffset vector)
    {
        var length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
        return length > 0 ? new PhysicalOffset(vector.X / length, vector.Y / length) : new PhysicalOffset(1, 0);
    }

    /// <summary>
    /// Returns the area of a path moved outwards by a distance (the union of the area and of its stroke of twice the
    /// distance, with the joins of <paramref name="stroke"/>), or inwards for a negative distance (the area without the
    /// stroke). A rounded rectangle (or an ellipse) stays one: its radii grow or shrink by the distance, a square corner
    /// stays square.
    /// </summary>
    public static DisplayPath Offset(DisplayPath path, double distance, StrokeStyle stroke)
    {
        if (distance == 0 || path.IsEmpty)
            return path;

        if (path.RoundedRect is { } rounded)
        {
            // A shape without area has no stroke (a point), or the stroke of a line
            if (rounded.Rect.Width <= 0 && rounded.Rect.Height <= 0)
                return new DisplayPath();

            if (distance < 0 && (rounded.Rect.Width <= -2 * distance || rounded.Rect.Height <= -2 * distance))
                return new DisplayPath();

            return DisplayPath.FromRoundedRect(rounded.Inset(new Sides<double>(-distance, -distance, -distance, -distance)));
        }

        var outline = CreateStrokeOutline(path, stroke with { Width = 2 * Math.Abs(distance) });
        return Combine(path, outline, distance > 0 ? PathOperation.Union : PathOperation.Difference);
    }

    /// <summary>
    /// Returns the result of a boolean operation on the areas of two paths (each with its fill rule), as a path of trapezoids
    /// that do not overlap. The plane is cut in horizontal slabs at the vertices and at the intersections of the edges: in a
    /// slab, the edges do not cross, so the areas are the spans between the edges where the winding numbers are in the result;
    /// the spans between the same two edges in consecutive slabs are one trapezoid.
    /// </summary>
    public static DisplayPath Combine(DisplayPath first, DisplayPath second, PathOperation operation)
    {
        var edges = new List<Edge>();
        AddEdges(edges, first, isSecond: false);
        AddEdges(edges, second, isSecond: true);
        var result = new DisplayPath { FillRule = FillRule.NonZero };
        if (edges.Count == 0)
            return result;

        edges.Sort(static (a, b) => a.Y0.CompareTo(b.Y0));

        // The boundaries of the slabs
        var boundaries = new List<double>((edges.Count * 2) + 16);
        for (var i = 0; i < edges.Count; i++)
        {
            var a = edges[i];
            boundaries.Add(a.Y0);
            boundaries.Add(a.Y1);
            for (var j = i + 1; j < edges.Count && edges[j].Y0 < a.Y1; j++)
            {
                if (GetIntersection(a, edges[j]) is { } y)
                {
                    boundaries.Add(y);
                }
            }
        }

        boundaries.Sort();
        var firstEvenOdd = first.FillRule == FillRule.EvenOdd;
        var secondEvenOdd = second.FillRule == FillRule.EvenOdd;
        var active = new List<int>();
        var order = new List<(double X, int Edge)>();
        var open = new Dictionary<(int Left, int Right), double>();
        var spans = new Dictionary<(int Left, int Right), double>();
        var next = 0;
        var top = boundaries[0];
        for (var b = 1; b < boundaries.Count; b++)
        {
            var bottom = boundaries[b];
            if (bottom - top < Epsilon)
                continue;

            var middle = (top + bottom) / 2;
            while (next < edges.Count && edges[next].Y0 < middle)
            {
                active.Add(next++);
            }

            active.RemoveAll(index => edges[index].Y1 <= middle);
            order.Clear();
            foreach (var index in active)
            {
                order.Add((edges[index].GetX(middle), index));
            }

            order.Sort(static (a, b) => a.X.CompareTo(b.X));
            var (firstWinding, secondWinding, inside, left) = (0, 0, false, -1);
            foreach (var (_, index) in order)
            {
                var edge = edges[index];
                if (edge.IsSecond)
                {
                    secondWinding += edge.Winding;
                }
                else
                {
                    firstWinding += edge.Winding;
                }

                var inFirst = firstEvenOdd ? (firstWinding & 1) != 0 : firstWinding != 0;
                var inSecond = secondEvenOdd ? (secondWinding & 1) != 0 : secondWinding != 0;
                var now = operation switch
                {
                    PathOperation.Union => inFirst || inSecond,
                    PathOperation.Difference => inFirst && !inSecond,
                    _ => inFirst && inSecond,
                };
                if (now && !inside)
                {
                    left = index;
                }
                else if (!now && inside)
                {
                    // The span continues the one of the slab above between the same edges
                    var key = (left, index);
                    spans[key] = open.Remove(key, out var start) ? start : top;
                }

                inside = now;
            }

            foreach (var (key, start) in open)
            {
                AddTrapezoid(result, edges[key.Left], edges[key.Right], start, top);
            }

            (open, spans) = (spans, open);
            spans.Clear();
            top = bottom;
        }

        foreach (var (key, start) in open)
        {
            AddTrapezoid(result, edges[key.Left], edges[key.Right], start, top);
        }

        return result;
    }

    private static void AddTrapezoid(DisplayPath path, Edge left, Edge right, double top, double bottom)
    {
        var (left0, right0, left1, right1) = (left.GetX(top), right.GetX(top), left.GetX(bottom), right.GetX(bottom));
        if (right0 - left0 < Epsilon && right1 - left1 < Epsilon)
            return;

        path.MoveTo(left0, top);
        path.LineTo(right0, top);
        path.LineTo(right1, bottom);
        path.LineTo(left1, bottom);
        path.Close();
    }

    /// <summary>Returns the y coordinate of the point where two edges cross (not at their ends), or null.</summary>
    private static double? GetIntersection(Edge a, Edge b)
    {
        if (Math.Max(a.X0, a.X1) < Math.Min(b.X0, b.X1) || Math.Max(b.X0, b.X1) < Math.Min(a.X0, a.X1))
            return null;

        var (ax, ay, bx, by) = (a.X1 - a.X0, a.Y1 - a.Y0, b.X1 - b.X0, b.Y1 - b.Y0);
        var denominator = (ax * by) - (ay * bx);
        if (Math.Abs(denominator) < 1e-12)
            return null;

        var (dx, dy) = (b.X0 - a.X0, b.Y0 - a.Y0);
        var t = ((dx * by) - (dy * bx)) / denominator;
        var u = ((dx * ay) - (dy * ax)) / denominator;
        return t > 0 && t < 1 && u > 0 && u < 1 ? a.Y0 + (t * ay) : null;
    }

    /// <summary>Adds the edges of the subpaths of a path (closed to fill them), without the horizontal ones.</summary>
    private static void AddEdges(List<Edge> edges, DisplayPath path, bool isSecond)
    {
        foreach (var contour in Flatten(path))
        {
            var points = contour.Points;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                if (a.Y < b.Y)
                {
                    edges.Add(new Edge(a.X, a.Y, b.X, b.Y, 1, isSecond));
                }
                else if (a.Y > b.Y)
                {
                    edges.Add(new Edge(b.X, b.Y, a.X, a.Y, -1, isSecond));
                }
            }
        }
    }

    /// <summary>Whether a point is in the area of a path (with its fill rule).</summary>
    public static bool Contains(DisplayPath path, PhysicalOffset point)
    {
        if (path.IsEmpty)
            return false;

        if (path.RoundedRect is { Contour: null } rounded)
        {
            var rect = rounded.Rect;
            if (point.X < rect.Left || point.Y < rect.Top || point.X > rect.Right || point.Y > rect.Bottom)
                return false;

            return IsInCorner(point.X - rect.Left, point.Y - rect.Top, rounded.Radii.TopLeft)
                && IsInCorner(rect.Right - point.X, point.Y - rect.Top, rounded.Radii.TopRight)
                && IsInCorner(rect.Right - point.X, rect.Bottom - point.Y, rounded.Radii.BottomRight)
                && IsInCorner(point.X - rect.Left, rect.Bottom - point.Y, rounded.Radii.BottomLeft);
        }

        var winding = 0;
        foreach (var contour in Flatten(path))
        {
            var points = contour.Points;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                if (a.Y <= point.Y)
                {
                    if (b.Y > point.Y && Cross(a, b, point) > 0)
                    {
                        winding++;
                    }
                }
                else if (b.Y <= point.Y && Cross(a, b, point) < 0)
                {
                    winding--;
                }
            }
        }

        return path.FillRule == FillRule.EvenOdd ? (winding & 1) != 0 : winding != 0;

        static double Cross(PhysicalOffset a, PhysicalOffset b, PhysicalOffset p) => ((b.X - a.X) * (p.Y - a.Y)) - ((p.X - a.X) * (b.Y - a.Y));

        // Whether a point at the given distances from the two sides of a corner is inside the ellipse of the corner
        static bool IsInCorner(double x, double y, (double X, double Y) radius)
        {
            if (radius.X <= 0 || radius.Y <= 0 || x >= radius.X || y >= radius.Y)
                return true;

            var dx = (radius.X - x) / radius.X;
            var dy = (radius.Y - y) / radius.Y;
            return (dx * dx) + (dy * dy) <= 1;
        }
    }

    /// <summary>Returns the path moved by an offset (a rounded rectangle stays one).</summary>
    public static DisplayPath Translate(DisplayPath path, PhysicalOffset offset)
    {
        if (offset == default || path.IsEmpty)
            return path;

        if (path.RoundedRect is { } rounded)
            return DisplayPath.FromRoundedRect(rounded.Translate(offset));

        return BasicShapePaths.Transform(path, new Svg.SvgAffine(1, 0, 0, 1, offset.X, offset.Y));
    }

    /// <summary>Whether a path has only straight segments.</summary>
    public static bool HasOnlyLines(DisplayPath path)
    {
        foreach (var verb in path.Verbs)
        {
            if (verb is PathVerb.QuadTo or PathVerb.CubicTo)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the subpaths of a path with their curves flattened: the number of segments of a curve is the one that keeps
    /// them within <see cref="Tolerance"/> of the curve (Wang's formula). The consecutive points that are equal are removed.
    /// </summary>
    private static List<Contour> Flatten(DisplayPath path)
    {
        var contours = new List<Contour>();
        var contour = new List<PhysicalOffset>();
        var points = path.Points;
        var index = 0;
        var start = default(PhysicalOffset);
        var current = default(PhysicalOffset);
        foreach (var verb in path.Verbs)
        {
            switch (verb)
            {
                case PathVerb.MoveTo:
                    EndContour(isClosed: false);
                    start = current = points[index++];
                    contour.Add(current);
                    break;

                case PathVerb.LineTo:
                    current = points[index++];
                    Add(current);
                    break;

                case PathVerb.QuadTo:
                {
                    var c = points[index++];
                    var end = points[index++];
                    var steps = GetSteps(Length(current.X - (2 * c.X) + end.X, current.Y - (2 * c.Y) + end.Y) / 4);
                    var from = current;
                    for (var i = 1; i <= steps; i++)
                    {
                        var t = i / (double)steps;
                        var u = 1 - t;
                        Add(new PhysicalOffset((u * u * from.X) + (2 * u * t * c.X) + (t * t * end.X), (u * u * from.Y) + (2 * u * t * c.Y) + (t * t * end.Y)));
                    }

                    current = end;
                    break;
                }

                case PathVerb.CubicTo:
                {
                    var c1 = points[index++];
                    var c2 = points[index++];
                    var end = points[index++];
                    var deviation = Math.Max(Length(current.X - (2 * c1.X) + c2.X, current.Y - (2 * c1.Y) + c2.Y), Length(c1.X - (2 * c2.X) + end.X, c1.Y - (2 * c2.Y) + end.Y));
                    var steps = GetSteps(deviation * 3 / 4);
                    var from = current;
                    for (var i = 1; i <= steps; i++)
                    {
                        var t = i / (double)steps;
                        var u = 1 - t;
                        Add(new PhysicalOffset(
                            (u * u * u * from.X) + (3 * u * u * t * c1.X) + (3 * u * t * t * c2.X) + (t * t * t * end.X),
                            (u * u * u * from.Y) + (3 * u * u * t * c1.Y) + (3 * u * t * t * c2.Y) + (t * t * t * end.Y)));
                    }

                    current = end;
                    break;
                }

                default:
                    // Close: the next segment starts at the start of the subpath
                    EndContour(isClosed: true);
                    current = start;
                    break;
            }
        }

        EndContour(isClosed: false);
        return contours;

        static double Length(double x, double y) => Math.Sqrt((x * x) + (y * y));

        static int GetSteps(double value) => (int)Math.Clamp(Math.Ceiling(Math.Sqrt(value / Tolerance)), 1, 512);

        void Add(PhysicalOffset point)
        {
            // A segment drawn after a close starts a new subpath at the start of the closed one
            if (contour.Count == 0)
            {
                contour.Add(start);
            }

            if (contour[^1] != point)
            {
                contour.Add(point);
            }
        }

        void EndContour(bool isClosed)
        {
            if (isClosed && contour.Count > 1 && contour[^1] == contour[0])
            {
                contour.RemoveAt(contour.Count - 1);
            }

            if (contour.Count > 0)
            {
                contours.Add(new Contour([.. contour], isClosed));
                contour.Clear();
            }
        }
    }
}
