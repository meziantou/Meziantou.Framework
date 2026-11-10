namespace Meziantou.Framework.Diagrams;

/// <summary>The line of an edge of a flowchart.</summary>
public enum FlowchartLineStyle
{
    /// <summary>A solid line (Mermaid <c>--&gt;</c>).</summary>
    Solid,

    /// <summary>A dotted line (Mermaid <c>-.-&gt;</c>).</summary>
    Dotted,

    /// <summary>A thick line (Mermaid <c>==&gt;</c>).</summary>
    Thick,

    /// <summary>No line: the edge only keeps its nodes close (Mermaid <c>~~~</c>).</summary>
    Invisible,
}
