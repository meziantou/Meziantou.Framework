namespace Meziantou.Framework.Imaging.TestHarness.Drawing;

/// <summary>
/// The region covered by the stroke of a polyline, as a point-membership predicate written from the definition of a
/// stroke: no outline, no piece emitted in a particular order, no orientation.
/// </summary>
/// <remarks>
/// <para>
/// The region is the union of convex sets, each described by inequalities: for a segment, the points within half the
/// width of its line and between the perpendiculars at its ends; for a vertex, on the outer side of the turn, the
/// triangle between the vertex and the two outer corners (bevel), the same completed up to the intersection of the two
/// outer edges (miter, when the miter length does not exceed the limit), or the points of the disk centered on the
/// vertex that are beyond the end of the first segment and before the start of the second (round); for the end of an
/// open polyline, nothing (butt), a square extension (square) or the half disk beyond the end (round).
/// </para>
/// <para>
/// <see cref="GetSignedDistance"/> returns a lower bound of the distance to the boundary of the region, positive
/// inside: a point whose bound exceeds a margin is inside (or outside) whatever the approximation of that size made by
/// the code under test.
/// </para>
/// </remarks>
public sealed class ReferenceStroke
{
    private readonly List<ConvexSet> _sets = [];

    /// <summary>Initializes a new instance of the <see cref="ReferenceStroke"/> class.</summary>
    /// <param name="points">The vertices of the polyline.</param>
    /// <param name="closed">Whether the last vertex is joined to the first.</param>
    /// <param name="width">The width of the stroke.</param>
    /// <param name="join">The join of the vertices that are not smooth.</param>
    /// <param name="cap">The cap of the ends of an open polyline.</param>
    /// <param name="miterLimit">The largest ratio of the miter length to the width.</param>
    /// <param name="smooth">For each vertex, whether it is always joined with a round join (a vertex created by flattening a curve); <see langword="null"/> for none.</param>
    public ReferenceStroke(IReadOnlyList<(double X, double Y)> points, bool closed, double width, ReferenceLineJoin join, ReferenceLineCap cap, double miterLimit, IReadOnlyList<bool>? smooth = null)
    {
        ArgumentNullException.ThrowIfNull(points);
        var h = width / 2;

        // Consecutive equal points are one vertex (smooth only if all of them are)
        var vertices = new List<(double X, double Y, bool Smooth)>();
        for (var i = 0; i < points.Count; i++)
        {
            var isSmooth = smooth is not null && smooth[i];
            if (vertices.Count > 0 && vertices[^1].X == points[i].X && vertices[^1].Y == points[i].Y)
            {
                vertices[^1] = (vertices[^1].X, vertices[^1].Y, vertices[^1].Smooth && isSmooth);
                continue;
            }

            vertices.Add((points[i].X, points[i].Y, isSmooth));
        }

        if (closed && vertices.Count > 1 && vertices[^1].X == vertices[0].X && vertices[^1].Y == vertices[0].Y)
        {
            vertices[0] = (vertices[0].X, vertices[0].Y, vertices[0].Smooth && vertices[^1].Smooth);
            vertices.RemoveAt(vertices.Count - 1);
        }

        if (vertices.Count == 0)
            return;

        if (vertices.Count == 1)
        {
            // A polyline reduced to a point
            var (x, y, _) = vertices[0];
            if (cap == ReferenceLineCap.Round)
            {
                _sets.Add(new ConvexSet { Disk = (x, y, h) });
            }
            else if (cap == ReferenceLineCap.Square)
            {
                _sets.Add(ConvexSet.FromPolygon([(x - h, y - h), (x + h, y - h), (x + h, y + h), (x - h, y + h)]));
            }

            return;
        }

        var segmentCount = closed ? vertices.Count : vertices.Count - 1;
        var directions = new (double X, double Y)[segmentCount];
        for (var i = 0; i < segmentCount; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertices.Count];
            var length = Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
            var d = ((b.X - a.X) / length, (b.Y - a.Y) / length);
            directions[i] = d;
            _sets.Add(Body((a.X, a.Y), (b.X, b.Y), d, h));
        }

        for (var i = 0; i < segmentCount; i++)
        {
            // The vertex at the end of segment i
            var next = (i + 1) % segmentCount;
            if (!closed && i == segmentCount - 1)
                break;

            var vertex = vertices[(i + 1) % vertices.Count];
            AddJoin((vertex.X, vertex.Y), directions[i], directions[next], h, vertex.Smooth ? ReferenceLineJoin.Round : join, miterLimit);
        }

        if (!closed)
        {
            AddCap((vertices[0].X, vertices[0].Y), (-directions[0].X, -directions[0].Y), h, cap);
            AddCap((vertices[^1].X, vertices[^1].Y), directions[^1], h, cap);
        }
    }

    /// <summary>Gets a lower bound of the distance from a point to the boundary of the stroke: positive when the point is inside, negative when it is outside.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <returns>The signed bound (negative infinity when the stroke is empty).</returns>
    public double GetSignedDistance(double x, double y)
    {
        var best = double.NegativeInfinity;
        foreach (var set in _sets)
        {
            best = Math.Max(best, set.GetSlack(x, y));
        }

        return best;
    }

    private static ConvexSet Body((double X, double Y) a, (double X, double Y) b, (double X, double Y) d, double h)
    {
        var set = new ConvexSet();
        (double X, double Y) n = (-d.Y, d.X);
        set.AddHalfPlane((-d.X, -d.Y), a); // not before the start
        set.AddHalfPlane(d, b); // not after the end
        set.AddHalfPlane(n, (a.X + (h * n.X), a.Y + (h * n.Y)));
        set.AddHalfPlane((-n.X, -n.Y), (a.X - (h * n.X), a.Y - (h * n.Y)));
        return set;
    }

    private void AddJoin((double X, double Y) v, (double X, double Y) d0, (double X, double Y) d1, double h, ReferenceLineJoin join, double miterLimit)
    {
        if (join == ReferenceLineJoin.Round)
        {
            // Beyond the end of the first segment and before the start of the second, within the disk. For a straight
            // continuation this is a half line; for a path that turns back it is a half disk.
            var set = new ConvexSet { Disk = (v.X, v.Y, h) };
            set.AddHalfPlane((-d0.X, -d0.Y), v);
            set.AddHalfPlane(d1, v);
            _sets.Add(set);
            return;
        }

        var cross = (d0.X * d1.Y) - (d0.Y * d1.X);
        if (Math.Abs(cross) < 1e-12)
            return;

        // The normal of each segment that points away from the other segment
        (double X, double Y) a = (-d0.Y, d0.X);
        if ((a.X * d1.X) + (a.Y * d1.Y) > 0)
        {
            a = (-a.X, -a.Y);
        }

        (double X, double Y) b = (-d1.Y, d1.X);
        if ((b.X * d0.X) + (b.Y * d0.Y) < 0)
        {
            b = (-b.X, -b.Y);
        }

        (double X, double Y) cornerA = (v.X + (h * a.X), v.Y + (h * a.Y));
        (double X, double Y) cornerB = (v.X + (h * b.X), v.Y + (h * b.Y));
        if (join == ReferenceLineJoin.Miter)
        {
            // The tip is where the outer edges meet: cornerA + s d0 = cornerB - t d1
            var determinant = (d0.X * d1.Y) - (d0.Y * d1.X);
            var s = (((cornerB.X - cornerA.X) * d1.Y) - ((cornerB.Y - cornerA.Y) * d1.X)) / determinant;
            (double X, double Y) tip = (cornerA.X + (s * d0.X), cornerA.Y + (s * d0.Y));

            // The miter length runs from the inner corner to the tip: twice the distance from the vertex to the tip
            var ratio = Math.Sqrt(((tip.X - v.X) * (tip.X - v.X)) + ((tip.Y - v.Y) * (tip.Y - v.Y))) / h;
            if (ratio <= miterLimit)
            {
                _sets.Add(ConvexSet.FromPolygon([v, cornerA, tip, cornerB]));
                return;
            }
        }

        _sets.Add(ConvexSet.FromPolygon([v, cornerA, cornerB]));
    }

    private void AddCap((double X, double Y) end, (double X, double Y) direction, double h, ReferenceLineCap cap)
    {
        if (cap == ReferenceLineCap.Square)
        {
            _sets.Add(Body(end, (end.X + (h * direction.X), end.Y + (h * direction.Y)), direction, h));
        }
        else if (cap == ReferenceLineCap.Round)
        {
            var set = new ConvexSet { Disk = (end.X, end.Y, h) };
            set.AddHalfPlane((-direction.X, -direction.Y), end);
            _sets.Add(set);
        }
    }

    /// <summary>An intersection of half planes and, optionally, of a disk.</summary>
    private sealed class ConvexSet
    {
        private readonly List<(double NormalX, double NormalY, double Offset)> _halfPlanes = [];

        public (double X, double Y, double Radius)? Disk { get; init; }

        public static ConvexSet FromPolygon((double X, double Y)[] points)
        {
            double area = 0;
            for (var i = 0; i < points.Length; i++)
            {
                var p = points[i];
                var q = points[(i + 1) % points.Length];
                area += (p.X * q.Y) - (q.X * p.Y);
            }

            var set = new ConvexSet();
            for (var i = 0; i < points.Length; i++)
            {
                var p = points[i];
                var q = points[(i + 1) % points.Length];
                var dx = q.X - p.X;
                var dy = q.Y - p.Y;
                var length = Math.Sqrt((dx * dx) + (dy * dy));
                if (length == 0)
                    continue;

                // The outward normal of the side: to the right of the direction of travel for a positive area
                var sign = area > 0 ? 1 : -1;
                set.AddHalfPlane((sign * dy / length, -sign * dx / length), p);
            }

            return set;
        }

        /// <summary>Keeps the points that are not beyond a point in the direction of a unit normal.</summary>
        public void AddHalfPlane((double X, double Y) normal, (double X, double Y) point)
            => _halfPlanes.Add((normal.X, normal.Y, (normal.X * point.X) + (normal.Y * point.Y)));

        /// <summary>Gets the smallest slack of the inequalities: the distance to the boundary inside, at most the distance to the set (negated) outside.</summary>
        public double GetSlack(double x, double y)
        {
            var slack = double.PositiveInfinity;
            if (Disk is { } disk)
            {
                slack = disk.Radius - Math.Sqrt(((x - disk.X) * (x - disk.X)) + ((y - disk.Y) * (y - disk.Y)));
            }

            foreach (var (normalX, normalY, offset) in _halfPlanes)
            {
                slack = Math.Min(slack, offset - ((normalX * x) + (normalY * y)));
            }

            return slack;
        }
    }
}
