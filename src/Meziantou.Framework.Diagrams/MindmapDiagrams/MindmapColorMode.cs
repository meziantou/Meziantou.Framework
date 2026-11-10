namespace Meziantou.Framework.Diagrams;

/// <summary>How the nodes of a mindmap take their color from the palette of the theme.</summary>
public enum MindmapColorMode
{
    /// <summary>Each child of the root and its descendants have their own color (Mermaid <c>colorBy: branch</c>).</summary>
    Branch,

    /// <summary>The nodes at the same depth have the same color (Mermaid <c>colorBy: depth</c>).</summary>
    Depth,
}
