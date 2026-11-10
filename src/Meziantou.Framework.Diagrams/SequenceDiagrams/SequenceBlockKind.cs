namespace Meziantou.Framework.Diagrams;

/// <summary>The kind of a block of a sequence diagram.</summary>
public enum SequenceBlockKind
{
    /// <summary>A loop (Mermaid <c>loop</c>).</summary>
    Loop,

    /// <summary>Alternatives: sections of which one happens (Mermaid <c>alt</c> and <c>else</c>).</summary>
    Alternative,

    /// <summary>An optional section (Mermaid <c>opt</c>).</summary>
    Optional,

    /// <summary>Parallel sections (Mermaid <c>par</c> and <c>and</c>).</summary>
    Parallel,

    /// <summary>Parallel sections that overlap in time (Mermaid <c>par_over</c>).</summary>
    ParallelOverlap,

    /// <summary>A critical region, with the options of the circumstances (Mermaid <c>critical</c> and <c>option</c>).</summary>
    Critical,

    /// <summary>A break of the sequence, such as an exception (Mermaid <c>break</c>).</summary>
    Break,

    /// <summary>A colored background, without frame (Mermaid <c>rect</c>).</summary>
    Rectangle,
}
