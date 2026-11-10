namespace Meziantou.Framework.Diagrams.Layout;

internal static partial class LayeredLayout
{
    /// <summary>
    /// Orders the nodes of each rank to reduce the crossings: sweeps down and up, sorting each rank by the barycenter of
    /// the neighbors of its nodes in the previous rank, the nodes of a cluster kept together; the best ordering wins.
    /// </summary>
    private static void Order(LayoutGraph graph)
    {
        var maxRank = graph.MaxRank;
        var downLayers = new List<LayerGraph>();
        for (var rank = 1; rank <= maxRank; rank++)
        {
            downLayers.Add(BuildLayerGraph(graph, rank, useInEdges: true));
        }

        var upLayers = new List<LayerGraph>();
        for (var rank = maxRank - 1; rank >= 0; rank--)
        {
            upLayers.Add(BuildLayerGraph(graph, rank, useInEdges: false));
        }

        var layering = InitOrder(graph);
        AssignOrder(layering);

        var bestCrossings = long.MaxValue;
        List<List<LayoutNode>>? best = null;
        for (int i = 0, lastBest = 0; lastBest < 4; i++, lastBest++)
        {
            SweepLayerGraphs(i % 2 == 1 ? downLayers : upLayers, biasRight: i % 4 >= 2);
            layering = BuildLayerMatrix(graph);
            var crossings = CrossCount(layering);
            if (crossings < bestCrossings)
            {
                lastBest = 0;
                best = [.. layering.Select(layer => layer.ToList())];
                bestCrossings = crossings;
            }

            // A safety net: the crossings decrease at each improvement, but keep the number of sweeps bounded
            if (i > 200)
                break;
        }

        if (best is not null)
        {
            AssignOrder(best);
        }
    }

    private static void AssignOrder(List<List<LayoutNode>> layering)
    {
        foreach (var layer in layering)
        {
            for (var i = 0; i < layer.Count; i++)
            {
                layer[i].Order = i;
            }
        }
    }

    /// <summary>Orders the nodes by a depth-first search from the nodes of the lowest ranks.</summary>
    private static List<List<LayoutNode>> InitOrder(LayoutGraph graph)
    {
        var simpleNodes = graph.Nodes.Where(node => !node.IsCluster).ToList();
        var maxRank = simpleNodes.Count is 0 ? -1 : simpleNodes.Max(node => node.Rank);
        var layers = new List<List<LayoutNode>>();
        for (var i = 0; i <= maxRank; i++)
        {
            layers.Add([]);
        }

        var visited = new HashSet<LayoutNode>();
        var stack = new Stack<LayoutNode>();
        foreach (var start in simpleNodes.OrderBy(node => node.Rank))
        {
            stack.Push(start);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (!visited.Add(node))
                    continue;

                layers[node.Rank].Add(node);
                for (var i = node.OutEdges.Count - 1; i >= 0; i--)
                {
                    var successor = node.OutEdges[i].W;
                    if (!visited.Contains(successor))
                    {
                        stack.Push(successor);
                    }
                }
            }
        }

        return layers;
    }

    /// <summary>The nodes of a rank, the clusters that span it, and the weighted edges from the adjacent rank.</summary>
    private sealed class LayerGraph
    {
        public Dictionary<LayoutNode, List<LayoutNode>> Children { get; } = [];

        public List<LayoutNode> RootChildren { get; } = [];

        public Dictionary<LayoutNode, LayoutNode?> Parents { get; } = [];

        public Dictionary<LayoutNode, (LayoutNode Left, LayoutNode Right)> Borders { get; } = [];

        public Dictionary<LayoutNode, List<(LayoutNode Node, double Weight)>> InEdges { get; } = [];

        public List<LayoutNode> GetChildren(LayoutNode? node) => node is null ? RootChildren : Children.TryGetValue(node, out var children) ? children : [];
    }

    private static LayerGraph BuildLayerGraph(LayoutGraph graph, int rank, bool useInEdges)
    {
        var result = new LayerGraph();
        foreach (var node in graph.Nodes)
        {
            var inRank = node.IsCluster ? node.HasRankRange && node.MinRank <= rank && rank <= node.MaxRank : node.Rank == rank;
            if (!inRank)
                continue;

            result.Parents[node] = node.Parent;
            if (node.Parent is null)
            {
                result.RootChildren.Add(node);
            }
            else
            {
                if (!result.Children.TryGetValue(node.Parent, out var siblings))
                {
                    result.Children[node.Parent] = siblings = [];
                }

                siblings.Add(node);
            }

            if (!node.IsCluster)
            {
                var weights = new List<(LayoutNode Node, double Weight)>();
                foreach (var edge in useInEdges ? node.InEdges : node.OutEdges)
                {
                    var other = useInEdges ? edge.V : edge.W;
                    var index = weights.FindIndex(item => item.Node == other);
                    if (index >= 0)
                    {
                        weights[index] = (other, weights[index].Weight + edge.Weight);
                    }
                    else
                    {
                        weights.Add((other, edge.Weight));
                    }
                }

                result.InEdges[node] = weights;
            }

            if (node.HasRankRange && node.BorderLeft!.TryGetValue(rank, out var left) && node.BorderRight!.TryGetValue(rank, out var right))
            {
                result.Borders[node] = (left, right);
            }
        }

        return result;
    }

    private static void SweepLayerGraphs(List<LayerGraph> layerGraphs, bool biasRight)
    {
        var constraints = new ConstraintGraph();
        foreach (var layerGraph in layerGraphs)
        {
            var sorted = SortSubgraph(layerGraph, null, constraints, biasRight);
            for (var i = 0; i < sorted.Nodes.Count; i++)
            {
                sorted.Nodes[i].Order = i;
            }

            AddSubgraphConstraints(layerGraph, constraints, sorted.Nodes);
        }
    }

    private sealed class ConstraintGraph
    {
        public List<(LayoutNode V, LayoutNode W)> Edges { get; } = [];

        private readonly HashSet<(LayoutNode, LayoutNode)> _set = [];

        public void Add(LayoutNode v, LayoutNode w)
        {
            if (_set.Add((v, w)))
            {
                Edges.Add((v, w));
            }
        }
    }

    private static void AddSubgraphConstraints(LayerGraph layerGraph, ConstraintGraph constraints, List<LayoutNode> nodes)
    {
        var previous = new Dictionary<LayoutNode, LayoutNode>();
        LayoutNode? rootPrevious = null;
        foreach (var node in nodes)
        {
            var child = layerGraph.Parents.GetValueOrDefault(node);
            while (child is not null)
            {
                var parent = layerGraph.Parents.GetValueOrDefault(child);
                LayoutNode? previousChild;
                if (parent is not null)
                {
                    previous.TryGetValue(parent, out previousChild);
                    previous[parent] = child;
                }
                else
                {
                    previousChild = rootPrevious;
                    rootPrevious = child;
                }

                if (previousChild is not null && previousChild != child)
                {
                    constraints.Add(previousChild, child);
                    break;
                }

                child = parent;
            }
        }
    }

    private sealed class SortResult
    {
        public List<LayoutNode> Nodes { get; set; } = [];

        public double? Barycenter { get; set; }

        public double Weight { get; set; }
    }

    private sealed class Entry
    {
        public required LayoutNode Node { get; init; }

        public List<LayoutNode> Nodes { get; set; } = [];

        public double? Barycenter { get; set; }

        public double Weight { get; set; }

        public int Index { get; set; }

        public int InDegree { get; set; }

        public List<Entry> In { get; } = [];

        public List<Entry> Out { get; } = [];

        public bool Merged { get; set; }
    }

    private static SortResult SortSubgraph(LayerGraph layerGraph, LayoutNode? cluster, ConstraintGraph constraints, bool biasRight)
    {
        var movable = layerGraph.GetChildren(cluster);
        (LayoutNode Left, LayoutNode Right)? borders = cluster is not null && layerGraph.Borders.TryGetValue(cluster, out var value) ? value : null;
        if (borders is { } b)
        {
            movable = [.. movable.Where(node => node != b.Left && node != b.Right)];
        }

        var subgraphs = new Dictionary<LayoutNode, SortResult>();
        var entries = new List<Entry>(movable.Count);
        foreach (var node in movable)
        {
            var entry = new Entry { Node = node, Nodes = [node], Index = entries.Count };
            if (layerGraph.InEdges.TryGetValue(node, out var inEdges) && inEdges.Count > 0)
            {
                var sum = 0.0;
                var weight = 0.0;
                foreach (var (other, edgeWeight) in inEdges)
                {
                    sum += edgeWeight * other.Order;
                    weight += edgeWeight;
                }

                entry.Barycenter = weight == 0 ? null : sum / weight;
                entry.Weight = weight;
            }

            if (node.IsCluster && layerGraph.GetChildren(node).Count > 0)
            {
                var subgraphResult = SortSubgraph(layerGraph, node, constraints, biasRight);
                subgraphs[node] = subgraphResult;
                if (subgraphResult.Barycenter is { } subBarycenter)
                {
                    if (entry.Barycenter is { } barycenter)
                    {
                        entry.Barycenter = ((barycenter * entry.Weight) + (subBarycenter * subgraphResult.Weight)) / (entry.Weight + subgraphResult.Weight);
                        entry.Weight += subgraphResult.Weight;
                    }
                    else
                    {
                        entry.Barycenter = subBarycenter;
                        entry.Weight = subgraphResult.Weight;
                    }
                }
            }

            entries.Add(entry);
        }

        var resolved = ResolveConflicts(entries, constraints);
        foreach (var entry in resolved)
        {
            entry.Nodes = [.. entry.Nodes.SelectMany(node => subgraphs.TryGetValue(node, out var sub) ? sub.Nodes : [node])];
        }

        var result = Sort(resolved, biasRight);
        if (borders is { } border)
        {
            result.Nodes = [border.Left, .. result.Nodes, border.Right];
            var leftPredecessors = layerGraph.InEdges.GetValueOrDefault(border.Left);
            var rightPredecessors = layerGraph.InEdges.GetValueOrDefault(border.Right);
            if (leftPredecessors is { Count: > 0 } && rightPredecessors is { Count: > 0 })
            {
                var barycenter = result.Barycenter ?? 0;
                var weight = result.Barycenter is null ? 0 : result.Weight;
                result.Barycenter = ((barycenter * weight) + leftPredecessors[0].Node.Order + rightPredecessors[0].Node.Order) / (weight + 2);
                result.Weight = weight + 2;
            }
        }

        return result;
    }

    private static List<Entry> ResolveConflicts(List<Entry> entries, ConstraintGraph constraints)
    {
        var mapped = new Dictionary<LayoutNode, Entry>(entries.Count);
        foreach (var entry in entries)
        {
            mapped[entry.Node] = entry;
        }

        foreach (var (v, w) in constraints.Edges)
        {
            if (mapped.TryGetValue(v, out var entryV) && mapped.TryGetValue(w, out var entryW))
            {
                entryW.InDegree++;
                entryV.Out.Add(entryW);
            }
        }

        var sources = new List<Entry>(entries.Where(entry => entry.InDegree == 0));
        var result = new List<Entry>();
        while (sources.Count > 0)
        {
            var entry = sources[^1];
            sources.RemoveAt(sources.Count - 1);
            result.Add(entry);
            for (var i = entry.In.Count - 1; i >= 0; i--)
            {
                var uEntry = entry.In[i];
                if (uEntry.Merged)
                    continue;

                if (uEntry.Barycenter is null || entry.Barycenter is null || uEntry.Barycenter >= entry.Barycenter)
                {
                    MergeEntries(entry, uEntry);
                }
            }

            foreach (var wEntry in entry.Out)
            {
                wEntry.In.Add(entry);
                if (--wEntry.InDegree == 0)
                {
                    sources.Add(wEntry);
                }
            }
        }

        // The entries of a cycle of constraints, which should not happen, keep their place
        foreach (var entry in entries)
        {
            if (entry.InDegree > 0 && !result.Contains(entry))
            {
                result.Add(entry);
            }
        }

        return [.. result.Where(entry => !entry.Merged)];
    }

    private static void MergeEntries(Entry target, Entry source)
    {
        var sum = 0.0;
        var weight = 0.0;
        if (target.Weight != 0 && target.Barycenter is { } targetBarycenter)
        {
            sum += targetBarycenter * target.Weight;
            weight += target.Weight;
        }

        if (source.Weight != 0 && source.Barycenter is { } sourceBarycenter)
        {
            sum += sourceBarycenter * source.Weight;
            weight += source.Weight;
        }

        target.Nodes = [.. source.Nodes, .. target.Nodes];
        target.Barycenter = weight == 0 ? null : sum / weight;
        target.Weight = weight;
        target.Index = Math.Min(source.Index, target.Index);
        source.Merged = true;
    }

    private static SortResult Sort(List<Entry> entries, bool biasRight)
    {
        var sortable = entries.Where(entry => entry.Barycenter is not null).ToList();
        var unsortable = entries.Where(entry => entry.Barycenter is null).OrderByDescending(entry => entry.Index).ToList();
        sortable.Sort((a, b) =>
        {
            var comparison = a.Barycenter!.Value.CompareTo(b.Barycenter!.Value);
            if (comparison != 0)
                return comparison;

            return biasRight ? b.Index.CompareTo(a.Index) : a.Index.CompareTo(b.Index);
        });

        var nodes = new List<LayoutNode>();
        var sum = 0.0;
        var weight = 0.0;
        var index = ConsumeUnsortable(nodes, unsortable, 0);
        foreach (var entry in sortable)
        {
            index += entry.Nodes.Count;
            nodes.AddRange(entry.Nodes);
            sum += entry.Barycenter!.Value * entry.Weight;
            weight += entry.Weight;
            index = ConsumeUnsortable(nodes, unsortable, index);
        }

        // The entries of an index past the end
        while (unsortable.Count > 0)
        {
            nodes.AddRange(unsortable[^1].Nodes);
            unsortable.RemoveAt(unsortable.Count - 1);
        }

        var result = new SortResult { Nodes = nodes };
        if (weight != 0)
        {
            result.Barycenter = sum / weight;
            result.Weight = weight;
        }

        return result;
    }

    private static int ConsumeUnsortable(List<LayoutNode> nodes, List<Entry> unsortable, int index)
    {
        while (unsortable.Count > 0 && unsortable[^1].Index <= index)
        {
            nodes.AddRange(unsortable[^1].Nodes);
            unsortable.RemoveAt(unsortable.Count - 1);
            index++;
        }

        return index;
    }

    /// <summary>Counts the weighted crossings between each pair of adjacent ranks, with the accumulator tree of Barth et al.</summary>
    private static long CrossCount(List<List<LayoutNode>> layering)
    {
        long crossings = 0;
        for (var i = 1; i < layering.Count; i++)
        {
            crossings += TwoLayerCrossCount(layering[i - 1], layering[i]);
        }

        return crossings;
    }

    private static long TwoLayerCrossCount(List<LayoutNode> north, List<LayoutNode> south)
    {
        var southPosition = new Dictionary<LayoutNode, int>(south.Count);
        for (var i = 0; i < south.Count; i++)
        {
            southPosition[south[i]] = i;
        }

        var entries = new List<(int Position, double Weight)>();
        foreach (var node in north)
        {
            var start = entries.Count;
            foreach (var edge in node.OutEdges)
            {
                if (southPosition.TryGetValue(edge.W, out var position))
                {
                    entries.Add((position, edge.Weight));
                }
            }

            entries.Sort(start, entries.Count - start, Comparer<(int Position, double Weight)>.Create((a, b) => a.Position.CompareTo(b.Position)));
        }

        var firstIndex = 1;
        while (firstIndex < south.Count)
        {
            firstIndex <<= 1;
        }

        var treeSize = (2 * firstIndex) - 1;
        firstIndex--;
        var tree = new double[treeSize];
        var count = 0.0;
        foreach (var (position, weight) in entries)
        {
            var index = position + firstIndex;
            tree[index] += weight;
            var weightSum = 0.0;
            while (index > 0)
            {
                if (index % 2 == 1)
                {
                    weightSum += tree[index + 1];
                }

                index = (index - 1) >> 1;
                tree[index] += weight;
            }

            count += weight * weightSum;
        }

        return (long)Math.Min(count, long.MaxValue / 4);
    }
}
