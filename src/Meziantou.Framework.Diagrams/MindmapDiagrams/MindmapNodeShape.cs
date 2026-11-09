namespace Meziantou.Framework.Diagrams;

/// <summary>The shape of a node of a mindmap.</summary>
public enum MindmapNodeShape
{
    /// <summary>A box without border, with a line below its text (Mermaid <c>text</c>).</summary>
    Default,

    /// <summary>A rectangle (Mermaid <c>id[text]</c>).</summary>
    Square,

    /// <summary>A rectangle with rounded corners (Mermaid <c>id(text)</c>).</summary>
    RoundedSquare,

    /// <summary>A circle (Mermaid <c>id((text))</c>).</summary>
    Circle,

    /// <summary>An explosion (Mermaid <c>id))text((</c>).</summary>
    Bang,

    /// <summary>A cloud (Mermaid <c>id)text(</c>).</summary>
    Cloud,

    /// <summary>A hexagon (Mermaid <c>id{{text}}</c>).</summary>
    Hexagon,
}
