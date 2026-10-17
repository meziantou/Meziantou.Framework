namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The ordering of the planes of a 3D rendering context (https://drafts.csswg.org/css-transforms-2/#3d-rendering-contexts: "the
/// final value of a pixel is determined by the depth of the planes at that pixel"): as the compositors of the browsers, the
/// polygons of the planes are sorted with a binary space partitioning tree, and the polygons that intersect a plane are split
/// along it, so that they are painted from the farthest to the nearest (the viewer looks along the negative z axis). The
/// polygons in the same plane keep their painting order.
/// </summary>
internal static class PlaneSorting
{
    private const double Epsilon = 1e-6;

    /// <summary>A convex polygon of a plane: its vertices after the transform of the context (x, y, z) and in the coordinates of the plane (local x, y).</summary>
    internal sealed class Polygon(int plane, List<(double X, double Y, double Z)> points, List<PhysicalOffset> local, bool isSplit)
    {
        /// <summary>The index of the plane.</summary>
        public int Plane { get; } = plane;

        public List<(double X, double Y, double Z)> Points { get; } = points;

        /// <summary>The vertices in the coordinates of the plane (the clip of a split polygon).</summary>
        public List<PhysicalOffset> Local { get; } = local;

        /// <summary>Whether the polygon is a part of the polygon of its plane.</summary>
        public bool IsSplit { get; } = isSplit;
    }

    /// <summary>
    /// Returns the polygon of a rectangle of a plane mapped by a 4x4 matrix, or null when a vertex is behind the viewer (w ≤ 0).
    /// </summary>
    public static Polygon? CreatePolygon(int plane, PhysicalRect rect, ReadOnlySpan<double> m)
    {
        var points = new List<(double X, double Y, double Z)>(4);
        var local = new List<PhysicalOffset>(4) { new(rect.Left, rect.Top), new(rect.Right, rect.Top), new(rect.Right, rect.Bottom), new(rect.Left, rect.Bottom) };
        foreach (var point in local)
        {
            var x = (m[0] * point.X) + (m[4] * point.Y) + m[12];
            var y = (m[1] * point.X) + (m[5] * point.Y) + m[13];
            var z = (m[2] * point.X) + (m[6] * point.Y) + m[14];
            var w = (m[3] * point.X) + (m[7] * point.Y) + m[15];
            if (w <= Epsilon)
                return null;

            points.Add((x / w, y / w, z / w));
        }

        return new Polygon(plane, points, local, isSplit: false);
    }

    /// <summary>
    /// Returns the polygons in painting order (from the farthest). <paramref name="toLocal"/> maps a point of the context to the
    /// coordinates of a plane (the vertices of the split polygons).
    /// </summary>
    public static List<Polygon> Sort(List<Polygon> polygons, Func<int, double, double, PhysicalOffset?> toLocal)
    {
        var result = new List<Polygon>(polygons.Count);
        Sort(polygons, toLocal, result, depth: 0);
        return result;
    }

    private static void Sort(List<Polygon> polygons, Func<int, double, double, PhysicalOffset?> toLocal, List<Polygon> output, int depth)
    {
        if (polygons.Count == 0)
            return;

        // The splitter is the first polygon that is not degenerate (a polygon seen edge-on is invisible: it keeps its order)
        var splitterIndex = polygons.FindIndex(polygon => GetPlane(polygon) is not null);
        if (splitterIndex < 0 || depth > 64)
        {
            output.AddRange(polygons);
            return;
        }

        var (normal, d) = GetPlane(polygons[splitterIndex])!.Value;
        var front = new List<Polygon>();
        var back = new List<Polygon>();
        var coplanar = new List<Polygon>();
        foreach (var polygon in polygons)
        {
            var hasFront = false;
            var hasBack = false;
            foreach (var point in polygon.Points)
            {
                var distance = Distance(normal, d, point);
                hasFront |= distance > Epsilon;
                hasBack |= distance < -Epsilon;
            }

            if (hasFront && hasBack)
            {
                Split(polygon, normal, d, toLocal, front, back);
            }
            else if (hasFront)
            {
                front.Add(polygon);
            }
            else if (hasBack)
            {
                back.Add(polygon);
            }
            else
            {
                coplanar.Add(polygon);
            }
        }

        // The viewer is on the side of positive z: the half-space it is in is painted last
        var viewerInFront = normal.Z >= 0;
        Sort(viewerInFront ? back : front, toLocal, output, depth + 1);
        output.AddRange(coplanar);
        Sort(viewerInFront ? front : back, toLocal, output, depth + 1);
    }

    private static double Distance((double X, double Y, double Z) normal, double d, (double X, double Y, double Z) point) => (normal.X * point.X) + (normal.Y * point.Y) + (normal.Z * point.Z) + d;

    /// <summary>Returns the plane of a polygon (its unit normal and its distance to the origin), or null when the polygon is degenerate.</summary>
    private static ((double X, double Y, double Z) Normal, double D)? GetPlane(Polygon polygon)
    {
        var points = polygon.Points;
        if (points.Count < 3)
            return null;

        // Newell's method: robust for the polygons whose first vertices are aligned
        double nx = 0, ny = 0, nz = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            nx += (a.Y - b.Y) * (a.Z + b.Z);
            ny += (a.Z - b.Z) * (a.X + b.X);
            nz += (a.X - b.X) * (a.Y + b.Y);
        }

        var length = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        if (length < Epsilon)
            return null;

        var normal = (nx / length, ny / length, nz / length);
        var p = points[0];
        return (normal, -((normal.Item1 * p.X) + (normal.Item2 * p.Y) + (normal.Item3 * p.Z)));
    }

    /// <summary>Splits a polygon along a plane (Sutherland-Hodgman); the new vertices are mapped to the coordinates of the plane of the polygon.</summary>
    private static void Split(Polygon polygon, (double X, double Y, double Z) normal, double d, Func<int, double, double, PhysicalOffset?> toLocal, List<Polygon> front, List<Polygon> back)
    {
        var frontPoints = new List<(double X, double Y, double Z)>();
        var frontLocal = new List<PhysicalOffset>();
        var backPoints = new List<(double X, double Y, double Z)>();
        var backLocal = new List<PhysicalOffset>();
        var points = polygon.Points;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            var localA = polygon.Local[i];
            var localB = polygon.Local[(i + 1) % points.Count];
            var da = Distance(normal, d, a);
            var db = Distance(normal, d, b);
            if (da >= -Epsilon)
            {
                frontPoints.Add(a);
                frontLocal.Add(localA);
            }

            if (da <= Epsilon)
            {
                backPoints.Add(a);
                backLocal.Add(localA);
            }

            if ((da > Epsilon && db < -Epsilon) || (da < -Epsilon && db > Epsilon))
            {
                var t = da / (da - db);
                var point = (a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t), a.Z + ((b.Z - a.Z) * t));

                // The point of the edge in the coordinates of the plane (the transform may be projective: not a linear interpolation)
                var local = toLocal(polygon.Plane, point.Item1, point.Item2) ?? new PhysicalOffset(localA.X + ((localB.X - localA.X) * t), localA.Y + ((localB.Y - localA.Y) * t));
                frontPoints.Add(point);
                frontLocal.Add(local);
                backPoints.Add(point);
                backLocal.Add(local);
            }
        }

        if (frontPoints.Count >= 3)
        {
            front.Add(new Polygon(polygon.Plane, frontPoints, frontLocal, isSplit: true));
        }

        if (backPoints.Count >= 3)
        {
            back.Add(new Polygon(polygon.Plane, backPoints, backLocal, isSplit: true));
        }
    }
}
