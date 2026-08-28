namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents an inline comment annotation.</summary>
public sealed class AdfAnnotationMark : AdfMark
{
    /// <inheritdoc />
    public override AdfMarkKind Kind => AdfMarkKind.Annotation;

    /// <summary>Gets the identifier of the annotation.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the type of the annotation.</summary>
    public string? AnnotationType { get; init; }
}
