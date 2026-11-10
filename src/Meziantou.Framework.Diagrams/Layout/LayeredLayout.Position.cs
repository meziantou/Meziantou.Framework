namespace Meziantou.Framework.Diagrams.Layout;

internal static partial class LayeredLayout
{
    private static void Position(LayoutGraph graph)
    {
        var layering = BuildLayerMatrix(graph);
        PositionY(graph, layering);
        var xs = PositionX(graph, layering);
        foreach (var (node, x) in xs)
        {
            node.X = x;
        }
    }

    private static void PositionY(LayoutGraph graph, List<List<LayoutNode>> layering)
    {
        var y = 0.0;
        foreach (var layer in layering)
        {
            var maxHeight = layer.Count is 0 ? 0 : layer.Max(node => node.Height);
            foreach (var node in layer)
            {
                node.Y = y + (maxHeight / 2);
            }

            y += maxHeight + graph.RankSeparation;
        }
    }

    /// <summary>
    /// The coordinate assignment of Brandes and Köpf: four alignments (up or down, left or right) of the nodes into vertical
    /// blocks, compacted horizontally, and balanced; the inner segments of the long edges stay straight.
    /// </summary>
    private static Dictionary<LayoutNode, double> PositionX(LayoutGraph graph, List<List<LayoutNode>> layering)
    {
        var conflicts = new HashSet<(int, int)>();
        FindType1Conflicts(layering, conflicts);
        FindType2Conflicts(layering, conflicts);

        var alignments = new List<Dictionary<LayoutNode, double>>(4);
        foreach (var vertical in new[] { true, false })
        {
            var adjusted = vertical ? layering : [.. Enumerable.Reverse(layering)];
            foreach (var right in new[] { false, true })
            {
                var layers = right ? adjusted.Select(layer => Enumerable.Reverse(layer).ToList()).ToList() : adjusted;
                var (root, align) = VerticalAlignment(layers, conflicts, useUpperNeighbors: vertical);
                var xs = HorizontalCompaction(graph, layers, root, align, right);
                if (right)
                {
                    foreach (var key in xs.Keys.ToList())
                    {
                        xs[key] = -xs[key];
                    }
                }

                alignments.Add(xs);
            }
        }

        var smallest = alignments.MinBy(xs =>
        {
            var max = double.NegativeInfinity;
            var min = double.PositiveInfinity;
            foreach (var (node, x) in xs)
            {
                max = Math.Max(max, x + (node.Width / 2));
                min = Math.Min(min, x - (node.Width / 2));
            }

            return max - min;
        })!;

        // Align the four layouts on the smallest one: the left alignments on its left edge, the right ones on its right edge
        var alignMin = smallest.Values.Min();
        var alignMax = smallest.Values.Max();
        for (var i = 0; i < alignments.Count; i++)
        {
            var xs = alignments[i];
            if (xs == smallest || xs.Count is 0)
                continue;

            var isLeft = i % 2 == 0;
            var delta = isLeft ? alignMin - xs.Values.Min() : alignMax - xs.Values.Max();
            if (delta != 0)
            {
                foreach (var key in xs.Keys.ToList())
                {
                    xs[key] += delta;
                }
            }
        }

        var result = new Dictionary<LayoutNode, double>();
        foreach (var node in alignments[0].Keys)
        {
            var values = alignments.Select(xs => xs[node]).Order().ToArray();
            result[node] = (values[1] + values[2]) / 2;
        }

        return result;
    }

    private static void AddConflict(HashSet<(int, int)> conflicts, LayoutNode v, LayoutNode w) => conflicts.Add(v.Id < w.Id ? (v.Id, w.Id) : (w.Id, v.Id));

    private static bool HasConflict(HashSet<(int, int)> conflicts, LayoutNode v, LayoutNode w) => conflicts.Contains(v.Id < w.Id ? (v.Id, w.Id) : (w.Id, v.Id));

    private static IEnumerable<LayoutNode> Predecessors(LayoutNode node) => node.InEdges.Select(edge => edge.V);

    private static IEnumerable<LayoutNode> Successors(LayoutNode node) => node.OutEdges.Select(edge => edge.W);

    /// <summary>Marks the segments that cross an inner segment (between two dummy nodes), which must not be aligned.</summary>
    private static void FindType1Conflicts(List<List<LayoutNode>> layering, HashSet<(int, int)> conflicts)
    {
        for (var layerIndex = 1; layerIndex < layering.Count; layerIndex++)
        {
            var previousLayer = layering[layerIndex - 1];
            var layer = layering[layerIndex];
            var k0 = 0;
            var scanPosition = 0;
            var lastNode = layer.Count > 0 ? layer[^1] : null;
            for (var i = 0; i < layer.Count; i++)
            {
                var v = layer[i];
                var w = v.IsDummy ? Predecessors(v).FirstOrDefault(u => u.IsDummy) : null;
                var k1 = w is not null ? w.Order : previousLayer.Count;
                if (w is not null || v == lastNode)
                {
                    for (var scan = scanPosition; scan <= i; scan++)
                    {
                        var scanNode = layer[scan];
                        foreach (var u in Predecessors(scanNode))
                        {
                            if ((u.Order < k0 || k1 < u.Order) && !(u.IsDummy && scanNode.IsDummy))
                            {
                                AddConflict(conflicts, u, scanNode);
                            }
                        }
                    }

                    scanPosition = i + 1;
                    k0 = k1;
                }
            }
        }
    }

    private static void FindType2Conflicts(List<List<LayoutNode>> layering, HashSet<(int, int)> conflicts)
    {
        for (var layerIndex = 1; layerIndex < layering.Count; layerIndex++)
        {
            var north = layering[layerIndex - 1];
            var south = layering[layerIndex];
            var previousNorthPosition = -1;
            var nextNorthPosition = 0;
            var southPosition = 0;
            for (var southLookahead = 0; southLookahead < south.Count; southLookahead++)
            {
                var v = south[southLookahead];
                if (v.Dummy is DummyKind.Border)
                {
                    var predecessor = Predecessors(v).FirstOrDefault();
                    if (predecessor is not null)
                    {
                        nextNorthPosition = predecessor.Order;
                        Scan(south, southPosition, southLookahead, previousNorthPosition, nextNorthPosition);
                        southPosition = southLookahead;
                        previousNorthPosition = nextNorthPosition;
                    }
                }

                Scan(south, southPosition, south.Count, nextNorthPosition, north.Count);
            }
        }

        void Scan(List<LayoutNode> south, int start, int end, int previousNorthBorder, int nextNorthBorder)
        {
            for (var i = start; i < end; i++)
            {
                var v = south[i];
                if (!v.IsDummy)
                    continue;

                foreach (var u in Predecessors(v))
                {
                    if (u.IsDummy && (u.Order < previousNorthBorder || u.Order > nextNorthBorder))
                    {
                        AddConflict(conflicts, u, v);
                    }
                }
            }
        }
    }

    private static (Dictionary<LayoutNode, LayoutNode> Root, Dictionary<LayoutNode, LayoutNode> Align) VerticalAlignment(List<List<LayoutNode>> layering, HashSet<(int, int)> conflicts, bool useUpperNeighbors)
    {
        var root = new Dictionary<LayoutNode, LayoutNode>();
        var align = new Dictionary<LayoutNode, LayoutNode>();
        var position = new Dictionary<LayoutNode, int>();
        foreach (var layer in layering)
        {
            for (var i = 0; i < layer.Count; i++)
            {
                root[layer[i]] = layer[i];
                align[layer[i]] = layer[i];
                position[layer[i]] = i;
            }
        }

        foreach (var layer in layering)
        {
            var previousIndex = -1;
            foreach (var v in layer)
            {
                var neighbors = (useUpperNeighbors ? Predecessors(v) : Successors(v)).Where(position.ContainsKey).Distinct().OrderBy(w => position[w]).ToList();
                if (neighbors.Count is 0)
                    continue;

                var middle = (neighbors.Count - 1) / 2.0;
                for (int i = (int)Math.Floor(middle), last = (int)Math.Ceiling(middle); i <= last; i++)
                {
                    var w = neighbors[i];
                    if (align[v] == v && previousIndex < position[w] && !HasConflict(conflicts, v, w))
                    {
                        align[w] = v;
                        align[v] = root[v] = root[w];
                        previousIndex = position[w];
                    }
                }
            }
        }

        return (root, align);
    }

    private static Dictionary<LayoutNode, double> HorizontalCompaction(LayoutGraph graph, List<List<LayoutNode>> layering, Dictionary<LayoutNode, LayoutNode> root, Dictionary<LayoutNode, LayoutNode> align, bool reverseSeparation)
    {
        // The block graph: an edge from the root of a block to the root of the next block of a layer, with their separation
        var blockNodes = new List<LayoutNode>();
        var blockNodeSet = new HashSet<LayoutNode>();
        var blockEdges = new Dictionary<(LayoutNode, LayoutNode), double>();
        var blockIn = new Dictionary<LayoutNode, List<LayoutNode>>();
        var blockOut = new Dictionary<LayoutNode, List<LayoutNode>>();
        foreach (var layer in layering)
        {
            LayoutNode? u = null;
            foreach (var v in layer)
            {
                var vRoot = root[v];
                if (blockNodeSet.Add(vRoot))
                {
                    blockNodes.Add(vRoot);
                    blockIn[vRoot] = [];
                    blockOut[vRoot] = [];
                }

                if (u is not null)
                {
                    var uRoot = root[u];
                    var separation = reverseSeparation ? Separation(graph, v, u) : Separation(graph, u, v);
                    if (blockEdges.TryGetValue((uRoot, vRoot), out var existing))
                    {
                        blockEdges[(uRoot, vRoot)] = Math.Max(existing, separation);
                    }
                    else
                    {
                        blockEdges[(uRoot, vRoot)] = separation;
                        blockOut[uRoot].Add(vRoot);
                        blockIn[vRoot].Add(uRoot);
                    }
                }

                u = v;
            }
        }

        var xs = new Dictionary<LayoutNode, double>();
        Iterate(node => blockIn[node], node =>
        {
            var value = 0.0;
            foreach (var predecessor in blockIn[node])
            {
                value = Math.Max(value, xs.GetValueOrDefault(predecessor) + blockEdges[(predecessor, node)]);
            }

            xs[node] = value;
        });

        var borderType = reverseSeparation ? BorderSide.Left : BorderSide.Right;
        Iterate(node => blockOut[node], node =>
        {
            var min = double.PositiveInfinity;
            foreach (var successor in blockOut[node])
            {
                min = Math.Min(min, xs.GetValueOrDefault(successor) - blockEdges[(node, successor)]);
            }

            if (!double.IsPositiveInfinity(min) && node.BorderType != borderType)
            {
                xs[node] = Math.Max(xs.GetValueOrDefault(node), min);
            }
        });

        var result = new Dictionary<LayoutNode, double>();
        foreach (var v in align.Keys)
        {
            result[v] = xs.GetValueOrDefault(root[v]);
        }

        return result;

        void Iterate(Func<LayoutNode, List<LayoutNode>> next, Action<LayoutNode> setX)
        {
            var stack = new List<LayoutNode>(blockNodes);
            var visited = new HashSet<LayoutNode>();
            while (stack.Count > 0)
            {
                var element = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                if (visited.Contains(element))
                {
                    setX(element);
                }
                else
                {
                    visited.Add(element);
                    stack.Add(element);
                    stack.AddRange(next(element));
                }
            }
        }
    }

    /// <summary>The distance between the centers of two neighbors of a rank, <paramref name="left"/> on the left of <paramref name="right"/>.</summary>
    private static double Separation(LayoutGraph graph, LayoutNode left, LayoutNode right)
    {
        return (left.Width / 2) + RightGap(left) + LeftGap(right) + (right.Width / 2);

        // The half gap on a side of a node: inside a cluster, its padding; around a node, half the node separation; around an edge, half the edge separation
        double RightGap(LayoutNode node) => node.BorderType switch
        {
            BorderSide.Left => graph.ClusterPadding,
            BorderSide.Right => graph.NodeSeparation / 2,
            _ => node.IsDummy ? graph.EdgeSeparation / 2 : graph.NodeSeparation / 2,
        };

        double LeftGap(LayoutNode node) => node.BorderType switch
        {
            BorderSide.Right => graph.ClusterPadding,
            BorderSide.Left => graph.NodeSeparation / 2,
            _ => node.IsDummy ? graph.EdgeSeparation / 2 : graph.NodeSeparation / 2,
        };
    }
}
