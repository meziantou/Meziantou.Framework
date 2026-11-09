namespace Meziantou.Framework.Diagrams;

/// <summary>The decoration of an end of a message of a sequence diagram.</summary>
public enum SequenceArrowHead
{
    /// <summary>No decoration (Mermaid <c>-&gt;</c>).</summary>
    None,

    /// <summary>A filled arrowhead (Mermaid <c>-&gt;&gt;</c>).</summary>
    Arrow,

    /// <summary>An open arrowhead: an asynchronous message (Mermaid <c>-)</c>).</summary>
    OpenArrow,

    /// <summary>A cross (Mermaid <c>-x</c>).</summary>
    Cross,

    /// <summary>The top half of a filled arrowhead (Mermaid <c>-|\</c>).</summary>
    HalfArrowTop,

    /// <summary>The bottom half of a filled arrowhead (Mermaid <c>-|/</c>).</summary>
    HalfArrowBottom,

    /// <summary>The top stroke of an open arrowhead (Mermaid <c>-\\</c>).</summary>
    StickTop,

    /// <summary>The bottom stroke of an open arrowhead (Mermaid <c>-//</c>).</summary>
    StickBottom,
}
