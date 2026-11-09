namespace Meziantou.Framework.Diagrams.Layout;

/// <summary>A compound multigraph laid out in layers, as dagre does: nodes, edges, and clusters that contain nodes.</summary>
internal sealed class LayoutGraph
{
    private int _nextId;

    public List<LayoutNode> Nodes { get; } = [];

    public List<LayoutEdge> Edges { get; } = [];

    public LayoutDirection Direction { get; set; }

    /// <summary>Gets or sets the space between two nodes of a rank.</summary>
    public double NodeSeparation { get; set; } = 50;

    /// <summary>Gets or sets the space between two ranks.</summary>
    public double RankSeparation { get; set; } = 50;

    /// <summary>Gets or sets the space between two edges, or an edge and a node, of a rank.</summary>
    public double EdgeSeparation { get; set; } = 20;

    /// <summary>Gets or sets the space between the border of a cluster and its content.</summary>
    public double ClusterPadding { get; set; } = 8;

    public double Width { get; internal set; }

    public double Height { get; internal set; }

    internal int NodeRankFactor { get; set; } = 1;

    internal LayoutNode? NestingRoot { get; set; }

    internal List<LayoutNode> DummyChains { get; } = [];

    internal int MaxRank { get; set; }

    public LayoutNode AddNode(double width, double height, object? tag = null)
    {
        var node = new LayoutNode(_nextId++) { Width = width, Height = height, Tag = tag };
        Nodes.Add(node);
        return node;
    }

    public LayoutNode AddCluster(object? tag = null)
    {
        var node = AddNode(0, 0, tag);
        node.IsCluster = true;
        return node;
    }

    internal LayoutNode AddDummy(DummyKind kind, double width = 0, double height = 0)
    {
        var node = AddNode(width, height);
        node.Dummy = kind;
        return node;
    }

    public LayoutEdge AddEdge(LayoutNode v, LayoutNode w, object? tag = null)
    {
        var edge = new LayoutEdge(v, w) { Tag = tag };
        AddEdge(edge);
        return edge;
    }

    internal void AddEdge(LayoutEdge edge)
    {
        Edges.Add(edge);
        edge.V.OutEdges.Add(edge);
        edge.W.InEdges.Add(edge);
    }

    internal void RemoveEdge(LayoutEdge edge)
    {
        Edges.Remove(edge);
        edge.V.OutEdges.Remove(edge);
        edge.W.InEdges.Remove(edge);
    }

    internal void RemoveNode(LayoutNode node)
    {
        foreach (var edge in node.InEdges.ToArray())
        {
            RemoveEdge(edge);
        }

        foreach (var edge in node.OutEdges.ToArray())
        {
            RemoveEdge(edge);
        }

        node.Parent?.Children.Remove(node);
        foreach (var child in node.Children)
        {
            child.Parent = node.Parent;
            node.Parent?.Children.Add(child);
        }

        node.Children.Clear();
        node.Parent = null;
        Nodes.Remove(node);
    }

    public static void SetParent(LayoutNode node, LayoutNode? parent)
    {
        node.Parent?.Children.Remove(node);
        node.Parent = parent;
        parent?.Children.Add(node);
    }

    internal IEnumerable<LayoutNode> GetChildren(LayoutNode? node) => node is null ? Nodes.Where(n => n.Parent is null) : node.Children;
}
