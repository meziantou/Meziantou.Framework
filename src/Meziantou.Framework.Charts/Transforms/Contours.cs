namespace Meziantou.Framework.Charts;

/// <summary>
/// The marching squares of d3-contour (ISC license): the polygons of the cells of a grid of values whose values are at
/// least a threshold. The value of the cell (x, y) is <c>values[y * width + x]</c>, and the cell covers the square from
/// (x, y) to (x + 1, y + 1): the coordinates of the polygons are in the unit of the cells. The edges are interpolated
/// linearly between the centers of the cells.
/// </summary>
internal static class Contours
{
    // The segments of the 16 cases, from the corners above the threshold: 1 bottom left, 2 bottom right, 4 top right, 8 top left
    private static readonly double[][][] Cases =
    [
        [],
        [[1.0, 1.5, 0.5, 1.0]],
        [[1.5, 1.0, 1.0, 1.5]],
        [[1.5, 1.0, 0.5, 1.0]],
        [[1.0, 0.5, 1.5, 1.0]],
        [[1.0, 1.5, 0.5, 1.0], [1.0, 0.5, 1.5, 1.0]],
        [[1.0, 0.5, 1.0, 1.5]],
        [[1.0, 0.5, 0.5, 1.0]],
        [[0.5, 1.0, 1.0, 0.5]],
        [[1.0, 1.5, 1.0, 0.5]],
        [[0.5, 1.0, 1.0, 0.5], [1.5, 1.0, 1.0, 1.5]],
        [[1.5, 1.0, 1.0, 0.5]],
        [[0.5, 1.0, 1.5, 1.0]],
        [[1.0, 1.5, 1.5, 1.0]],
        [[0.5, 1.0, 1.0, 1.5]],
        [],
    ];

    /// <summary>The polygons of the cells whose values are at least <paramref name="threshold"/>: each polygon is an exterior ring, then its holes.</summary>
    public static List<List<List<(double X, double Y)>>> Compute(double[] values, int width, int height, double threshold)
    {
        var polygons = new List<List<List<(double X, double Y)>>>();
        var holes = new List<List<(double X, double Y)>>();
        IsoRings(values, width, height, threshold, ring =>
        {
            Smooth(ring, values, width, height, threshold);
            if (Area(ring) > 0)
            {
                polygons.Add([ring]);
            }
            else
            {
                holes.Add(ring);
            }
        });

        foreach (var hole in holes)
        {
            foreach (var polygon in polygons)
            {
                if (Contains(polygon[0], hole) is not -1)
                {
                    polygon.Add(hole);
                    break;
                }
            }
        }

        return polygons;
    }

    /// <summary>Twice the signed area of a ring, as d3: positive for the exterior rings.</summary>
    internal static double Area(List<(double X, double Y)> ring)
    {
        var n = ring.Count;
        var area = (ring[n - 1].Y * ring[0].X) - (ring[n - 1].X * ring[0].Y);
        for (var i = 1; i < n; i++)
        {
            area += (ring[i - 1].Y * ring[i].X) - (ring[i - 1].X * ring[i].Y);
        }

        return area;
    }

    private static void IsoRings(double[] values, int dx, int dy, double value, Action<List<(double X, double Y)>> callback)
    {
        var fragmentByStart = new Dictionary<int, Fragment>();
        var fragmentByEnd = new Dictionary<int, Fragment>();
        int t0, t1, t2, t3;

        // The first row (y = -1): the cells above the grid are below the threshold
        var x = -1;
        var y = -1;
        t1 = Above(values[0], value);
        Stitch(t1 << 1);
        while (++x < dx - 1)
        {
            t0 = t1;
            t1 = Above(values[x + 1], value);
            Stitch(t0 | (t1 << 1));
        }

        Stitch(t1);

        // The rows in between
        while (++y < dy - 1)
        {
            x = -1;
            t1 = Above(values[(y * dx) + dx], value);
            t2 = Above(values[y * dx], value);
            Stitch((t1 << 1) | (t2 << 2));
            while (++x < dx - 1)
            {
                t0 = t1;
                t1 = Above(values[(y * dx) + dx + x + 1], value);
                t3 = t2;
                t2 = Above(values[(y * dx) + x + 1], value);
                Stitch(t0 | (t1 << 1) | (t2 << 2) | (t3 << 3));
            }

            Stitch(t1 | (t2 << 3));
        }

        // The last row (y = dy - 1): the cells under the grid are below the threshold
        x = -1;
        t2 = Above(values[y * dx], value);
        Stitch(t2 << 2);
        while (++x < dx - 1)
        {
            t3 = t2;
            t2 = Above(values[(y * dx) + x + 1], value);
            Stitch((t2 << 2) | (t3 << 3));
        }

        Stitch(t2 << 3);

        void Stitch(int caseIndex)
        {
            foreach (var line in Cases[caseIndex])
            {
                var start = (line[0] + x, line[1] + y);
                var end = (line[2] + x, line[3] + y);
                var startIndex = Index(start);
                var endIndex = Index(end);
                if (fragmentByEnd.TryGetValue(startIndex, out var f))
                {
                    if (fragmentByStart.TryGetValue(endIndex, out var g))
                    {
                        fragmentByEnd.Remove(f.End);
                        fragmentByStart.Remove(g.Start);
                        if (f == g)
                        {
                            f.Ring.Add(end);
                            callback(f.Ring);
                        }
                        else
                        {
                            var joined = new Fragment(f.Start, g.End, [.. f.Ring, .. g.Ring]);
                            fragmentByStart[f.Start] = joined;
                            fragmentByEnd[g.End] = joined;
                        }
                    }
                    else
                    {
                        fragmentByEnd.Remove(f.End);
                        f.Ring.Add(end);
                        f.End = endIndex;
                        fragmentByEnd[endIndex] = f;
                    }
                }
                else if (fragmentByStart.TryGetValue(endIndex, out f))
                {
                    if (fragmentByEnd.TryGetValue(startIndex, out var g))
                    {
                        fragmentByStart.Remove(f.Start);
                        fragmentByEnd.Remove(g.End);
                        if (f == g)
                        {
                            f.Ring.Add(end);
                            callback(f.Ring);
                        }
                        else
                        {
                            var joined = new Fragment(g.Start, f.End, [.. g.Ring, .. f.Ring]);
                            fragmentByStart[g.Start] = joined;
                            fragmentByEnd[f.End] = joined;
                        }
                    }
                    else
                    {
                        fragmentByStart.Remove(f.Start);
                        f.Ring.Insert(0, start);
                        f.Start = startIndex;
                        fragmentByStart[startIndex] = f;
                    }
                }
                else
                {
                    var fragment = new Fragment(startIndex, endIndex, [start, end]);
                    fragmentByStart[startIndex] = fragment;
                    fragmentByEnd[endIndex] = fragment;
                }
            }
        }

        int Index((double X, double Y) point) => (int)((point.X * 2) + (point.Y * (dx + 1) * 4));
    }

    private static int Above(double x, double value) => x >= value ? 1 : 0;

    private static void Smooth(List<(double X, double Y)> ring, double[] values, int dx, int dy, double value)
    {
        for (var i = 0; i < ring.Count; i++)
        {
            var (x, y) = ring[i];
            var xt = (int)x;
            var yt = (int)y;
            var v1 = Valid(Get(values, (yt * dx) + xt));
            if (x > 0 && x < dx && xt == x)
            {
                x = Smooth1(x, Valid(Get(values, (yt * dx) + xt - 1)), v1, value);
            }

            if (y > 0 && y < dy && yt == y)
            {
                y = Smooth1(y, Valid(Get(values, ((yt - 1) * dx) + xt)), v1, value);
            }

            ring[i] = (x, y);
        }
    }

    // The points on the last edges of the grid read past its end, as d3 reads undefined
    private static double Get(double[] values, int index) => index >= 0 && index < values.Length ? values[index] : double.NaN;

    private static double Valid(double value) => double.IsNaN(value) ? double.NegativeInfinity : value;

    private static double Smooth1(double x, double v0, double v1, double value)
    {
        var a = value - v0;
        var b = v1 - v0;
        var d = double.IsFinite(a) || double.IsFinite(b) ? a / b : Math.Sign(a) / (double)Math.Sign(b);
        return double.IsNaN(d) ? x : x + d - 0.5;
    }

    /// <summary>-1 when the hole is outside the ring, 1 inside, 0 on its edge: the position of its first point that is not on the edge.</summary>
    private static int Contains(List<(double X, double Y)> ring, List<(double X, double Y)> hole)
    {
        foreach (var point in hole)
        {
            var c = RingContains(ring, point);
            if (c is not 0)
                return c;
        }

        return 0;
    }

    private static int RingContains(List<(double X, double Y)> ring, (double X, double Y) point)
    {
        var (x, y) = point;
        var contains = -1;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var (xi, yi) = ring[i];
            var (xj, yj) = ring[j];
            if (SegmentContains(ring[i], ring[j], point))
                return 0;

            if (yi > y != yj > y && x < ((xj - xi) * (y - yi) / (yj - yi)) + xi)
            {
                contains = -contains;
            }
        }

        return contains;
    }

    private static bool SegmentContains((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
    {
        if ((b.X - a.X) * (c.Y - a.Y) != (c.X - a.X) * (b.Y - a.Y))
            return false;

        return a.X == b.X ? Within(a.Y, c.Y, b.Y) : Within(a.X, c.X, b.X);
    }

    private static bool Within(double p, double q, double r) => (p <= q && q <= r) || (r <= q && q <= p);

    private sealed class Fragment(int start, int end, List<(double X, double Y)> ring)
    {
        public int Start { get; set; } = start;

        public int End { get; set; } = end;

        public List<(double X, double Y)> Ring { get; } = ring;
    }
}
