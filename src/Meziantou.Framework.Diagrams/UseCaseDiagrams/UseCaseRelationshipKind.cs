namespace Meziantou.Framework.Diagrams;

/// <summary>The kind of a relationship of a use case diagram.</summary>
public enum UseCaseRelationshipKind
{
    /// <summary>An association: a solid line, with the arrowheads of <see cref="UseCaseRelationship.FromArrowHead"/> and <see cref="UseCaseRelationship.ToArrowHead"/>.</summary>
    Association,

    /// <summary>The source use case includes the target one: a dashed arrow labeled «include».</summary>
    Include,

    /// <summary>The source use case extends the target one: a dashed arrow labeled «extend».</summary>
    Extend,

    /// <summary>The source is a specialization of the target: a solid line with a hollow triangle at the target.</summary>
    Generalization,
}
