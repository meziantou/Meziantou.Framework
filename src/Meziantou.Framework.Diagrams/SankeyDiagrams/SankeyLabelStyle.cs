namespace Meziantou.Framework.Diagrams;

/// <summary>How the labels of the nodes of a sankey diagram are drawn (Mermaid <c>labelStyle</c>).</summary>
public enum SankeyLabelStyle
{
    /// <summary>Plain texts, after the nodes of the left half and before the nodes of the right half (Mermaid <c>legacy</c>).</summary>
    Legacy,

    /// <summary>
    /// Texts outlined with the background color, for links that cross them, before the nodes of the columns that precede the
    /// column of the largest node and after the others (Mermaid <c>outlined</c>).
    /// </summary>
    Outlined,
}
