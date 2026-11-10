namespace Meziantou.Framework.Diagrams;

/// <summary>The direction of the flow of a link of a Wardley map, drawn as arrowheads.</summary>
public enum WardleyFlow
{
    /// <summary>A dependency without flow: a line without arrowheads.</summary>
    None,

    /// <summary>A flow from the source to the target (Mermaid <c>+&gt;</c>).</summary>
    Forward,

    /// <summary>A flow from the target to the source (Mermaid <c>+&lt;</c>).</summary>
    Backward,

    /// <summary>A flow in both directions (Mermaid <c>+&lt;&gt;</c>).</summary>
    Bidirectional,
}
