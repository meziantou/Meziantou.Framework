namespace Meziantou.Framework.Graphviz;

/// <summary>The options of <see cref="GraphvizDocument.Parse(string, GraphvizParseOptions?)"/>.</summary>
public sealed class GraphvizParseOptions
{
    /// <summary>Gets the default options.</summary>
    public static GraphvizParseOptions Default { get; } = new();

    /// <summary>Gets the largest text, in characters; a larger text is rejected. The default is 1,000,000.</summary>
    public int MaxTextSize { get; init; } = 1_000_000;

    /// <summary>Gets the largest number of nodes of a graph. The default is 5,000.</summary>
    public int MaxNodes { get; init; } = 5_000;

    /// <summary>Gets the largest number of edges of a graph, after the edges between groups of nodes are expanded, and before the duplicate edges of a strict graph are merged. The default is 10,000.</summary>
    public int MaxEdges { get; init; } = 10_000;

    /// <summary>Gets the largest number of attributes the default attributes (<c>node [...]</c>, <c>edge [...]</c> and the attributes of the parent graphs) give to the nodes, the edges and the subgraphs, in total. The default is 1,000,000.</summary>
    public int MaxInheritedAttributes { get; init; } = 1_000_000;

    /// <summary>Gets the deepest nesting of the subgraphs. The default is 32.</summary>
    public int MaxDepth { get; init; } = 32;

    /// <summary>Gets the theme of the diagram, or <see langword="null"/> for <see cref="DiagramTheme.Default"/>. The <c>bgcolor</c> of the graph sets its background.</summary>
    public DiagramTheme? DefaultTheme { get; init; }
}
