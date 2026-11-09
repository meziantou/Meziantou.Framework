namespace Meziantou.Framework.Diagrams;

/// <summary>The kind of a state of a state diagram.</summary>
public enum StateKind
{
    /// <summary>A state: a rounded rectangle with its name, or a frame when it has regions (a composite state).</summary>
    Normal,

    /// <summary>The start of a region: a filled circle (Mermaid <c>[*] --&gt;</c>).</summary>
    Start,

    /// <summary>The end of a region: a filled circle in a ring (Mermaid <c>--&gt; [*]</c>).</summary>
    End,

    /// <summary>A choice between transitions: a small diamond (Mermaid <c>&lt;&lt;choice&gt;&gt;</c>).</summary>
    Choice,

    /// <summary>A fork into concurrent transitions: a bar (Mermaid <c>&lt;&lt;fork&gt;&gt;</c>).</summary>
    Fork,

    /// <summary>A join of concurrent transitions: a bar (Mermaid <c>&lt;&lt;join&gt;&gt;</c>).</summary>
    Join,
}
