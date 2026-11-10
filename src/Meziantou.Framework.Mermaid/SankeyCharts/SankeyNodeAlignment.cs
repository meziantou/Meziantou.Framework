namespace Meziantou.Framework.Mermaid;

/// <summary>How the nodes of a sankey diagram are placed in columns, as the alignments of d3-sankey (Mermaid <c>nodeAlignment</c>).</summary>
public enum SankeyNodeAlignment
{
    /// <summary>A node is in the column of its depth, and the nodes without outgoing links are in the last column (Mermaid <c>justify</c>).</summary>
    Justify,

    /// <summary>A node is in the column of its depth: the number of links from the first column (Mermaid <c>left</c>).</summary>
    Left,

    /// <summary>A node is in the column of its height: the number of links to the last column (Mermaid <c>right</c>).</summary>
    Right,

    /// <summary>As <see cref="Left"/>, except that the nodes without incoming links are just before their first target (Mermaid <c>center</c>).</summary>
    Center,
}
