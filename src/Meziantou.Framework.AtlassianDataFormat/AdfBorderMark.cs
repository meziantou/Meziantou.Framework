namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a border applied to a media item.</summary>
public sealed class AdfBorderMark : AdfMark
{
    /// <inheritdoc />
    public override AdfMarkKind Kind => AdfMarkKind.Border;

    /// <summary>Gets the border size, between 1 and 3.</summary>
    public required int Size { get; init; }

    /// <summary>Gets the border color.</summary>
    public string? Color { get; init; }
}
