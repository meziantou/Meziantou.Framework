namespace Meziantou.Framework.Diagrams;

/// <summary>The style of the line of a message of a sequence diagram.</summary>
public enum SequenceLineStyle
{
    /// <summary>A solid line (Mermaid <c>-&gt;&gt;</c>).</summary>
    Solid,

    /// <summary>A dotted line, usually a reply (Mermaid <c>--&gt;&gt;</c>).</summary>
    Dotted,
}
