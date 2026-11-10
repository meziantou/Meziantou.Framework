namespace Meziantou.Framework.Diagrams.Layout;

/// <summary>
/// Lays a compound directed graph out in layers (Sugiyama): the algorithm of dagre (MIT license), which Mermaid uses
/// for its flowcharts. The steps are the ones of dagre: cycle removal, nesting graph, network simplex ranking, dummy
/// nodes for the long edges and the edge labels, crossing minimization by barycenters with the clusters kept together,
/// and the coordinate assignment of Brandes and Köpf.
/// </summary>
internal static partial class LayeredLayout
{
    public static void Run(LayoutGraph graph)
    {
        if (graph.Nodes.Count is 0)
            return;

        // As in dagre, a cluster without children is laid out as a node
        foreach (var node in graph.Nodes)
        {
            if (node.IsCluster && node.Children.Count is 0)
            {
                node.IsCluster = false;
            }
        }

        MakeSpaceForEdgeLabels(graph);
        RemoveSelfEdges(graph);
        Acyclic(graph);
        NestingGraphRun(graph);
        Rank(graph);
        InjectEdgeLabelProxies(graph);
        RemoveEmptyRanks(graph);
        NestingGraphCleanup(graph);
        NormalizeRanks(graph);
        AssignRankMinMax(graph);
        RemoveEdgeLabelProxies(graph);
        Normalize(graph);
        ParentDummyChains(graph);
        AddBorderSegments(graph);
        Order(graph);
        InsertSelfEdges(graph);
        AdjustCoordinateSystem(graph);
        SetBorderSizes(graph);
        Position(graph);
        PositionSelfEdges(graph);
        RemoveBorderNodes(graph);
        Denormalize(graph);
        UndoCoordinateSystem(graph);
        TranslateGraph(graph);
        ReversePoints(graph);
        UndoAcyclic(graph);
    }

    private static bool IsHorizontal(LayoutGraph graph) => graph.Direction is LayoutDirection.LeftToRight or LayoutDirection.RightToLeft;

    private static void MakeSpaceForEdgeLabels(LayoutGraph graph)
    {
        graph.RankSeparation /= 2;
        foreach (var edge in graph.Edges)
        {
            edge.MinLength *= 2;
        }
    }

    private static void RemoveSelfEdges(LayoutGraph graph)
    {
        foreach (var edge in graph.Edges.ToArray())
        {
            if (edge.V == edge.W)
            {
                (edge.V.SelfEdges ??= []).Add(edge);
                graph.RemoveEdge(edge);
            }
        }
    }

    /// <summary>Reverses the edges that close a cycle, found by a depth-first search.</summary>
    private static void Acyclic(LayoutGraph graph)
    {
        var visited = new HashSet<LayoutNode>();
        var onStack = new HashSet<LayoutNode>();
        var feedbackEdges = new List<LayoutEdge>();
        var stack = new Stack<(LayoutNode Node, int Index)>();
        foreach (var start in graph.Nodes.ToArray())
        {
            if (!visited.Add(start))
                continue;

            onStack.Add(start);
            stack.Push((start, 0));
            while (stack.Count > 0)
            {
                var (node, index) = stack.Pop();
                if (index < node.OutEdges.Count)
                {
                    stack.Push((node, index + 1));
                    var edge = node.OutEdges[index];
                    if (onStack.Contains(edge.W))
                    {
                        feedbackEdges.Add(edge);
                    }
                    else if (visited.Add(edge.W))
                    {
                        onStack.Add(edge.W);
                        stack.Push((edge.W, 0));
                    }

                    continue;
                }

                onStack.Remove(node);
            }
        }

        foreach (var edge in feedbackEdges)
        {
            graph.RemoveEdge(edge);
            (edge.V, edge.W) = (edge.W, edge.V);
            edge.Reversed = true;
            graph.AddEdge(edge);
        }
    }

    private static void UndoAcyclic(LayoutGraph graph)
    {
        foreach (var edge in graph.Edges.ToArray())
        {
            if (edge.Reversed)
            {
                graph.RemoveEdge(edge);
                (edge.V, edge.W) = (edge.W, edge.V);
                edge.Reversed = false;
                graph.AddEdge(edge);
            }
        }
    }

    /// <summary>
    /// Adds a top and a bottom border node to every cluster, linked to the content of the cluster, and a root node linked to
    /// everything, so the ranking keeps the clusters together and the graph connected.
    /// </summary>
    private static void NestingGraphRun(LayoutGraph graph)
    {
        var root = graph.AddDummy(DummyKind.Root);
        graph.NestingRoot = root;
        var depths = new Dictionary<LayoutNode, int>();
        var topLevel = graph.GetChildren(null).Where(node => node != root).ToList();
        foreach (var node in topLevel)
        {
            TreeDepths(node, 1);
        }

        var height = depths.Count is 0 ? 0 : depths.Values.Max() - 1;
        var nodeSeparation = (2 * height) + 1;
        graph.NodeRankFactor = nodeSeparation;
        foreach (var edge in graph.Edges)
        {
            edge.MinLength *= nodeSeparation;
        }

        var weight = graph.Edges.Sum(edge => edge.Weight) + 1;
        foreach (var node in topLevel)
        {
            Visit(node);
        }

        void TreeDepths(LayoutNode node, int depth)
        {
            foreach (var child in node.Children)
            {
                TreeDepths(child, depth + 1);
            }

            depths[node] = depth;
        }

        void Visit(LayoutNode node)
        {
            if (!node.IsCluster)
            {
                AddNestingEdge(root, node, 0, nodeSeparation);
                return;
            }

            var top = graph.AddDummy(DummyKind.Border);
            var bottom = graph.AddDummy(DummyKind.Border);
            LayoutGraph.SetParent(top, node);
            LayoutGraph.SetParent(bottom, node);
            node.BorderTop = top;
            node.BorderBottom = bottom;
            foreach (var child in node.Children.ToArray())
            {
                if (child == top || child == bottom)
                    continue;

                Visit(child);
                var childTop = child.BorderTop ?? child;
                var childBottom = child.BorderBottom ?? child;
                var thisWeight = child.BorderTop is not null ? weight : 2 * weight;
                var minLength = childTop != childBottom ? 1 : height - depths[node] + 1;
                AddNestingEdge(top, childTop, thisWeight, minLength);
                AddNestingEdge(childBottom, bottom, thisWeight, minLength);
            }

            if (node.Parent is null)
            {
                AddNestingEdge(root, top, 0, height + depths[node]);
            }
        }

        void AddNestingEdge(LayoutNode v, LayoutNode w, double edgeWeight, int minLength)
        {
            var edge = graph.AddEdge(v, w);
            edge.Weight = edgeWeight;
            edge.MinLength = minLength;
            edge.NestingEdge = true;
        }
    }

    private static void NestingGraphCleanup(LayoutGraph graph)
    {
        if (graph.NestingRoot is { } root)
        {
            graph.RemoveNode(root);
            graph.NestingRoot = null;
        }

        foreach (var edge in graph.Edges.ToArray())
        {
            if (edge.NestingEdge)
            {
                graph.RemoveEdge(edge);
            }
        }
    }

    private static void Rank(LayoutGraph graph)
    {
        var nodes = graph.Nodes.Where(node => !node.IsCluster).ToList();
        var index = new Dictionary<LayoutNode, int>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            index[nodes[i]] = i;
        }

        var edges = new List<(int, int, int, double)>(graph.Edges.Count);
        foreach (var edge in graph.Edges)
        {
            if (index.TryGetValue(edge.V, out var v) && index.TryGetValue(edge.W, out var w))
            {
                edges.Add((v, w, edge.MinLength, edge.Weight));
            }
        }

        var ranks = NetworkSimplex.Run(nodes.Count, edges);
        for (var i = 0; i < nodes.Count; i++)
        {
            nodes[i].Rank = ranks[i];
        }
    }

    private static void InjectEdgeLabelProxies(LayoutGraph graph)
    {
        foreach (var edge in graph.Edges.ToArray())
        {
            if (edge.HasLabel)
            {
                var proxy = graph.AddDummy(DummyKind.EdgeProxy);
                proxy.Rank = edge.V.Rank + ((edge.W.Rank - edge.V.Rank) / 2);
                proxy.EdgeObject = edge;
            }
        }
    }

    private static void RemoveEdgeLabelProxies(LayoutGraph graph)
    {
        foreach (var node in graph.Nodes.ToArray())
        {
            if (node.Dummy is DummyKind.EdgeProxy)
            {
                node.EdgeObject!.LabelRank = node.Rank;
                graph.RemoveNode(node);
            }
        }
    }

    /// <summary>Removes the ranks that hold no node, except the ranks of the borders of the clusters.</summary>
    private static void RemoveEmptyRanks(LayoutGraph graph)
    {
        var ranked = graph.Nodes.Where(node => !node.IsCluster).ToList();
        if (ranked.Count is 0)
            return;

        var offset = ranked.Min(node => node.Rank);
        var layers = new SortedDictionary<int, List<LayoutNode>>();
        foreach (var node in ranked)
        {
            var rank = node.Rank - offset;
            if (!layers.TryGetValue(rank, out var layer))
            {
                layers[rank] = layer = [];
            }

            layer.Add(node);
        }

        var maxRank = layers.Keys.Max();
        var delta = 0;
        for (var rank = 0; rank <= maxRank; rank++)
        {
            if (!layers.TryGetValue(rank, out var layer))
            {
                if (rank % graph.NodeRankFactor != 0)
                {
                    delta--;
                }
            }
            else if (delta != 0)
            {
                foreach (var node in layer)
                {
                    node.Rank += delta;
                }
            }
        }
    }

    private static void NormalizeRanks(LayoutGraph graph)
    {
        var ranked = graph.Nodes.Where(node => !node.IsCluster).ToList();
        if (ranked.Count is 0)
            return;

        var min = ranked.Min(node => node.Rank);
        foreach (var node in ranked)
        {
            node.Rank -= min;
        }
    }

    private static void AssignRankMinMax(LayoutGraph graph)
    {
        var maxRank = 0;
        foreach (var node in graph.Nodes)
        {
            if (node.BorderTop is { } top && node.BorderBottom is { } bottom)
            {
                node.MinRank = top.Rank;
                node.MaxRank = bottom.Rank;
                node.HasRankRange = true;
                maxRank = Math.Max(maxRank, node.MaxRank);
            }
            else if (!node.IsCluster)
            {
                maxRank = Math.Max(maxRank, node.Rank);
            }
        }

        graph.MaxRank = maxRank;
    }

    /// <summary>Replaces the edges longer than one rank by chains of dummy nodes; the label of an edge becomes the dummy of its label rank.</summary>
    private static void Normalize(LayoutGraph graph)
    {
        graph.DummyChains.Clear();
        foreach (var edge in graph.Edges.ToArray())
        {
            var v = edge.V;
            var vRank = v.Rank;
            var w = edge.W;
            var wRank = w.Rank;
            if (wRank == vRank + 1)
                continue;

            graph.RemoveEdge(edge);
            edge.Points = [];
            var first = true;
            for (vRank++; vRank < wRank; vRank++)
            {
                var dummy = graph.AddDummy(DummyKind.Edge);
                dummy.Rank = vRank;
                dummy.EdgeObject = edge;
                if (vRank == edge.LabelRank)
                {
                    dummy.Width = edge.LabelWidth;
                    dummy.Height = edge.LabelHeight;
                    dummy.Dummy = DummyKind.EdgeLabel;
                }

                graph.AddEdge(v, dummy).Weight = edge.Weight;
                if (first)
                {
                    graph.DummyChains.Add(dummy);
                    first = false;
                }

                v = dummy;
            }

            graph.AddEdge(v, w).Weight = edge.Weight;
        }
    }

    private static void Denormalize(LayoutGraph graph)
    {
        foreach (var start in graph.DummyChains)
        {
            var node = start;
            var edge = node.EdgeObject!;
            graph.AddEdge(edge);
            while (node.IsDummy && node.EdgeObject == edge)
            {
                edge.Points.Add(new LayoutPoint(node.X, node.Y));
                if (node.Dummy is DummyKind.EdgeLabel)
                {
                    edge.LabelX = node.X;
                    edge.LabelY = node.Y;
                }

                var next = node.OutEdges.Count > 0 ? node.OutEdges[0].W : null;
                graph.RemoveNode(node);
                if (next is null)
                    break;

                node = next;
            }
        }

        graph.DummyChains.Clear();
    }

    /// <summary>Moves the dummy nodes of the edges into the clusters they cross, so the clusters stay together.</summary>
    private static void ParentDummyChains(LayoutGraph graph)
    {
        var postorder = new Dictionary<LayoutNode, (int Low, int Lim)>();
        var lim = 0;
        foreach (var node in graph.GetChildren(null).ToList())
        {
            PostOrder(node);
        }

        foreach (var start in graph.DummyChains)
        {
            var edge = start.EdgeObject!;
            var (path, lca) = FindPath(edge.V, edge.W);
            var pathIndex = 0;
            var ascending = true;
            var v = start;
            while (v != edge.W && v.IsDummy && v.EdgeObject == edge)
            {
                if (ascending)
                {
                    while (pathIndex < path.Count && path[pathIndex] != lca && path[pathIndex]!.MaxRank < v.Rank)
                    {
                        pathIndex++;
                    }

                    if (pathIndex >= path.Count || path[pathIndex] == lca)
                    {
                        ascending = false;
                    }
                }

                if (!ascending)
                {
                    while (pathIndex < path.Count - 1 && path[pathIndex + 1]!.MinRank <= v.Rank)
                    {
                        pathIndex++;
                    }
                }

                var parent = pathIndex < path.Count ? path[pathIndex] : lca;
                LayoutGraph.SetParent(v, parent);
                if (v.OutEdges.Count is 0)
                    break;

                v = v.OutEdges[0].W;
            }
        }

        void PostOrder(LayoutNode node)
        {
            var low = lim;
            foreach (var child in node.Children)
            {
                PostOrder(child);
            }

            postorder[node] = (low, lim++);
        }

        (List<LayoutNode?> Path, LayoutNode? Lca) FindPath(LayoutNode v, LayoutNode w)
        {
            var vPath = new List<LayoutNode?>();
            var wPath = new List<LayoutNode?>();
            var low = Math.Min(postorder[v].Low, postorder[w].Low);
            var maxLim = Math.Max(postorder[v].Lim, postorder[w].Lim);
            LayoutNode? parent = v;
            do
            {
                parent = parent.Parent;
                vPath.Add(parent);
            }
            while (parent is not null && (postorder[parent].Low > low || maxLim > postorder[parent].Lim));

            var lca = parent;
            parent = w;
            while ((parent = parent.Parent) != lca && parent is not null)
            {
                wPath.Add(parent);
            }

            wPath.Reverse();
            vPath.AddRange(wPath);
            return (vPath, lca);
        }
    }

    /// <summary>Adds a left and a right border node to each cluster on each of its ranks.</summary>
    private static void AddBorderSegments(LayoutGraph graph)
    {
        foreach (var node in graph.GetChildren(null).ToList())
        {
            Visit(node);
        }

        void Visit(LayoutNode node)
        {
            foreach (var child in node.Children.ToList())
            {
                Visit(child);
            }

            if (!node.HasRankRange)
                return;

            node.BorderLeft = [];
            node.BorderRight = [];
            for (var rank = node.MinRank; rank <= node.MaxRank; rank++)
            {
                AddBorderNode(node, node.BorderLeft, BorderSide.Left, rank);
                AddBorderNode(node, node.BorderRight, BorderSide.Right, rank);
            }
        }

        void AddBorderNode(LayoutNode cluster, Dictionary<int, LayoutNode> borders, BorderSide side, int rank)
        {
            var border = graph.AddDummy(DummyKind.Border);
            border.Rank = rank;
            border.BorderType = side;
            LayoutGraph.SetParent(border, cluster);
            if (borders.TryGetValue(rank - 1, out var previous))
            {
                graph.AddEdge(previous, border);
            }

            borders[rank] = border;
        }
    }

    private static void InsertSelfEdges(LayoutGraph graph)
    {
        foreach (var layer in BuildLayerMatrix(graph))
        {
            var orderShift = 0;
            for (var i = 0; i < layer.Count; i++)
            {
                var node = layer[i];
                node.Order = i + orderShift;
                if (node.SelfEdges is null)
                    continue;

                foreach (var selfEdge in node.SelfEdges)
                {
                    var dummy = graph.AddDummy(DummyKind.SelfEdge, selfEdge.LabelWidth, selfEdge.LabelHeight);
                    dummy.Rank = node.Rank;
                    dummy.Order = i + ++orderShift;
                    dummy.EdgeObject = selfEdge;
                    LayoutGraph.SetParent(dummy, node.Parent);
                }

                node.SelfEdges = null;
            }
        }
    }

    private static void PositionSelfEdges(LayoutGraph graph)
    {
        foreach (var node in graph.Nodes.ToArray())
        {
            if (node.Dummy is not DummyKind.SelfEdge)
                continue;

            var edge = node.EdgeObject!;
            var self = edge.V;
            var x = self.X + (self.Width / 2);
            var y = self.Y;
            var dx = node.X - x;
            var dy = self.Height / 2;
            graph.AddEdge(edge);
            graph.RemoveNode(node);
            edge.Points =
            [
                new(x + (2 * dx / 3), y - dy),
                new(x + (5 * dx / 6), y - dy),
                new(x + dx, y),
                new(x + (5 * dx / 6), y + dy),
                new(x + (2 * dx / 3), y + dy),
            ];
            edge.LabelX = node.X;
            edge.LabelY = node.Y;
        }
    }

    private static void AdjustCoordinateSystem(LayoutGraph graph)
    {
        if (IsHorizontal(graph))
        {
            SwapWidthHeight(graph);
        }
    }

    private static void UndoCoordinateSystem(LayoutGraph graph)
    {
        if (graph.Direction is LayoutDirection.BottomToTop or LayoutDirection.RightToLeft)
        {
            foreach (var node in graph.Nodes)
            {
                node.Y = -node.Y;
            }

            foreach (var edge in graph.Edges)
            {
                edge.Points = [.. edge.Points.Select(point => point with { Y = -point.Y })];
                edge.LabelY = -edge.LabelY;
            }
        }

        if (IsHorizontal(graph))
        {
            foreach (var node in graph.Nodes)
            {
                (node.X, node.Y) = (node.Y, node.X);
            }

            foreach (var edge in graph.Edges)
            {
                edge.Points = [.. edge.Points.Select(point => new LayoutPoint(point.Y, point.X))];
                (edge.LabelX, edge.LabelY) = (edge.LabelY, edge.LabelX);
            }

            SwapWidthHeight(graph);
        }
    }

    private static void SwapWidthHeight(LayoutGraph graph)
    {
        foreach (var node in graph.Nodes)
        {
            (node.Width, node.Height) = (node.Height, node.Width);
        }

        foreach (var edge in graph.Edges)
        {
            (edge.LabelWidth, edge.LabelHeight) = (edge.LabelHeight, edge.LabelWidth);
        }
    }

    /// <summary>
    /// Sizes the border nodes, in the coordinates of the layout (ranks along y): the title of a cluster takes the space
    /// on its top side, which is the start of the ranks from top to bottom, their end from bottom to top, and the start of
    /// the order from left to right.
    /// </summary>
    private static void SetBorderSizes(LayoutGraph graph)
    {
        foreach (var cluster in graph.Nodes)
        {
            if (!cluster.HasRankRange)
                continue;

            var title = cluster.TitleSpace;
            cluster.BorderTop!.Height = graph.Direction is LayoutDirection.TopToBottom ? title : 0;
            cluster.BorderBottom!.Height = graph.Direction is LayoutDirection.BottomToTop ? title : 0;
            foreach (var border in cluster.BorderLeft!.Values)
            {
                border.Width = IsHorizontal(graph) ? title : 0;
            }
        }
    }

    private static void RemoveBorderNodes(LayoutGraph graph)
    {
        foreach (var cluster in graph.Nodes)
        {
            if (!cluster.HasRankRange)
                continue;

            var top = cluster.BorderTop!;
            var bottom = cluster.BorderBottom!;
            var left = cluster.BorderLeft!.Values.Min(node => node.X - (node.Width / 2));
            var right = cluster.BorderRight!.Values.Max(node => node.X + (node.Width / 2));
            var minY = top.Y - (top.Height / 2);
            var maxY = bottom.Y + (bottom.Height / 2);
            cluster.Width = right - left;
            cluster.Height = maxY - minY;
            cluster.X = left + (cluster.Width / 2);
            cluster.Y = minY + (cluster.Height / 2);
        }

        foreach (var node in graph.Nodes.ToArray())
        {
            if (node.Dummy is DummyKind.Border)
            {
                graph.RemoveNode(node);
            }
        }
    }

    private static void TranslateGraph(LayoutGraph graph)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        foreach (var node in graph.Nodes)
        {
            Extend(node.X, node.Y, node.Width, node.Height);
        }

        foreach (var edge in graph.Edges)
        {
            foreach (var point in edge.Points)
            {
                Extend(point.X, point.Y, 0, 0);
            }

            if (edge.HasLabel)
            {
                Extend(edge.LabelX, edge.LabelY, edge.LabelWidth, edge.LabelHeight);
            }
        }

        if (double.IsInfinity(minX))
            return;

        foreach (var node in graph.Nodes)
        {
            node.X -= minX;
            node.Y -= minY;
        }

        foreach (var edge in graph.Edges)
        {
            edge.Points = [.. edge.Points.Select(point => new LayoutPoint(point.X - minX, point.Y - minY))];
            edge.LabelX -= minX;
            edge.LabelY -= minY;
        }

        graph.Width = maxX - minX;
        graph.Height = maxY - minY;

        void Extend(double x, double y, double width, double height)
        {
            minX = Math.Min(minX, x - (width / 2));
            maxX = Math.Max(maxX, x + (width / 2));
            minY = Math.Min(minY, y - (height / 2));
            maxY = Math.Max(maxY, y + (height / 2));
        }
    }

    private static void ReversePoints(LayoutGraph graph)
    {
        foreach (var edge in graph.Edges)
        {
            if (edge.Reversed)
            {
                edge.Points.Reverse();
            }
        }
    }

    /// <summary>Returns the nodes of each rank, sorted by their order. The clusters, which have no rank, are not part of it.</summary>
    internal static List<List<LayoutNode>> BuildLayerMatrix(LayoutGraph graph)
    {
        var layers = new List<List<LayoutNode>>();
        foreach (var node in graph.Nodes)
        {
            if (node.IsCluster)
                continue;

            while (layers.Count <= node.Rank)
            {
                layers.Add([]);
            }

            layers[node.Rank].Add(node);
        }

        foreach (var layer in layers)
        {
            layer.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Id.CompareTo(b.Id));
        }

        return layers;
    }
}
