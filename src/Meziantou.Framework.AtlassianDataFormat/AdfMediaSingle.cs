namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a single media item, optionally with a caption.</summary>
public sealed class AdfMediaSingle : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.MediaSingle;

    /// <summary>Gets the layout of the media item.</summary>
    public string? Layout { get; init; }

    /// <summary>Gets the width of the media item.</summary>
    public double? Width { get; init; }

    /// <summary>Gets the unit of <see cref="Width"/>.</summary>
    public string? WidthType { get; init; }
}
