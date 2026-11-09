namespace Meziantou.Framework.Diagrams;

/// <summary>A relationship between two nodes of a requirement diagram, such as an element that satisfies a requirement.</summary>
public sealed class RequirementRelationship
{
    /// <summary>Initializes a new instance of the <see cref="RequirementRelationship"/> class.</summary>
    /// <param name="source">The source.</param>
    /// <param name="target">The target.</param>
    /// <param name="kind">The kind.</param>
    public RequirementRelationship(RequirementDiagramNode source, RequirementDiagramNode target, RequirementRelationshipKind kind)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Source = source;
        Target = target;
        Kind = kind;
    }

    /// <summary>Gets the source.</summary>
    public RequirementDiagramNode Source { get; }

    /// <summary>Gets the target.</summary>
    public RequirementDiagramNode Target { get; }

    /// <summary>Gets or sets the kind of the relationship.</summary>
    public RequirementRelationshipKind Kind { get; set; }
}
