namespace Meziantou.Framework.Graphviz.Internals;

internal sealed class DotNode(string id, int position, DotAttributes attributes)
{
    public string Id { get; } = id;

    /// <summary>Gets the offset of the first mention of the node.</summary>
    public int Position { get; } = position;

    public DotAttributes Attributes { get; } = attributes;

    /// <summary>Gets the subgraphs whose statements mention the node directly.</summary>
    public List<DotSubgraph> Mentions { get; } = [];
}
