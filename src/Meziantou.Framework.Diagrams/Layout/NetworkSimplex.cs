using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagrams.Layout;

/// <summary>
/// Assigns the ranks of the nodes of a connected directed acyclic graph, so the sum of the weighted lengths of the edges
/// is minimal and every edge is at least its minimum length long: the network simplex of Gansner et al., as dagre implements it.
/// </summary>
internal static class NetworkSimplex
{
    /// <summary>Ranks the nodes 0 to <paramref name="nodeCount"/> - 1.</summary>
    /// <param name="nodeCount">The number of nodes.</param>
    /// <param name="edges">The edges: source, target, minimum length and weight.</param>
    /// <returns>The rank of each node.</returns>
    public static int[] Run(int nodeCount, IReadOnlyList<(int V, int W, int MinLength, double Weight)> edges)
    {
        // Merge the parallel edges: their weights add up, the longest minimum length wins
        var merged = new Dictionary<(int, int), (int MinLength, double Weight)>();
        var order = new List<(int, int)>();
        foreach (var (v, w, minLength, weight) in edges)
        {
            if (v == w)
                continue;

            if (merged.TryGetValue((v, w), out var existing))
            {
                merged[(v, w)] = (Math.Max(existing.MinLength, minLength), existing.Weight + weight);
            }
            else
            {
                merged[(v, w)] = (minLength, weight);
                order.Add((v, w));
            }
        }

        var graph = new Graph(nodeCount, [.. order.Select(key => new Edge(key.Item1, key.Item2, merged[key].MinLength, merged[key].Weight))]);
        var rank = LongestPath(graph);
        if (nodeCount <= 1)
            return rank;

        if (!FeasibleTree(graph, rank, out var tree))
            return rank;

        tree.InitLowLim();
        tree.InitCutValues(graph);

        // Each exchange lowers the cost; the bound keeps a pathological input from running for too long
        var maxIterations = 50 + (10 * graph.Edges.Length);
        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var leave = tree.FindLeaveEdge();
            if (leave < 0)
                break;

            var enter = FindEnterEdge(tree, graph, rank, leave);
            if (enter < 0)
                break;

            tree.Exchange(leave, enter, graph);
            UpdateRanks(tree, graph, rank);
        }

        return rank;
    }

    /// <summary>Ranks the nodes as low as their successors allow: the sinks are 0.</summary>
    private static int[] LongestPath(Graph graph)
    {
        var rank = new int[graph.NodeCount];
        var state = new byte[graph.NodeCount]; // 0: unvisited, 1: in progress, 2: done
        var stack = new Stack<(int Node, int Index)>();
        for (var start = 0; start < graph.NodeCount; start++)
        {
            if (state[start] != 0 || graph.In[start].Count != 0)
                continue;

            Visit(start);
        }

        // Nodes on cycles or not reachable from a source (the input should have none)
        for (var start = 0; start < graph.NodeCount; start++)
        {
            if (state[start] == 0)
            {
                Visit(start);
            }
        }

        return rank;

        void Visit(int start)
        {
            state[start] = 1;
            stack.Push((start, 0));
            while (stack.Count > 0)
            {
                var (node, index) = stack.Pop();
                var outEdges = graph.Out[node];
                if (index < outEdges.Count)
                {
                    stack.Push((node, index + 1));
                    var target = graph.Edges[outEdges[index]].W;
                    if (state[target] == 0)
                    {
                        state[target] = 1;
                        stack.Push((target, 0));
                    }

                    continue;
                }

                var value = int.MaxValue;
                foreach (var edgeIndex in outEdges)
                {
                    var edge = graph.Edges[edgeIndex];
                    if (state[edge.W] == 2)
                    {
                        value = Math.Min(value, rank[edge.W] - edge.MinLength);
                    }
                }

                rank[node] = value == int.MaxValue ? 0 : value;
                state[node] = 2;
            }
        }
    }

    /// <summary>Grows a tree of tight edges (whose length is their minimum length) that spans the graph, shifting the ranks as needed.</summary>
    private static bool FeasibleTree(Graph graph, int[] rank, out Tree tree)
    {
        tree = new Tree(graph.NodeCount);
        tree.AddNode(0);
        while (TightTree(tree, graph, rank) < graph.NodeCount)
        {
            var best = -1;
            var bestSlack = int.MaxValue;
            for (var i = 0; i < graph.Edges.Length; i++)
            {
                var edge = graph.Edges[i];
                if (tree.Contains(edge.V) == tree.Contains(edge.W))
                    continue;

                var slack = Slack(edge, rank);
                if (slack < bestSlack)
                {
                    bestSlack = slack;
                    best = i;
                }
            }

            // The graph is not connected
            if (best < 0)
                return false;

            var bestEdge = graph.Edges[best];
            var delta = tree.Contains(bestEdge.V) ? bestSlack : -bestSlack;
            foreach (var node in tree.Nodes)
            {
                rank[node] += delta;
            }
        }

        return true;
    }

    private static int TightTree(Tree tree, Graph graph, int[] rank)
    {
        var stack = new Stack<int>(tree.Nodes);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            foreach (var edgeIndex in graph.NodeEdges(node))
            {
                var edge = graph.Edges[edgeIndex];
                var other = edge.V == node ? edge.W : edge.V;
                if (!tree.Contains(other) && Slack(edge, rank) == 0)
                {
                    tree.AddNode(other);
                    tree.AddEdge(node, other);
                    stack.Push(other);
                }
            }
        }

        return tree.Nodes.Count;
    }

    private static int Slack(Edge edge, int[] rank) => rank[edge.W] - rank[edge.V] - edge.MinLength;

    private static int FindEnterEdge(Tree tree, Graph graph, int[] rank, int leave)
    {
        var (v, w) = tree.EdgeEndpoints(leave);
        if (!graph.HasEdge(v, w))
        {
            (v, w) = (w, v);
        }

        var vLim = tree.Lim[v];
        var wLim = tree.Lim[w];
        var tail = v;
        var flip = false;
        if (vLim > wLim)
        {
            tail = w;
            flip = true;
        }

        var best = -1;
        var bestSlack = int.MaxValue;
        for (var i = 0; i < graph.Edges.Length; i++)
        {
            var edge = graph.Edges[i];
            if (flip == tree.IsDescendant(edge.V, tail) && flip != tree.IsDescendant(edge.W, tail))
            {
                var slack = Slack(edge, rank);
                if (slack < bestSlack)
                {
                    bestSlack = slack;
                    best = i;
                }
            }
        }

        return best;
    }

    private static void UpdateRanks(Tree tree, Graph graph, int[] rank)
    {
        foreach (var node in tree.PreOrder())
        {
            var parent = tree.Parent[node];
            if (parent < 0)
                continue;

            if (graph.TryGetEdge(node, parent, out var edge))
            {
                rank[node] = rank[parent] - edge.MinLength;
            }
            else if (graph.TryGetEdge(parent, node, out edge))
            {
                rank[node] = rank[parent] + edge.MinLength;
            }
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Edge(int V, int W, int MinLength, double Weight);

    private sealed class Graph
    {
        private readonly Dictionary<(int, int), int> _index = [];

        public Graph(int nodeCount, Edge[] edges)
        {
            NodeCount = nodeCount;
            Edges = edges;
            In = new List<int>[nodeCount];
            Out = new List<int>[nodeCount];
            for (var i = 0; i < nodeCount; i++)
            {
                In[i] = [];
                Out[i] = [];
            }

            for (var i = 0; i < edges.Length; i++)
            {
                Out[edges[i].V].Add(i);
                In[edges[i].W].Add(i);
                _index[(edges[i].V, edges[i].W)] = i;
            }
        }

        public int NodeCount { get; }

        public Edge[] Edges { get; }

        public List<int>[] In { get; }

        public List<int>[] Out { get; }

        public IEnumerable<int> NodeEdges(int node) => In[node].Concat(Out[node]);

        public bool HasEdge(int v, int w) => _index.ContainsKey((v, w));

        public bool TryGetEdge(int v, int w, out Edge edge)
        {
            if (_index.TryGetValue((v, w), out var index))
            {
                edge = Edges[index];
                return true;
            }

            edge = default;
            return false;
        }
    }

    /// <summary>An undirected spanning tree, with the cut values of its edges and the postorder numbers of its nodes.</summary>
    private sealed class Tree
    {
        private readonly bool[] _contains;
        private readonly List<(int V, int W)> _edges = [];
        private readonly List<double> _cutValues = [];
        private readonly List<int>[] _adjacency;

        public Tree(int nodeCount)
        {
            _contains = new bool[nodeCount];
            _adjacency = new List<int>[nodeCount];
            for (var i = 0; i < nodeCount; i++)
            {
                _adjacency[i] = [];
            }

            Low = new int[nodeCount];
            Lim = new int[nodeCount];
            Parent = new int[nodeCount];
        }

        public List<int> Nodes { get; } = [];

        public int[] Low { get; }

        public int[] Lim { get; }

        public int[] Parent { get; }

        public bool Contains(int node) => _contains[node];

        public void AddNode(int node)
        {
            _contains[node] = true;
            Nodes.Add(node);
        }

        public void AddEdge(int v, int w)
        {
            _edges.Add((v, w));
            _cutValues.Add(0);
            _adjacency[v].Add(_edges.Count - 1);
            _adjacency[w].Add(_edges.Count - 1);
        }

        public (int V, int W) EdgeEndpoints(int edge) => _edges[edge];

        public bool IsDescendant(int node, int root) => Low[root] <= Lim[node] && Lim[node] <= Lim[root];

        public int FindLeaveEdge()
        {
            for (var i = 0; i < _edges.Count; i++)
            {
                if (_edges[i].V >= 0 && _cutValues[i] < 0)
                    return i;
            }

            return -1;
        }

        public void Exchange(int leave, int enter, Graph graph)
        {
            var (v, w) = _edges[leave];
            _adjacency[v].Remove(leave);
            _adjacency[w].Remove(leave);
            _edges[leave] = (-1, -1);
            var entering = graph.Edges[enter];
            AddEdge(entering.V, entering.W);
            InitLowLim();
            InitCutValues(graph);
        }

        public void InitLowLim()
        {
            Array.Fill(Parent, -1);
            var root = Nodes[0];
            var visited = new bool[_contains.Length];
            var nextLim = 1;
            var stack = new Stack<(int Node, int Index, int Low)>();
            visited[root] = true;
            stack.Push((root, 0, nextLim));
            while (stack.Count > 0)
            {
                var (node, index, low) = stack.Pop();
                var adjacency = _adjacency[node];
                if (index < adjacency.Count)
                {
                    stack.Push((node, index + 1, low));
                    var (a, b) = _edges[adjacency[index]];
                    var other = a == node ? b : a;
                    if (!visited[other])
                    {
                        visited[other] = true;
                        Parent[other] = node;
                        stack.Push((other, 0, nextLim));
                    }

                    continue;
                }

                Low[node] = low;
                Lim[node] = nextLim++;
            }
        }

        public void InitCutValues(Graph graph)
        {
            // Postorder: the children before their parents, so the cut values of the child edges are known
            var nodes = Nodes.OrderBy(node => Lim[node]).ToList();
            foreach (var child in nodes)
            {
                var parent = Parent[child];
                if (parent < 0)
                    continue;

                SetCutValue(child, parent, CalculateCutValue(child, parent, graph));
            }
        }

        public IEnumerable<int> PreOrder() => Nodes.OrderByDescending(node => Lim[node]);

        private double CalculateCutValue(int child, int parent, Graph graph)
        {
            var childIsTail = true;
            if (!graph.TryGetEdge(child, parent, out var graphEdge))
            {
                childIsTail = false;
                graph.TryGetEdge(parent, child, out graphEdge);
            }

            var cutValue = graphEdge.Weight;
            foreach (var edgeIndex in graph.NodeEdges(child))
            {
                var edge = graph.Edges[edgeIndex];
                var isOutEdge = edge.V == child;
                var other = isOutEdge ? edge.W : edge.V;
                if (other == parent)
                    continue;

                var pointsToHead = isOutEdge == childIsTail;
                cutValue += pointsToHead ? edge.Weight : -edge.Weight;
                if (TryGetCutValue(child, other, out var otherCutValue))
                {
                    cutValue += pointsToHead ? -otherCutValue : otherCutValue;
                }
            }

            return cutValue;
        }

        private bool TryGetCutValue(int a, int b, out double value)
        {
            foreach (var edgeIndex in _adjacency[a])
            {
                var (v, w) = _edges[edgeIndex];
                if ((v == a && w == b) || (v == b && w == a))
                {
                    value = _cutValues[edgeIndex];
                    return true;
                }
            }

            value = 0;
            return false;
        }

        private void SetCutValue(int a, int b, double value)
        {
            foreach (var edgeIndex in _adjacency[a])
            {
                var (v, w) = _edges[edgeIndex];
                if ((v == a && w == b) || (v == b && w == a))
                {
                    _cutValues[edgeIndex] = value;
                    return;
                }
            }
        }
    }
}
