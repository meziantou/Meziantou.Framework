namespace Meziantou.Framework.Mermaid;

/// <summary>How the links of a sankey diagram are colored, from the colors of their nodes (Mermaid <c>linkColor</c>).</summary>
public enum SankeyLinkColoring
{
    /// <summary>
    /// A gradient from the color of the source to the color of the target (Mermaid <c>gradient</c>). It is written back,
    /// but the links are drawn in the color of their source, as <see cref="Source"/>.
    /// </summary>
    Gradient,

    /// <summary>The color of the source (Mermaid <c>source</c>).</summary>
    Source,

    /// <summary>The color of the target (Mermaid <c>target</c>).</summary>
    Target,
}
