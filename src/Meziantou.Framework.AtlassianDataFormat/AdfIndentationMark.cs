namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents an indentation applied to a paragraph or heading.</summary>
public sealed class AdfIndentationMark : AdfMark
{
    /// <inheritdoc />
    public override AdfMarkKind Kind => AdfMarkKind.Indentation;

    /// <summary>Gets the indentation level, between 1 and 6.</summary>
    public required int Level { get; init; }
}
