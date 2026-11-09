namespace Meziantou.Framework.Diagrams;

/// <summary>The kind of a relationship of a requirement diagram, shown on the relationship.</summary>
public enum RequirementRelationshipKind
{
    /// <summary>The source contains the target: a solid line with a circled cross at the source.</summary>
    Contains,

    /// <summary>The source copies the target.</summary>
    Copies,

    /// <summary>The source derives from the target.</summary>
    Derives,

    /// <summary>The source satisfies the target.</summary>
    Satisfies,

    /// <summary>The source verifies the target.</summary>
    Verifies,

    /// <summary>The source refines the target.</summary>
    Refines,

    /// <summary>The source traces to the target.</summary>
    Traces,
}
