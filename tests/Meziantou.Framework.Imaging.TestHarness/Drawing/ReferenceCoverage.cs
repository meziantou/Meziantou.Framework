using System.Numerics;
using Meziantou.Framework.Imaging.TestHarness.Resampling;

namespace Meziantou.Framework.Imaging.TestHarness.Drawing;

/// <summary>
/// The exact area of each pixel that is inside a set of closed contours, computed with rational numbers by brute force:
/// no sweep state is carried from one row to the next, no accumulation row, no clipping of the contours.
/// </summary>
/// <remarks>
/// <para>
/// A point is inside when its winding number is not zero, or is odd for the even-odd rule. A row of pixels is cut into
/// horizontal slabs at every height where something happens: the ends of the segments, the intersections of every pair
/// of segments, and the heights where a segment crosses a column boundary. Inside a slab, the segments are disjoint,
/// each stays within one column, and their left-to-right order is the order of their midpoints: the area between two
/// consecutive segments is a trapezoid whose intersection with each column is exact.
/// </para>
/// <para>
/// The double coordinates are converted exactly (every finite double is a rational number), so the result is the exact
/// area for the coordinates given, whatever their magnitude.
/// </para>
/// </remarks>
public static class ReferenceCoverage
{
    /// <summary>The number of coverage steps of a fully covered pixel in the library contract.</summary>
    public const int FullCoverage = 65536;

    /// <summary>
    /// The distance to a rounding boundary, in coverage steps, under which either neighbor is accepted. The library
    /// rounds areas computed in double precision to the nearest step, with a bias of 1/256 step so that exact ties round
    /// upward; a wrong edge, winding or clipping changes coverages by orders of magnitude more.
    /// </summary>
    public static Fraction TieWindow { get; } = new(1, 128);

    /// <summary>Converts closed polygons to directed segments (each polygon is closed by a segment from its last point to its first).</summary>
    /// <param name="polygons">The polygons, as lists of points.</param>
    /// <returns>The segments.</returns>
    public static IReadOnlyList<ReferenceEdge> FromPolygons(IEnumerable<IReadOnlyList<(double X, double Y)>> polygons)
    {
        ArgumentNullException.ThrowIfNull(polygons);
        var edges = new List<ReferenceEdge>();
        foreach (var polygon in polygons)
        {
            for (var i = 0; i < polygon.Count; i++)
            {
                var from = polygon[i];
                var to = polygon[(i + 1) % polygon.Count];
                edges.Add(new ReferenceEdge(from.X, from.Y, to.X, to.Y));
            }
        }

        return edges;
    }

    /// <summary>Computes the exact covered area of every pixel.</summary>
    /// <param name="edges">The directed segments of closed contours.</param>
    /// <param name="width">The width of the canvas.</param>
    /// <param name="height">The height of the canvas.</param>
    /// <param name="evenOdd"><see langword="true"/> for the even-odd rule, <see langword="false"/> for the non-zero rule.</param>
    /// <returns>The areas, row by row (<c>width * height</c> values between 0 and 1).</returns>
    public static Fraction[] Compute(IReadOnlyList<ReferenceEdge> edges, int width, int height, bool evenOdd)
    {
        ArgumentNullException.ThrowIfNull(edges);
        var segments = new List<Segment>();
        foreach (var edge in edges)
        {
            // A horizontal segment encloses nothing on its own
            if (edge.Y0 == edge.Y1)
                continue;

            var downward = edge.Y0 < edge.Y1;
            var (top, bottom) = downward ? ((edge.X0, edge.Y0), (edge.X1, edge.Y1)) : ((edge.X1, edge.Y1), (edge.X0, edge.Y0));
            segments.Add(new Segment(Fraction.FromDouble(top.Item1), Fraction.FromDouble(top.Item2), Fraction.FromDouble(bottom.Item1), Fraction.FromDouble(bottom.Item2), downward ? 1 : -1));
        }

        var areas = new Fraction[width * height];
        for (var row = 0; row < height; row++)
        {
            Fraction top = row;
            Fraction bottom = row + 1;
            var active = segments.Where(segment => segment.Y0 < bottom && segment.Y1 > top).ToList();
            if (active.Count == 0)
                continue;

            var events = new SortedSet<Fraction> { top, bottom };
            void AddEvent(Fraction y, Fraction low, Fraction high)
            {
                if (y > low && y < high && y > top && y < bottom)
                {
                    events.Add(y);
                }
            }

            for (var i = 0; i < active.Count; i++)
            {
                var a = active[i];
                AddEvent(a.Y0, top, bottom);
                AddEvent(a.Y1, top, bottom);
                if (a.X0 != a.X1)
                {
                    for (var column = 0; column <= width; column++)
                    {
                        AddEvent(a.Y0 + (((Fraction)column - a.X0) / a.Slope), a.Y0, a.Y1);
                    }
                }

                for (var j = i + 1; j < active.Count; j++)
                {
                    var b = active[j];
                    if (a.Slope == b.Slope)
                        continue;

                    // x = X0 + (y - Y0) * slope on both segments
                    var y = (b.X0 - (b.Y0 * b.Slope) - a.X0 + (a.Y0 * a.Slope)) / (a.Slope - b.Slope);
                    AddEvent(y, Max(a.Y0, b.Y0), Min(a.Y1, b.Y1));
                }
            }

            var heights = events.ToList();
            for (var i = 0; i + 1 < heights.Count; i++)
            {
                var slabTop = heights[i];
                var slabBottom = heights[i + 1];
                var middle = (slabTop + slabBottom) / 2;
                var slabHeight = slabBottom - slabTop;
                var crossing = active
                    .Where(segment => segment.Y0 <= slabTop && segment.Y1 >= slabBottom)
                    .Select(segment => (Middle: segment.GetX(middle), Mean: (segment.GetX(slabTop) + segment.GetX(slabBottom)) / 2, segment.Direction))
                    .OrderBy(item => item.Middle)
                    .ToList();
                var winding = 0;
                for (var j = 0; j + 1 < crossing.Count; j++)
                {
                    winding += crossing[j].Direction;
                    if (evenOdd ? (winding & 1) == 0 : winding == 0)
                        continue;

                    var left = crossing[j];
                    var right = crossing[j + 1];
                    var leftColumn = left.Middle.Floor();
                    var rightColumn = right.Middle.Floor();
                    var first = BigInteger.Max(leftColumn, BigInteger.Zero);
                    var last = BigInteger.Min(rightColumn, width - 1);
                    for (var column = first; column <= last; column++)
                    {
                        var low = column == leftColumn ? left.Mean : new Fraction(column, 1);
                        var high = column == rightColumn ? right.Mean : new Fraction(column + 1, 1);
                        areas[(row * width) + (int)column] += slabHeight * (high - low);
                    }
                }
            }
        }

        return areas;
    }

    /// <summary>Gets the coverage values the library may produce for an exact area: the nearest step, and the other neighbor within <see cref="TieWindow"/> of a tie.</summary>
    /// <param name="area">The exact area, between 0 and 1.</param>
    /// <returns>The accepted coverages, from 0 to <see cref="FullCoverage"/> (equal when only one is accepted).</returns>
    public static (int Low, int High) GetAcceptedCoverage(Fraction area)
    {
        var steps = area * FullCoverage;
        var floor = steps.Floor();
        var fraction = steps - new Fraction(floor, 1);
        var half = new Fraction(1, 2);
        var distance = fraction - half;
        if (distance < 0)
        {
            distance = (Fraction)0 - distance;
        }

        int Clamp(BigInteger value) => (int)BigInteger.Clamp(value, 0, FullCoverage);
        if (distance <= TieWindow)
            return (Clamp(floor), Clamp(floor + 1));

        var rounded = fraction >= half ? floor + 1 : floor;
        return (Clamp(rounded), Clamp(rounded));
    }

    /// <summary>Gets the winding number of a point: the directed segments crossing the horizontal line of the point on its left, counted with their direction.</summary>
    /// <param name="edges">The directed segments of closed contours.</param>
    /// <param name="x">The horizontal coordinate of the point.</param>
    /// <param name="y">The vertical coordinate of the point.</param>
    /// <returns>The winding number (exact as long as the point is not on a segment).</returns>
    public static int GetWinding(IReadOnlyList<ReferenceEdge> edges, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(edges);
        var px = Fraction.FromDouble(x);
        var py = Fraction.FromDouble(y);
        var winding = 0;
        foreach (var edge in edges)
        {
            if (edge.Y0 == edge.Y1)
                continue;

            var downward = edge.Y0 < edge.Y1;
            var (topX, topY, bottomX, bottomY) = downward ? (edge.X0, edge.Y0, edge.X1, edge.Y1) : (edge.X1, edge.Y1, edge.X0, edge.Y0);

            // Half-open in y, so that a vertex shared by two segments is counted once
            if (!(topY <= y && y < bottomY))
                continue;

            var segment = new Segment(Fraction.FromDouble(topX), Fraction.FromDouble(topY), Fraction.FromDouble(bottomX), Fraction.FromDouble(bottomY), downward ? 1 : -1);
            if (segment.GetX(py) < px)
            {
                winding += segment.Direction;
            }
        }

        return winding;
    }

    private static Fraction Min(Fraction left, Fraction right) => left < right ? left : right;

    private static Fraction Max(Fraction left, Fraction right) => left > right ? left : right;

    /// <summary>A non-horizontal segment from its top end to its bottom end; the direction is +1 when the contour goes downward.</summary>
    private sealed record Segment(Fraction X0, Fraction Y0, Fraction X1, Fraction Y1, int Direction)
    {
        /// <summary>Gets the horizontal distance per unit of height.</summary>
        public Fraction Slope { get; } = (X1 - X0) / (Y1 - Y0);

        public Fraction GetX(Fraction y) => X0 + ((y - Y0) * Slope);
    }
}
