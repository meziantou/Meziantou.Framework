namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents colored text.</summary>
public sealed class AdfTextColorMark : AdfMark
{
    /// <inheritdoc />
    public override AdfMarkKind Kind => AdfMarkKind.TextColor;

    /// <summary>Gets the color, as a <c>#rrggbb</c> string.</summary>
    public required string Color { get; init; }
}
