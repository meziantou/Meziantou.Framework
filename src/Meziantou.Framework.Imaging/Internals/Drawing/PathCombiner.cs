using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Computes the outline of the area one path fills, or of a combination of the areas two paths fill.</summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> The paths are flattened as they are drawn, and their vertices are moved to the nearest point of a
/// grid of 1/1024 of a pixel. The result is the boundary of the area that is kept: closed contours of straight segments
/// whose vertices are on that grid, which share no more than vertices. The area is on the right of each contour: its
/// outer contours turn clockwise on the image and its holes the other way, so that both fill rules paint it.
/// </para>
/// <para>
/// <b>Method.</b> Everything is computed on the integers of the grid, exactly (the products of two differences of
/// coordinates fit in 128 bits), so that no decision depends on a rounding:
/// </para>
/// <list type="number">
/// <item><description>
/// The segments are cut where they meet: where the end of one is on another, and where two of them cross (at the
/// point of the grid nearest to the crossing). An end counts as on a segment when it is within a unit and a half of
/// the grid of it: a point that was on a segment before both were moved to the grid is cut there, so that the pieces
/// of a stroke, which share their sides, leave no crack between them. A cut moves a segment by about a unit of the
/// grid, which can make it meet other segments: the segments are cut again until none of them meets another one
/// elsewhere than at their ends. Segments that are almost on the same line can go on meeting: a cut turns a piece
/// slightly, which makes it cross its neighbors far from the cut. When a pass makes no fewer cuts than the one before,
/// after a few passes, or once the passes that follow the first one have made too many segments, they are snapped
/// instead: the points where they meet and their ends are hot, and each segment becomes the segments between the hot
/// points whose square of the grid it goes through, in their order along it. Snapping makes no crossing: two of the
/// segments it makes meet at their ends or are the same. It moves the segments by less than a unit of the grid, but
/// does not cut them at the ends that are only near them: simplifying an outline that was snapped can add a few
/// vertices to it.
/// </description></item>
/// <item><description>
/// The segments that have the same ends become one edge that counts how many of them go each way, for each path. A
/// sweep from the top gives the winding numbers on both sides of every edge: between two consecutive rows of vertices
/// the edges do not cross, so their order from the left is the order of the positions of their middles.
/// </description></item>
/// <item><description>
/// An edge whose two sides are not both kept, or both left out, is a part of the boundary. The contours are followed
/// from edge to edge; at a vertex where several meet, the next edge is the first one found by turning around the
/// vertex through the area that is kept, so that two contours touch at a vertex and never cross.
/// </description></item>
/// </list>
/// <para>
/// The first step compares every segment with the ones whose horizontal extents overlap its own: its cost grows with
/// the square of the number of segments for paths that are stacked on each other.
/// </para>
/// </remarks>
internal static class PathCombiner
{
    /// <summary>The number of units of the grid in a pixel.</summary>
    public const double GridScale = 1024;

    /// <summary>The number of times the segments are cut before they are snapped; more than paths that are not built against the method need.</summary>
    public const int DefaultMaxPasses = 8;

    /// <summary>Computes the outline of what a path fills, or of what two paths fill, combined.</summary>
    /// <param name="first">The first path.</param>
    /// <param name="second">The second path, or <see langword="null"/> for the outline of the first one alone.</param>
    /// <param name="mode">What is kept of the two areas.</param>
    /// <param name="fillRule">The fill rule of both paths.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The segments still meet elsewhere than at their ends once they are snapped.</exception>
    public static VectorPath Combine(VectorPath first, VectorPath? second, PathCombineMode mode, FillRule fillRule, CancellationToken cancellationToken)
        => Combine(first, second, mode, fillRule, DefaultMaxPasses, cancellationToken);

    /// <summary>Computes the outline of what a path fills, or of what two paths fill, combined, cutting the segments at most a number of times before they are snapped.</summary>
    /// <param name="first">The first path.</param>
    /// <param name="second">The second path, or <see langword="null"/> for the outline of the first one alone.</param>
    /// <param name="mode">What is kept of the two areas.</param>
    /// <param name="fillRule">The fill rule of both paths.</param>
    /// <param name="maxPasses">The number of times the segments are cut at most before they are snapped.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The segments still meet elsewhere than at their ends once they are snapped.</exception>
    public static VectorPath Combine(VectorPath first, VectorPath? second, PathCombineMode mode, FillRule fillRule, int maxPasses, CancellationToken cancellationToken)
    {
        var segments = new List<Segment>();
        PathFlattener.Flatten(first, new Collector(segments, source: 0));
        if (second is not null)
        {
            PathFlattener.Flatten(second, new Collector(segments, source: 1));
        }

        var builder = new VectorPathBuilder();
        if (segments.Count != 0)
        {
            segments = Planarize(segments, maxPasses, cancellationToken);
            var edges = BuildEdges(segments);
            ComputeWindings(edges, cancellationToken);
            Trace(edges, mode, fillRule, builder, cancellationToken);
        }

        return builder.Build();
    }

    /// <summary>Moves a coordinate to the grid: the nearest unit, halves upward.</summary>
    public static long ToGrid(double value) => (long)Math.Floor((value * GridScale) + 0.5);

    /// <summary>Cuts the segments until two of them only meet at their ends.</summary>
    /// <exception cref="InvalidOperationException">The segments still meet elsewhere than at their ends once they are snapped.</exception>
    private static List<Segment> Planarize(List<Segment> segments, int maxPasses, CancellationToken cancellationToken)
    {
        var cuts = new List<Cut>();

        // The first pass cuts the segments where the paths cross, which can make many more of them. A cut moves a
        // segment by about a unit of the grid, which can make it meet the segments around it: the passes that follow
        // cut those. Paths that are not built against the method are done in a few of them, each one making fewer cuts
        // than the one before. Segments that are almost on the same line are not: each cut turns a piece across its
        // neighbors, the cuts make new meetings faster than they remove them, and the segments are snapped instead.
        var limit = long.MaxValue;
        var previous = int.MaxValue;
        for (var pass = 0; ; pass++)
        {
            var isComplete = FindCuts(segments, cuts, previous, cancellationToken);
            if (cuts.Count == 0)
                return segments;

            if (!isComplete || pass == maxPasses || segments.Count > limit)
                break;

            previous = cuts.Count;
            segments = Split(segments, cuts);
            if (pass == 0)
            {
                limit = (segments.Count * 16L) + 4096;
            }
        }

        segments = Snap(segments, cancellationToken);

        // The edges are swept on the assumption that they only meet at their ends: a wrong outline is never returned.
        // The segments of a bundle are snapped to the same points: each one is only checked once.
        var distinct = new HashSet<Segment>(segments.Count);
        foreach (ref readonly var segment in CollectionsMarshal.AsSpan(segments))
        {
            var isForward = segment.X0 != segment.X1 ? segment.X0 < segment.X1 : segment.Y0 < segment.Y1;
            distinct.Add(isForward ? segment with { Source = 0 } : new Segment(segment.X1, segment.Y1, segment.X0, segment.Y0, Source: 0));
        }

        var unique = new Segment[distinct.Count];
        distinct.CopyTo(unique);
        var check = new PlanarityCheck();
        Sweep(unique, reach: 0, ref check, cancellationToken);
        if (check.IsViolated)
            throw new InvalidOperationException("The outline cannot be computed: the segments of the paths could not be cut so that they only meet at their ends.");

        return segments;
    }

    /// <summary>Finds where the segments meet elsewhere than at their ends, sorted by segment and by position along it.</summary>
    /// <returns><see langword="false"/> when the search stopped at the largest number of cuts, which leaves them unsorted.</returns>
    private static bool FindCuts(List<Segment> segments, List<Cut> cuts, int maxCuts, CancellationToken cancellationToken)
    {
        cuts.Clear();

        // What is within reach of a segment: two units of the grid around it
        var collector = new CutCollector(cuts, maxCuts);
        if (!Sweep(CollectionsMarshal.AsSpan(segments), reach: 2, ref collector, cancellationToken))
            return false;

        cuts.Sort((a, b) =>
        {
            if (a.Segment != b.Segment)
                return a.Segment.CompareTo(b.Segment);

            if (a.Position != b.Position)
                return a.Position.CompareTo(b.Position);

            return a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y);
        });
        return true;
    }

    /// <summary>Gives a visitor each pair of segments whose bounds, enlarged by a number of units of the grid, overlap, until it asks to stop.</summary>
    /// <returns><see langword="false"/> when the visitor asked to stop.</returns>
    private static bool Sweep<TVisitor>(ReadOnlySpan<Segment> span, long reach, ref TVisitor visitor, CancellationToken cancellationToken)
        where TVisitor : struct, IPairVisitor
    {
        // The segments from the left: one only meets those that start before it ends
        var order = new int[span.Length];
        var starts = new long[span.Length];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
            starts[i] = Math.Min(span[i].X0, span[i].X1);
        }

        Array.Sort(order, (a, b) => starts[a] != starts[b] ? starts[a].CompareTo(starts[b]) : a.CompareTo(b));
        for (var i = 0; i < order.Length; i++)
        {
            if ((i & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            ref readonly var s = ref span[order[i]];
            var right = Math.Max(s.X0, s.X1) + reach;
            var top = Math.Min(s.Y0, s.Y1) - reach;
            var bottom = Math.Max(s.Y0, s.Y1) + reach;
            for (var j = i + 1; j < order.Length && starts[order[j]] <= right; j++)
            {
                ref readonly var t = ref span[order[j]];
                if (Math.Min(t.Y0, t.Y1) > bottom || Math.Max(t.Y0, t.Y1) < top)
                    continue;

                if (!visitor.Visit(order[i], s, order[j], t))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Snaps the segments to the hot points of the grid, their ends and the points nearest to their crossings: each
    /// segment becomes the segments between the hot points whose square it goes through, in their order along it.
    /// </summary>
    /// <remarks>
    /// The square of a point of the grid is the points that are moved to it: from half a unit before it, included, to
    /// half a unit after it, excluded, on both axes. The squares do not overlap, and a segment that goes through the
    /// squares of two hot points also goes through the square of every hot point between them: the segments that are
    /// made meet at their ends or are the same, and no end is on another one.
    /// </remarks>
    private static List<Segment> Snap(List<Segment> segments, CancellationToken cancellationToken)
    {
        var span = CollectionsMarshal.AsSpan(segments);
        var hot = new HashSet<(long X, long Y)>();
        foreach (ref readonly var segment in span)
        {
            hot.Add((segment.X0, segment.Y0));
            hot.Add((segment.X1, segment.Y1));
        }

        var collector = new CrossingCollector(hot);
        Sweep(span, reach: 0, ref collector, cancellationToken);
        var points = new (long X, long Y)[hot.Count];
        hot.CopyTo(points);
        Array.Sort(points);

        var result = new List<Segment>(span.Length + points.Length);
        var found = new List<(Entry Entry, long X, long Y)>();
        for (var i = 0; i < span.Length; i++)
        {
            if ((i & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // A square meets the segment only when its point is within the bounds of the segment, whose ends are on the grid
            ref readonly var s = ref span[i];
            var left = Math.Min(s.X0, s.X1);
            var right = Math.Max(s.X0, s.X1);
            var top = Math.Min(s.Y0, s.Y1);
            var bottom = Math.Max(s.Y0, s.Y1);
            found.Clear();
            for (var index = LowerBound(points, left); index < points.Length && points[index].X <= right; index++)
            {
                var (x, y) = points[index];
                if (y >= top && y <= bottom && TryEnter(s, x, y, out var entry))
                {
                    found.Add((entry, x, y));
                }
            }

            // The ends of the segment are hot: the first point is its start and the last one its end
            found.Sort((a, b) => a.Entry.CompareOrder(b.Entry));
            for (var k = 1; k < found.Count; k++)
            {
                result.Add(new Segment(found[k - 1].X, found[k - 1].Y, found[k].X, found[k].Y, s.Source));
            }
        }

        return result;

        static int LowerBound((long X, long Y)[] points, long x)
        {
            var low = 0;
            var high = points.Length;
            while (low < high)
            {
                var middle = (low + high) >>> 1;
                if (points[middle].X < x)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }
    }

    /// <summary>Gets where a segment enters the square of a point of the grid, as a position from 0 at its start to 1 at its end.</summary>
    private static bool TryEnter(in Segment segment, long x, long y, out Entry entry)
    {
        entry = default;
        long dx = segment.X1 - segment.X0;
        long dy = segment.Y1 - segment.Y0;

        // The square is further than half a diagonal from the line when the distance from the line to its point,
        // the cross product over the length, exceeds it: 2 cross^2 > length^2. Over the sum of the two differences,
        // which the length is not above, it is further than a unit, which also keeps the square within 128 bits.
        var cross = Int128.Abs(Side(segment, x, y));
        if (cross > Math.Abs(dx) + Math.Abs(dy) || 2 * cross * cross > ((Int128)dx * dx) + ((Int128)dy * dy))
            return false;

        // The positions where the segment is within the square, on each axis, in halves of a unit
        var low = new Entry(0, 1, IsOpen: false);
        var high = new Entry(1, 1, IsOpen: false);
        if (!Clip(2 * segment.X0, 2 * dx, (2 * x) - 1, (2 * x) + 1, ref low, ref high) || !Clip(2 * segment.Y0, 2 * dy, (2 * y) - 1, (2 * y) + 1, ref low, ref high))
            return false;

        var order = low.CompareValue(high);
        if (order > 0 || (order == 0 && (low.IsOpen || high.IsOpen)))
            return false;

        entry = low;
        return true;

        // Keeps the positions where start + position * step is at least min, and below max
        static bool Clip(long start, long step, long min, long max, ref Entry low, ref Entry high)
        {
            if (step == 0)
                return min <= start && start < max;

            if (step > 0)
            {
                Raise(ref low, new Entry(min - start, step, IsOpen: false));
                Lower(ref high, new Entry(max - start, step, IsOpen: true));
            }
            else
            {
                Lower(ref high, new Entry(start - min, -step, IsOpen: false));
                Raise(ref low, new Entry(start - max, -step, IsOpen: true));
            }

            return true;
        }

        static void Raise(ref Entry low, Entry bound)
        {
            var order = bound.CompareValue(low);
            if (order > 0 || (order == 0 && bound.IsOpen))
            {
                low = bound;
            }
        }

        static void Lower(ref Entry high, Entry bound)
        {
            var order = bound.CompareValue(high);
            if (order < 0 || (order == 0 && bound.IsOpen))
            {
                high = bound;
            }
        }
    }

    /// <summary>Gets the point of the grid nearest to where two segments cross, when each one has an end on each side of the other.</summary>
    private static bool TryGetCrossing(in Segment first, in Segment second, out long x, out long y)
    {
        // The side of each end of a segment, from the line of the other one
        var a = Side(second, first.X0, first.Y0);
        var b = Side(second, first.X1, first.Y1);
        var c = Side(first, second.X0, second.Y0);
        var d = Side(first, second.X1, second.Y1);
        if (((a > 0 && b < 0) || (a < 0 && b > 0)) && ((c > 0 && d < 0) || (c < 0 && d > 0)))
        {
            var divisor = a - b;
            x = first.X0 + RoundedDivide((first.X1 - first.X0) * a, divisor);
            y = first.Y0 + RoundedDivide((first.Y1 - first.Y0) * a, divisor);
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    /// <summary>Gets a value indicating whether a point is on a segment, elsewhere than at its ends.</summary>
    private static bool IsInside(in Segment segment, long x, long y)
    {
        if (Side(segment, x, y) != 0 || (x == segment.X0 && y == segment.Y0) || (x == segment.X1 && y == segment.Y1))
            return false;

        var along = ((Int128)(x - segment.X0) * (segment.X1 - segment.X0)) + ((Int128)(y - segment.Y0) * (segment.Y1 - segment.Y0));
        var lengthSquared = ((Int128)(segment.X1 - segment.X0) * (segment.X1 - segment.X0)) + ((Int128)(segment.Y1 - segment.Y0) * (segment.Y1 - segment.Y0));
        return along > 0 && along < lengthSquared;
    }

    /// <summary>Makes each segment the segments between its cuts, in their order along it.</summary>
    private static List<Segment> Split(List<Segment> segments, List<Cut> cuts)
    {
        var span = CollectionsMarshal.AsSpan(segments);
        var result = new List<Segment>(segments.Count + cuts.Count);
        var next = 0;
        for (var i = 0; i < span.Length; i++)
        {
            ref readonly var s = ref span[i];
            var x = s.X0;
            var y = s.Y0;
            for (; next < cuts.Count && cuts[next].Segment == i; next++)
            {
                var cut = cuts[next];
                if (cut.X == x && cut.Y == y)
                    continue;

                result.Add(new Segment(x, y, cut.X, cut.Y, s.Source));
                x = cut.X;
                y = cut.Y;
            }

            if (x != s.X1 || y != s.Y1)
            {
                result.Add(new Segment(x, y, s.X1, s.Y1, s.Source));
            }
        }

        return result;
    }

    /// <summary>Records where two segments are cut by each other: at an end of one that is on the other, or else where they cross.</summary>
    private static void AddCuts(int firstIndex, in Segment first, int secondIndex, in Segment second, List<Cut> cuts)
    {
        // An end that is on the other segment, or was before the grid moved them apart, cuts it: no vertex is added
        var count = cuts.Count;
        AddCutWhenNear(secondIndex, second, first.X0, first.Y0, cuts);
        AddCutWhenNear(secondIndex, second, first.X1, first.Y1, cuts);
        AddCutWhenNear(firstIndex, first, second.X0, second.Y0, cuts);
        AddCutWhenNear(firstIndex, first, second.X1, second.Y1, cuts);
        if (cuts.Count != count)
            return;

        // They cross at a point of both, which is cut at the nearest point of the grid
        if (TryGetCrossing(first, second, out var x, out var y))
        {
            AddCut(firstIndex, first, x, y, cuts);
            AddCut(secondIndex, second, x, y, cuts);
        }
    }

    /// <summary>
    /// Cuts a segment at a point that is on it, or within a unit and a half of the grid of it: two points that were on
    /// each other before they were moved to the grid are no further apart than the square root of two.
    /// </summary>
    private static void AddCutWhenNear(int index, in Segment segment, long x, long y, List<Cut> cuts)
    {
        if ((x == segment.X0 && y == segment.Y0) || (x == segment.X1 && y == segment.Y1))
            return;

        Int128 dx = segment.X1 - segment.X0;
        Int128 dy = segment.Y1 - segment.Y0;
        Int128 px = x - segment.X0;
        Int128 py = y - segment.Y0;
        var along = (px * dx) + (py * dy);
        var lengthSquared = (dx * dx) + (dy * dy);
        if (along < 0 || along > lengthSquared)
            return;

        // The distance to the line is the cross product over the length: at most 3/2 when 4 cross^2 <= 9 length^2.
        // The length is at most the sum of the two differences, which keeps the square within 128 bits.
        var cross = Int128.Abs((dx * py) - (dy * px));
        if (cross > 2 * (Int128.Abs(dx) + Int128.Abs(dy)) || 4 * cross * cross > 9 * lengthSquared)
            return;

        // Both parts must be shorter than the segment: three vertices closer to each other than the tolerance would
        // otherwise cut the segments between them at each other without end
        Int128 qx = x - segment.X1;
        Int128 qy = y - segment.Y1;
        if ((px * px) + (py * py) >= lengthSquared || (qx * qx) + (qy * qy) >= lengthSquared)
            return;

        cuts.Add(new Cut(index, along, x, y));
    }

    private static void AddCut(int index, in Segment segment, long x, long y, List<Cut> cuts)
    {
        if ((x == segment.X0 && y == segment.Y0) || (x == segment.X1 && y == segment.Y1))
            return;

        var position = ((Int128)(x - segment.X0) * (segment.X1 - segment.X0)) + ((Int128)(y - segment.Y0) * (segment.Y1 - segment.Y0));
        cuts.Add(new Cut(index, position, x, y));
    }

    /// <summary>Gets a number whose sign tells the side of the line of a segment a point is on; zero on the line.</summary>
    private static Int128 Side(in Segment segment, long x, long y)
        => ((Int128)(segment.X1 - segment.X0) * (y - segment.Y0)) - ((Int128)(segment.Y1 - segment.Y0) * (x - segment.X0));

    /// <summary>Divides and rounds to the nearest integer, a half toward positive infinity.</summary>
    private static long RoundedDivide(Int128 numerator, Int128 divisor)
    {
        if (divisor < 0)
        {
            numerator = -numerator;
            divisor = -divisor;
        }

        var doubled = (numerator * 2) + divisor;
        var quotient = doubled / (divisor * 2);
        if (doubled < 0 && quotient * divisor * 2 != doubled)
        {
            quotient--;
        }

        return (long)quotient;
    }

    /// <summary>Merges the segments that have the same ends into edges, from the top-left one.</summary>
    private static Edge[] BuildEdges(List<Segment> segments)
    {
        var counts = new Dictionary<(long LowX, long LowY, long HighX, long HighY), (int First, int Second)>(segments.Count);
        foreach (ref readonly var segment in CollectionsMarshal.AsSpan(segments))
        {
            // An edge goes from its top end to its bottom end, or from its left end to its right end when it is horizontal
            var isForward = segment.Y0 != segment.Y1 ? segment.Y0 < segment.Y1 : segment.X0 < segment.X1;
            var key = isForward ? (segment.X0, segment.Y0, segment.X1, segment.Y1) : (segment.X1, segment.Y1, segment.X0, segment.Y0);
            ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(counts, key, out _);
            var step = isForward ? 1 : -1;
            if (segment.Source == 0)
            {
                count.First += step;
            }
            else
            {
                count.Second += step;
            }
        }

        var edges = new List<Edge>(counts.Count);
        foreach (var (key, count) in counts)
        {
            // As many segments each way: the edge separates nothing
            if (count.First != 0 || count.Second != 0)
            {
                edges.Add(new Edge { LowX = key.LowX, LowY = key.LowY, HighX = key.HighX, HighY = key.HighY, First = count.First, Second = count.Second });
            }
        }

        var result = edges.ToArray();
        Array.Sort(result, (a, b) =>
        {
            if (a.LowY != b.LowY)
                return a.LowY.CompareTo(b.LowY);

            if (a.LowX != b.LowX)
                return a.LowX.CompareTo(b.LowX);

            return a.HighY != b.HighY ? a.HighY.CompareTo(b.HighY) : a.HighX.CompareTo(b.HighX);
        });
        return result;
    }

    /// <summary>
    /// Gives each edge the winding numbers of the points on its left (of the points under it, for a horizontal edge):
    /// those of the points on its other side are these numbers plus the counts of the edge.
    /// </summary>
    private static void ComputeWindings(Edge[] edges, CancellationToken cancellationToken)
    {
        var rows = new List<long>(edges.Length * 2);
        foreach (ref readonly var edge in edges.AsSpan())
        {
            rows.Add(edge.LowY);
            rows.Add(edge.HighY);
        }

        rows.Sort();
        var active = new List<int>();
        var horizontal = new List<int>();
        var next = 0;
        for (var row = 0; row < rows.Count; row++)
        {
            var y = rows[row];
            if (row > 0 && y == rows[row - 1])
                continue;

            cancellationToken.ThrowIfCancellationRequested();

            // The edges that end on this row leave; those that start on it come in, at their place from the left
            var kept = 0;
            for (var i = 0; i < active.Count; i++)
            {
                if (edges[active[i]].HighY != y)
                {
                    active[kept++] = active[i];
                }
            }

            active.RemoveRange(kept, active.Count - kept);
            horizontal.Clear();
            var following = row + 1;
            while (following < rows.Count && rows[following] == y)
            {
                following++;
            }

            for (; next < edges.Length && edges[next].LowY == y; next++)
            {
                if (edges[next].HighY == y)
                {
                    horizontal.Add(next);
                    continue;
                }

                // Twice the row in the middle of this row and of the next one, where no two edges are at the same place
                var middle = y + rows[following];
                var low = 0;
                var high = active.Count;
                while (low < high)
                {
                    var index = (low + high) >>> 1;
                    if (ComparePositions(edges[active[index]], edges[next], middle) < 0)
                    {
                        low = index + 1;
                    }
                    else
                    {
                        high = index;
                    }
                }

                active.Insert(low, next);
            }

            var first = 0;
            var second = 0;
            foreach (var index in active)
            {
                ref var edge = ref edges[index];
                edge.WindingFirst = first;
                edge.WindingSecond = second;
                first += edge.First;
                second += edge.Second;
            }

            // Under a horizontal edge: on the right of the last edge that goes down from its left end, or from further left
            foreach (var index in horizontal)
            {
                ref var edge = ref edges[index];
                edge.WindingFirst = 0;
                edge.WindingSecond = 0;
                foreach (var other in active)
                {
                    ref readonly var candidate = ref edges[other];
                    if (!IsAtOrBefore(candidate, y, edge.LowX))
                        break;

                    edge.WindingFirst = candidate.WindingFirst + candidate.First;
                    edge.WindingSecond = candidate.WindingSecond + candidate.Second;
                }
            }
        }
    }

    /// <summary>Compares the horizontal positions of two edges that are not horizontal on a row both cross, given as twice its coordinate.</summary>
    private static int ComparePositions(in Edge a, in Edge b, long doubledY)
    {
        // The position of an edge is its numerator over twice its height
        var heightA = (Int128)(a.HighY - a.LowY) * 2;
        var heightB = (Int128)(b.HighY - b.LowY) * 2;
        var numeratorA = (a.LowX * heightA) + ((Int128)(a.HighX - a.LowX) * (doubledY - (2 * a.LowY)));
        var numeratorB = (b.LowX * heightB) + ((Int128)(b.HighX - b.LowX) * (doubledY - (2 * b.LowY)));
        return (numeratorA * heightB).CompareTo(numeratorB * heightA);
    }

    /// <summary>Gets a value indicating whether an edge that is not horizontal is, on a row it reaches, at a position or on its left.</summary>
    private static bool IsAtOrBefore(in Edge edge, long y, long x)
    {
        var height = (Int128)(edge.HighY - edge.LowY);
        return (edge.LowX * height) + ((Int128)(edge.HighX - edge.LowX) * (y - edge.LowY)) <= x * height;
    }

    /// <summary>Follows the edges of the boundary of the area that is kept, and adds each contour to a path.</summary>
    private static void Trace(Edge[] edges, PathCombineMode mode, FillRule fillRule, VectorPathBuilder builder, CancellationToken cancellationToken)
    {
        // The edges of the boundary, directed so that the area is on their right as the image shows them
        var boundary = new List<Directed>();
        foreach (ref readonly var edge in edges.AsSpan())
        {
            var before = IsKept(edge.WindingFirst, edge.WindingSecond, mode, fillRule);
            var after = IsKept(edge.WindingFirst + edge.First, edge.WindingSecond + edge.Second, mode, fillRule);
            if (before == after)
                continue;

            boundary.Add(before ? new Directed(edge.LowX, edge.LowY, edge.HighX, edge.HighY) : new Directed(edge.HighX, edge.HighY, edge.LowX, edge.LowY));
        }

        var directed = CollectionsMarshal.AsSpan(boundary);
        var leaving = new Dictionary<(long X, long Y), List<int>>(directed.Length);
        for (var i = 0; i < directed.Length; i++)
        {
            ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(leaving, (directed[i].FromX, directed[i].FromY), out _);
            list ??= [];
            list.Add(i);
        }

        var used = new bool[directed.Length];
        var contour = new List<(long X, long Y)>();
        for (var start = 0; start < directed.Length; start++)
        {
            if (used[start])
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            contour.Clear();
            var current = start;
            while (true)
            {
                ref readonly var edge = ref directed[current];
                used[current] = true;
                contour.Add((edge.FromX, edge.FromY));
                if (!leaving.TryGetValue((edge.ToX, edge.ToY), out var candidates))
                    break;

                var next = PickNext(directed, candidates, edge);
                if (used[next])
                    break;

                current = next;
            }

            AddContour(contour, leaving, builder);
        }
    }

    /// <summary>
    /// Picks the edge that continues a contour at the vertex an edge ends at: the first one found by turning from that
    /// edge around the vertex, through the area that is kept.
    /// </summary>
    private static int PickNext(ReadOnlySpan<Directed> directed, List<int> candidates, in Directed arriving)
    {
        var best = candidates[0];
        if (candidates.Count == 1)
            return best;

        // The direction back along the edge the contour arrives by
        var backX = arriving.FromX - arriving.ToX;
        var backY = arriving.FromY - arriving.ToY;
        for (var i = 1; i < candidates.Count; i++)
        {
            if (IsBefore(directed[candidates[i]], directed[best], backX, backY))
            {
                best = candidates[i];
            }
        }

        return best;
    }

    /// <summary>Gets a value indicating whether an edge is found before another one by turning from a direction around the vertex both leave.</summary>
    private static bool IsBefore(in Directed a, in Directed b, long backX, long backY)
    {
        var ax = a.ToX - a.FromX;
        var ay = a.ToY - a.FromY;
        var bx = b.ToX - b.FromX;
        var by = b.ToY - b.FromY;
        var partA = GetPart(ax, ay, backX, backY);
        var partB = GetPart(bx, by, backX, backY);
        if (partA != partB)
            return partA < partB;

        // In the same half turn: the one the other is reached from by turning the same way
        return ((Int128)bx * ay) - ((Int128)by * ax) > 0;

        static int GetPart(long x, long y, long backX, long backY)
        {
            // The sine and the cosine of the angle from the direction back to the edge, up to a positive factor
            var sine = ((Int128)x * backY) - ((Int128)y * backX);
            if (sine != 0)
                return sine > 0 ? 0 : 2;

            return ((Int128)x * backX) + ((Int128)y * backY) < 0 ? 1 : 3;
        }
    }

    private static bool IsKept(int first, int second, PathCombineMode mode, FillRule fillRule)
    {
        var inFirst = fillRule == FillRule.EvenOdd ? (first & 1) != 0 : first != 0;
        var inSecond = fillRule == FillRule.EvenOdd ? (second & 1) != 0 : second != 0;
        return mode switch
        {
            PathCombineMode.Intersect => inFirst && inSecond,
            PathCombineMode.Exclude => inFirst && !inSecond,
            PathCombineMode.Xor => inFirst != inSecond,
            _ => inFirst || inSecond,
        };
    }

    /// <summary>
    /// Adds a contour to a path, from its top-left vertex, without the vertices that are on the segment between their
    /// neighbors, unless another contour touches it there.
    /// </summary>
    private static void AddContour(List<(long X, long Y)> contour, Dictionary<(long X, long Y), List<int>> leaving, VectorPathBuilder builder)
    {
        // The contour starts at the same vertex whatever the edge it was followed from. It can go through that vertex
        // more than once: it then starts where the vertex that follows is the first one.
        var count = contour.Count;
        var first = 0;
        for (var i = 1; i < count; i++)
        {
            var order = Compare(contour[i], contour[first]);
            if (order < 0 || (order == 0 && Compare(contour[(i + 1) % count], contour[(first + 1) % count]) < 0))
            {
                first = i;
            }
        }

        var written = 0;
        for (var offset = 0; offset < count; offset++)
        {
            var i = (first + offset) % count;
            var previous = contour[(i + count - 1) % count];
            var point = contour[i];
            var next = contour[(i + 1) % count];
            var cross = ((Int128)(point.X - previous.X) * (next.Y - point.Y)) - ((Int128)(point.Y - previous.Y) * (next.X - point.X));
            var dot = ((Int128)(point.X - previous.X) * (next.X - point.X)) + ((Int128)(point.Y - previous.Y) * (next.Y - point.Y));
            if (cross == 0 && dot > 0 && leaving[point].Count == 1)
                continue;

            if (written++ == 0)
            {
                builder.AppendMoveTo(point.X / GridScale, point.Y / GridScale);
            }
            else
            {
                builder.AppendLineTo(point.X / GridScale, point.Y / GridScale);
            }
        }

        builder.Close();

        static int Compare((long X, long Y) a, (long X, long Y) b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X);
    }

    private interface IPairVisitor
    {
        /// <summary>Visits a pair of segments.</summary>
        /// <returns><see langword="false"/> to stop the sweep.</returns>
        bool Visit(int firstIndex, in Segment first, int secondIndex, in Segment second);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Segment(long X0, long Y0, long X1, long Y1, byte Source);

    /// <summary>A point a segment is cut at, and its position along the segment.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Cut(int Segment, Int128 Position, long X, long Y);

    /// <summary>A position along a segment, a fraction whose denominator is positive, and whether it is excluded: a bound of the positions within a square.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Entry(Int128 Numerator, Int128 Denominator, bool IsOpen)
    {
        public int CompareValue(Entry other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

        /// <summary>Compares two positions where a segment enters squares: an included one comes before an excluded one at the same place.</summary>
        public int CompareOrder(Entry other)
        {
            var order = CompareValue(other);
            return order != 0 ? order : IsOpen.CompareTo(other.IsOpen);
        }
    }

    /// <summary>Records where two segments are cut by each other, until there are a number of cuts.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct CutCollector(List<Cut> cuts, int maxCuts) : IPairVisitor
    {
        public bool Visit(int firstIndex, in Segment first, int secondIndex, in Segment second)
        {
            AddCuts(firstIndex, first, secondIndex, second, cuts);
            return cuts.Count < maxCuts;
        }
    }

    /// <summary>Records the points of the grid nearest to where two segments cross.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct CrossingCollector(HashSet<(long X, long Y)> points) : IPairVisitor
    {
        public bool Visit(int firstIndex, in Segment first, int secondIndex, in Segment second)
        {
            if (TryGetCrossing(first, second, out var x, out var y))
            {
                points.Add((x, y));
            }

            return true;
        }
    }

    /// <summary>Finds two segments that meet elsewhere than at their ends: they cross, or an end of one is on the other.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct PlanarityCheck : IPairVisitor
    {
        public bool IsViolated { get; private set; }

        public bool Visit(int firstIndex, in Segment first, int secondIndex, in Segment second)
        {
            IsViolated = TryGetCrossing(first, second, out _, out _) ||
                IsInside(first, second.X0, second.Y0) || IsInside(first, second.X1, second.Y1) ||
                IsInside(second, first.X0, first.Y0) || IsInside(second, first.X1, first.Y1);
            return !IsViolated;
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Directed(long FromX, long FromY, long ToX, long ToY);

    [StructLayout(LayoutKind.Auto)]
    private struct Edge
    {
        public long LowX;
        public long LowY;
        public long HighX;
        public long HighY;

        /// <summary>The number of segments of the first path that go from the first end to the second one, less those that go the other way.</summary>
        public int First;
        public int Second;

        /// <summary>The winding number, around the first path, of the points on the left of the edge (under it when it is horizontal).</summary>
        public int WindingFirst;
        public int WindingSecond;
    }

    /// <summary>Collects the segments of the contours of a path, on the grid. An open contour is closed, as when it is filled.</summary>
    private sealed class Collector(List<Segment> segments, byte source) : PathSink
    {
        private bool _isOpen;
        private long _startX;
        private long _startY;
        private long _x;
        private long _y;

        public override void MoveTo(double x, double y)
        {
            End();
            _x = _startX = ToGrid(x);
            _y = _startY = ToGrid(y);
            _isOpen = true;
        }

        public override void LineTo(double x, double y, bool smooth)
        {
            if (!_isOpen)
            {
                MoveTo(x, y);
                return;
            }

            Add(ToGrid(x), ToGrid(y));
        }

        public override void Close(bool smooth)
        {
            if (_isOpen)
            {
                Add(_startX, _startY);
            }
        }

        public override void End()
        {
            Close(smooth: false);
            _isOpen = false;
        }

        private void Add(long x, long y)
        {
            if (x != _x || y != _y)
            {
                segments.Add(new Segment(_x, _y, x, y, source));
                _x = x;
                _y = y;
            }
        }

    }
}
