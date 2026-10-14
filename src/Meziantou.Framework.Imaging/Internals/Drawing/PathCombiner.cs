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
/// elsewhere than at their ends.
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

    /// <summary>The number of times the segments are cut again; far more than paths that are not built against the method need.</summary>
    private const int MaxPasses = 64;

    /// <summary>Computes the outline of what a path fills, or of what two paths fill, combined.</summary>
    /// <param name="first">The first path.</param>
    /// <param name="second">The second path, or <see langword="null"/> for the outline of the first one alone.</param>
    /// <param name="mode">What is kept of the two areas.</param>
    /// <param name="fillRule">The fill rule of both paths.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static VectorPath Combine(VectorPath first, VectorPath? second, PathCombineMode mode, FillRule fillRule, CancellationToken cancellationToken)
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
            segments = Planarize(segments, cancellationToken);
            var edges = BuildEdges(segments);
            ComputeWindings(edges, cancellationToken);
            Trace(edges, mode, fillRule, builder, cancellationToken);
        }

        return builder.Build();
    }

    /// <summary>Moves a coordinate to the grid: the nearest unit, halves upward.</summary>
    public static long ToGrid(double value) => (long)Math.Floor((value * GridScale) + 0.5);

    /// <summary>Cuts the segments until two of them only meet at their ends.</summary>
    private static List<Segment> Planarize(List<Segment> segments, CancellationToken cancellationToken)
    {
        var cuts = new List<Cut>();

        // A cut makes two segments of one. Paths that are not built against the method are done in a few passes and
        // gain a few segments; the limits keep the others from growing without end, and leave them as they are then.
        var limit = (segments.Count * 16L) + 4096;
        for (var pass = 0; pass < MaxPasses && segments.Count <= limit; pass++)
        {
            cuts.Clear();
            var span = CollectionsMarshal.AsSpan(segments);

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

                // What is within reach of the segment: two units of the grid around it
                ref readonly var s = ref span[order[i]];
                var right = Math.Max(s.X0, s.X1) + 2;
                var top = Math.Min(s.Y0, s.Y1) - 2;
                var bottom = Math.Max(s.Y0, s.Y1) + 2;
                for (var j = i + 1; j < order.Length && starts[order[j]] <= right; j++)
                {
                    ref readonly var t = ref span[order[j]];
                    if (Math.Min(t.Y0, t.Y1) > bottom || Math.Max(t.Y0, t.Y1) < top)
                        continue;

                    AddCuts(order[i], s, order[j], t, cuts);
                }
            }

            if (cuts.Count == 0)
                break;

            // Each segment becomes the segments between its cuts, in their order along it
            cuts.Sort((a, b) =>
            {
                if (a.Segment != b.Segment)
                    return a.Segment.CompareTo(b.Segment);

                if (a.Position != b.Position)
                    return a.Position.CompareTo(b.Position);

                return a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y);
            });

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

            segments = result;
        }

        return segments;
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

        // The side of each end of a segment, from the line of the other one
        var a = Side(second, first.X0, first.Y0);
        var b = Side(second, first.X1, first.Y1);
        var c = Side(first, second.X0, second.Y0);
        var d = Side(first, second.X1, second.Y1);
        if (((a > 0 && b < 0) || (a < 0 && b > 0)) && ((c > 0 && d < 0) || (c < 0 && d > 0)))
        {
            // They cross at a point of both, which is cut at the nearest point of the grid
            var divisor = a - b;
            var x = first.X0 + RoundedDivide((first.X1 - first.X0) * a, divisor);
            var y = first.Y0 + RoundedDivide((first.Y1 - first.Y0) * a, divisor);
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

            AddContour(contour, builder);
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
    /// neighbors.
    /// </summary>
    private static void AddContour(List<(long X, long Y)> contour, VectorPathBuilder builder)
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
            if (cross == 0 && dot > 0)
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

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Segment(long X0, long Y0, long X1, long Y1, byte Source);

    /// <summary>A point a segment is cut at, and its position along the segment.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Cut(int Segment, Int128 Position, long X, long Y);

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
