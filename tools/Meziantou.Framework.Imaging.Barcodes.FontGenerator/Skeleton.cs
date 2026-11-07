namespace Meziantou.Framework.Imaging.Barcodes.FontGenerator;

/// <summary>Thins an area of ink to its skeleton (Zhang-Suen) and traces the skeleton into polylines.</summary>
internal static class Skeleton
{
    private static readonly (int X, int Y)[] Neighbors = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    /// <summary>Traces the centerlines of an area: the chains of the skeleton between its ends and its junctions, and its loops.</summary>
    /// <param name="component">The area.</param>
    /// <param name="spurLength">The length below which a chain from a junction to an end is a corner of the area, not a stroke.</param>
    /// <returns>The chains, in pixels; a loop starts and ends at the same point.</returns>
    public static List<TracedChain> Trace(Component component, double spurLength, int maxHoleArea)
    {
        var width = component.Right - component.Left + 2;
        var height = component.Bottom - component.Top + 2;
        var grid = new bool[width, height];
        foreach (var (x, y) in component.Pixels)
        {
            grid[x - component.Left + 1, y - component.Top + 1] = true;
        }

        FillHoles(grid, width, height, maxHoleArea);
        Thin(grid, width, height);

        bool At(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && grid[x, y];
        int Transitions(int x, int y)
        {
            var count = 0;
            for (var i = 0; i < 8; i++)
            {
                var (ax, ay) = Neighbors[i];
                var (bx, by) = Neighbors[(i + 1) % 8];
                if (!At(x + ax, y + ay) && At(x + bx, y + by))
                {
                    count++;
                }
            }

            return count;
        }

        // Nodes: the ends (one transition) and the junctions (three or more); adjacent junction pixels are one junction
        var nodeOf = new Dictionary<(int, int), int>();
        var nodes = new List<(double X, double Y, bool IsEnd)>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!grid[x, y] || nodeOf.ContainsKey((x, y)))
                    continue;

                var transitions = Transitions(x, y);
                if (transitions == 1)
                {
                    nodeOf[(x, y)] = nodes.Count;
                    nodes.Add((x, y, true));
                }
                else if (transitions >= 3)
                {
                    var cluster = new List<(int X, int Y)>();
                    var queue = new Queue<(int X, int Y)>();
                    queue.Enqueue((x, y));
                    nodeOf[(x, y)] = nodes.Count;
                    while (queue.Count > 0)
                    {
                        var (cx, cy) = queue.Dequeue();
                        cluster.Add((cx, cy));
                        foreach (var (dx, dy) in Neighbors)
                        {
                            var (nx, ny) = (cx + dx, cy + dy);
                            if (At(nx, ny) && !nodeOf.ContainsKey((nx, ny)) && Transitions(nx, ny) >= 3)
                            {
                                nodeOf[(nx, ny)] = nodes.Count;
                                queue.Enqueue((nx, ny));
                            }
                        }
                    }

                    nodes.Add((cluster.Average(p => p.X), cluster.Average(p => p.Y), false));
                }
            }
        }

        // Edges between the nodes
        var visited = new HashSet<(int, int)>();
        var edges = new List<(int Start, int End, List<(double X, double Y)> Points)>();
        foreach (var ((x, y), node) in nodeOf.ToArray())
        {
            foreach (var (dx, dy) in Neighbors)
            {
                var (nx, ny) = (x + dx, y + dy);
                if (!At(nx, ny) || visited.Contains((nx, ny)))
                    continue;

                if (nodeOf.TryGetValue((nx, ny), out var adjacent))
                {
                    if (adjacent != node && node < adjacent && !edges.Any(edge => (edge.Start, edge.End) == (node, adjacent)))
                    {
                        edges.Add((node, adjacent, [(nodes[node].X, nodes[node].Y), (nodes[adjacent].X, nodes[adjacent].Y)]));
                    }

                    continue;
                }

                var points = new List<(double X, double Y)> { (nodes[node].X, nodes[node].Y) };
                var (px, py) = (x, y);
                var (cx, cy) = (nx, ny);
                var end = -1;
                while (true)
                {
                    visited.Add((cx, cy));
                    points.Add((cx, cy));
                    (int X, int Y)? next = null;
                    foreach (var (ox, oy) in Neighbors.OrderBy(n => Math.Abs(n.X) + Math.Abs(n.Y)))
                    {
                        var (qx, qy) = (cx + ox, cy + oy);
                        if ((qx, qy) == (px, py) || !At(qx, qy))
                            continue;

                        if (nodeOf.TryGetValue((qx, qy), out var target) && (target != node || points.Count > 3))
                        {
                            end = target;
                            break;
                        }

                        if (!visited.Contains((qx, qy)) && !nodeOf.ContainsKey((qx, qy)))
                        {
                            next = (qx, qy);
                            break;
                        }
                    }

                    if (end >= 0 || next is null)
                        break;

                    (px, py) = (cx, cy);
                    (cx, cy) = next.Value;
                }

                if (end >= 0)
                {
                    points.Add((nodes[end].X, nodes[end].Y));
                }

                edges.Add((node, end, points));
            }
        }

        // Loops: what is left of the skeleton has no node
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!grid[x, y] || visited.Contains((x, y)) || nodeOf.ContainsKey((x, y)))
                    continue;

                var points = new List<(double X, double Y)>();
                var (cx, cy) = (x, y);
                while (true)
                {
                    visited.Add((cx, cy));
                    points.Add((cx, cy));
                    (int X, int Y)? next = null;
                    foreach (var (ox, oy) in Neighbors.OrderBy(n => Math.Abs(n.X) + Math.Abs(n.Y)))
                    {
                        if (At(cx + ox, cy + oy) && !visited.Contains((cx + ox, cy + oy)))
                        {
                            next = (cx + ox, cy + oy);
                            break;
                        }
                    }

                    if (next is null)
                        break;

                    (cx, cy) = next.Value;
                }

                if (points.Count > 8)
                {
                    points.Add(points[0]);
                    edges.Add((-1, -1, points));
                }
            }
        }

        // The corners of a thick stroke leave short chains from a junction to an end: they are removed, and the two
        // chains left at the junction are joined
        var removed = true;
        while (removed)
        {
            removed = false;
            for (var i = 0; i < edges.Count; i++)
            {
                var (s, e, points) = edges[i];
                var startIsEnd = s >= 0 && nodes[s].IsEnd;
                var endIsEnd = e < 0 || nodes[e].IsEnd;
                if (startIsEnd != endIsEnd && Length(points) < spurLength && edges.Count > 1)
                {
                    edges.RemoveAt(i);
                    removed = true;
                    break;
                }
            }

            for (var node = 0; node < nodes.Count && !removed; node++)
            {
                if (nodes[node].IsEnd)
                    continue;

                var touching = edges.Select((edge, index) => (edge, index)).Where(item => item.edge.Start == node || item.edge.End == node).ToList();
                if (touching.Count != 2 || touching[0].index == touching[1].index)
                    continue;

                var first = touching[0].edge;
                var second = touching[1].edge;
                var a = first.End == node ? first.Points : Enumerable.Reverse(first.Points).ToList();
                var b = second.Start == node ? second.Points : Enumerable.Reverse(second.Points).ToList();
                var aStart = first.End == node ? first.Start : first.End;
                var bEnd = second.Start == node ? second.End : second.Start;
                var joined = a.Concat(b.Skip(1)).ToList();
                foreach (var index in new[] { touching[0].index, touching[1].index }.OrderDescending())
                {
                    edges.RemoveAt(index);
                }

                if (aStart == bEnd && aStart == node)
                {
                    joined[^1] = joined[0];
                }

                edges.Add((aStart, bEnd, joined));
                removed = true;
            }
        }

        // Specks: small loops and short chains that touch nothing
        edges.RemoveAll(edge =>
            (edge.Start < 0 && Length(edge.Points) < spurLength * 4) ||
            (edge.Start >= 0 && edge.End >= 0 && nodes[edge.Start].IsEnd && nodes[edge.End].IsEnd && Length(edge.Points) < spurLength && edges.Count > 1));

        return [.. edges.Select(edge => new TracedChain(
            [.. edge.Points.Select(p => (p.X + component.Left - 1 + 0.5, p.Y + component.Top - 1 + 0.5))],
            StartIsEnd: edge.Start >= 0 && nodes[edge.Start].IsEnd,
            EndIsEnd: edge.End >= 0 && nodes[edge.End].IsEnd))];
    }

    /// <summary>Traces the outer boundary of an area (Moore neighborhood), through the centers of its boundary pixels.</summary>
    public static List<(double X, double Y)> Outline(Component component)
    {
        var ink = component.Pixels.ToHashSet();
        var start = component.Pixels.OrderBy(p => p.Y).ThenBy(p => p.X).First();
        var outline = new List<(double X, double Y)> { (start.X + 0.5, start.Y + 0.5) };
        var current = start;

        // The neighbor the boundary was entered from: the pixel left of the first one, which is not ink
        var backtrack = 6;
        for (var steps = 0; steps < component.Pixels.Count * 8; steps++)
        {
            var found = false;
            for (var k = 1; k <= 8; k++)
            {
                var direction = (backtrack + k) % 8;
                var (dx, dy) = Neighbors[direction];
                var candidate = (current.X + dx, current.Y + dy);
                if (ink.Contains(candidate))
                {
                    backtrack = (direction + 4) % 8;
                    current = candidate;
                    found = true;
                    break;
                }
            }

            if (!found || current == start)
                break;

            outline.Add((current.X + 0.5, current.Y + 0.5));
        }

        return outline;
    }

    private static double Length(List<(double X, double Y)> points)
    {
        var length = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            length += Math.Sqrt(Math.Pow(points[i].X - points[i - 1].X, 2) + Math.Pow(points[i].Y - points[i - 1].Y, 2));
        }

        return length;
    }

    // The specks of the scan inside a stroke would be loops of the skeleton: the areas of background smaller than a
    // counter that do not reach the border are ink
    private static void FillHoles(bool[,] grid, int width, int height, int maxHoleArea)
    {
        var region = new int[width, height];
        var regionCount = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (grid[x, y] || region[x, y] != 0)
                    continue;

                regionCount++;
                var pixels = new List<(int X, int Y)>();
                var touchesBorder = false;
                var queue = new Queue<(int X, int Y)>();
                queue.Enqueue((x, y));
                region[x, y] = regionCount;
                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    pixels.Add((cx, cy));
                    touchesBorder |= cx == 0 || cy == 0 || cx == width - 1 || cy == height - 1;
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        var (nx, ny) = (cx + dx, cy + dy);
                        if (nx >= 0 && ny >= 0 && nx < width && ny < height && !grid[nx, ny] && region[nx, ny] == 0)
                        {
                            region[nx, ny] = regionCount;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }

                if (!touchesBorder && pixels.Count <= maxHoleArea)
                {
                    foreach (var (px, py) in pixels)
                    {
                        grid[px, py] = true;
                    }
                }
            }
        }
    }

    // Zhang and Suen, "A fast parallel algorithm for thinning digital patterns" (1984)
    private static void Thin(bool[,] grid, int width, int height)
    {
        var changed = true;
        var toRemove = new List<(int X, int Y)>();
        while (changed)
        {
            changed = false;
            for (var pass = 0; pass < 2; pass++)
            {
                toRemove.Clear();
                for (var y = 1; y < height - 1; y++)
                {
                    for (var x = 1; x < width - 1; x++)
                    {
                        if (!grid[x, y])
                            continue;

                        var p2 = grid[x, y - 1];
                        var p3 = grid[x + 1, y - 1];
                        var p4 = grid[x + 1, y];
                        var p5 = grid[x + 1, y + 1];
                        var p6 = grid[x, y + 1];
                        var p7 = grid[x - 1, y + 1];
                        var p8 = grid[x - 1, y];
                        var p9 = grid[x - 1, y - 1];
                        bool[] ring = [p2, p3, p4, p5, p6, p7, p8, p9];
                        var neighbors = ring.Count(v => v);
                        var transitions = 0;
                        for (var i = 0; i < 8; i++)
                        {
                            if (!ring[i] && ring[(i + 1) % 8])
                            {
                                transitions++;
                            }
                        }

                        if (neighbors is < 2 or > 6 || transitions != 1)
                            continue;

                        var remove = pass == 0
                            ? !(p2 && p4 && p6) && !(p4 && p6 && p8)
                            : !(p2 && p4 && p8) && !(p2 && p6 && p8);
                        if (remove)
                        {
                            toRemove.Add((x, y));
                        }
                    }
                }

                foreach (var (x, y) in toRemove)
                {
                    grid[x, y] = false;
                }

                changed |= toRemove.Count > 0;
            }
        }
    }
}

/// <summary>A chain of the skeleton, and whether each of its ends is a free end (rather than a junction or a loop).</summary>
internal sealed record TracedChain(List<(double X, double Y)> Points, bool StartIsEnd, bool EndIsEnd);
