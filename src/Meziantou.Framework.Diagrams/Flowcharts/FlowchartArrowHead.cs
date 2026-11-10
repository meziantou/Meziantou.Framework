namespace Meziantou.Framework.Diagrams;

/// <summary>The decoration of an end of an edge of a flowchart.</summary>
public enum FlowchartArrowHead
{
    /// <summary>No decoration (Mermaid <c>---</c>).</summary>
    None,

    /// <summary>An arrowhead (Mermaid <c>--&gt;</c>).</summary>
    Arrow,

    /// <summary>A circle (Mermaid <c>--o</c>).</summary>
    Circle,

    /// <summary>A cross (Mermaid <c>--x</c>).</summary>
    Cross,
}
