namespace Meziantou.Framework.Diagrams.Layout;

internal sealed class LayoutNode(int id)
{
    public int Id { get; } = id;

    public double Width { get; set; }

    public double Height { get; set; }

    /// <summary>Gets or sets the x coordinate of the center.</summary>
    public double X { get; set; }

    /// <summary>Gets or sets the y coordinate of the center.</summary>
    public double Y { get; set; }

    public object? Tag { get; set; }

    public LayoutNode? Parent { get; set; }

    public List<LayoutNode> Children { get; } = [];

    /// <summary>Gets or sets a value that indicates whether the node is a cluster, whose size comes from its children.</summary>
    public bool IsCluster { get; set; }

    /// <summary>Gets or sets the space on the top side of a cluster, for its title.</summary>
    public double TitleSpace { get; set; }

    internal List<LayoutEdge> InEdges { get; } = [];

    internal List<LayoutEdge> OutEdges { get; } = [];

    internal int Rank { get; set; }

    internal int Order { get; set; }

    internal DummyKind Dummy { get; set; }

    internal LayoutNode? BorderTop { get; set; }

    internal LayoutNode? BorderBottom { get; set; }

    internal Dictionary<int, LayoutNode>? BorderLeft { get; set; }

    internal Dictionary<int, LayoutNode>? BorderRight { get; set; }

    internal bool HasRankRange { get; set; }

    internal int MinRank { get; set; }

    internal int MaxRank { get; set; }

    internal BorderSide BorderType { get; set; }

    /// <summary>Gets or sets the edge a dummy node of a chain belongs to.</summary>
    internal LayoutEdge? EdgeObject { get; set; }

    internal List<LayoutEdge>? SelfEdges { get; set; }

    public bool IsDummy => Dummy is not DummyKind.None;

    public override string ToString() => $"{Id} ({Dummy}) r={Rank} o={Order} x={X} y={Y}";
}
