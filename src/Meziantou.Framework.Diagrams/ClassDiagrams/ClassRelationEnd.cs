namespace Meziantou.Framework.Diagrams;

/// <summary>The decoration of an end of a relation of a class diagram.</summary>
public enum ClassRelationEnd
{
    /// <summary>No decoration: a plain link.</summary>
    None,

    /// <summary>An arrowhead: an association or a dependency (Mermaid <c>&lt;</c> or <c>&gt;</c>).</summary>
    Arrow,

    /// <summary>A hollow triangle: an inheritance or a realization (Mermaid <c>&lt;|</c> or <c>|&gt;</c>).</summary>
    Inheritance,

    /// <summary>A filled diamond: a composition (Mermaid <c>*</c>).</summary>
    Composition,

    /// <summary>A hollow diamond: an aggregation (Mermaid <c>o</c>).</summary>
    Aggregation,

    /// <summary>A lollipop: the end is an interface the other end provides (Mermaid <c>()</c>).</summary>
    Lollipop,
}
