namespace Meziantou.Framework.Mermaid;

/// <summary>The options of <see cref="MermaidDocument.Parse(string, MermaidParseOptions?)"/>.</summary>
public sealed class MermaidParseOptions
{
    /// <summary>Gets the default options.</summary>
    public static MermaidParseOptions Default { get; } = new();

    /// <summary>Gets the largest text, in characters; a larger text is rejected. The default is 50,000, the limit of Mermaid.</summary>
    public int MaxTextSize { get; init; } = 50_000;

    /// <summary>Gets the largest number of edges of a diagram. The default is 500, the limit of Mermaid.</summary>
    public int MaxEdges { get; init; } = 500;

    /// <summary>Gets the largest number of nodes of a diagram. The default is 2,000.</summary>
    public int MaxNodes { get; init; } = 2_000;

    /// <summary>Gets the deepest nesting of the groups (subgraphs, namespaces, composite states...) and of the expressions of a railroad diagram. The default is 32.</summary>
    public int MaxDepth { get; init; } = 32;

    /// <summary>Gets the theme of the diagram when the text does not choose one, or <see langword="null"/> for <see cref="DiagramTheme.Default"/>.</summary>
    public DiagramTheme? DefaultTheme { get; init; }
}
