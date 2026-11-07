namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents text with a background color.</summary>
public sealed class AdfBackgroundColorMark : AdfMark
{
    /// <inheritdoc />
    public override AdfMarkKind Kind => AdfMarkKind.BackgroundColor;

    /// <summary>Gets the color, as a <c>#rrggbb</c> string.</summary>
    public required string Color { get; init; }
}
