namespace Meziantou.Framework.Graphviz.Internals;

internal sealed class DotGraph(bool isStrict, bool isDirected, string? name, DotSubgraph root)
{
    public bool IsStrict { get; } = isStrict;

    public bool IsDirected { get; } = isDirected;

    public string? Name { get; } = name;

    public DotSubgraph Root { get; } = root;

    /// <summary>Gets the nodes, in the order they are created.</summary>
    public List<DotNode> Nodes { get; } = [];

    public Dictionary<string, DotNode> NodesById { get; } = new(StringComparer.Ordinal);

    public List<DotEdge> Edges { get; } = [];

    /// <summary>Gets the subgraphs, in the order they are created.</summary>
    public List<DotSubgraph> Subgraphs { get; } = [];
}
