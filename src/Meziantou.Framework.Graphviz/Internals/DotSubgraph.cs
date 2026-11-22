namespace Meziantou.Framework.Graphviz.Internals;

internal sealed class DotSubgraph(string? name, DotSubgraph? parent, int position, DotAttributes attributes)
{
    private readonly HashSet<DotNode> _nodeSet = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets the name of the subgraph, or <see langword="null"/> for the root graph or an anonymous subgraph.</summary>
    public string? Name { get; } = name;

    public string? GeneratedName { get; init; }

    public DotSubgraph? Parent { get; } = parent;

    public int Position { get; } = position;

    public int Depth { get; } = parent is null ? 0 : parent.Depth + 1;

    public DotAttributes Attributes { get; } = attributes;

    /// <summary>Gets or sets the default attributes of the nodes of the subgraph, kept when its body is reopened, or <see langword="null"/> before its first body.</summary>
    public DotAttributes? NodeDefaults { get; set; }

    /// <summary>Gets or sets the default attributes of the edges of the subgraph, kept when its body is reopened, or <see langword="null"/> before its first body.</summary>
    public DotAttributes? EdgeDefaults { get; set; }

    /// <summary>Gets the nodes of the subgraph and of its nested subgraphs, in the order they are mentioned.</summary>
    public List<DotNode> Nodes { get; } = [];

    public List<DotSubgraph> Children { get; } = [];

    public bool AddNode(DotNode node)
    {
        if (!_nodeSet.Add(node))
            return false;

        Nodes.Add(node);
        return true;
    }

    public bool Contains(DotNode node) => _nodeSet.Contains(node);

    public bool IsDescendantOf(DotSubgraph other)
    {
        for (var current = Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, other))
                return true;
        }

        return false;
    }
}
