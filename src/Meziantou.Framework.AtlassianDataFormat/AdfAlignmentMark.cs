namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents an alignment applied to a paragraph or heading.</summary>
public sealed class AdfAlignmentMark : AdfMark
{
    /// <inheritdoc />
    public override AdfMarkKind Kind => AdfMarkKind.Alignment;

    /// <summary>Gets the alignment, either <c>center</c> or <c>end</c>.</summary>
    public required string Align { get; init; }
}
