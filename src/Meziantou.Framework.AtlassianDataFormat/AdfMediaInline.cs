namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a media item rendered inline with surrounding text.</summary>
public sealed class AdfMediaInline : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.MediaInline;

    /// <summary>Gets the kind of media item.</summary>
    public AdfMediaType Type { get; init; }

    /// <summary>Gets the identifier of the item in the Atlassian media store.</summary>
    public string? Id { get; init; }

    /// <summary>Gets the collection of the item in the Atlassian media store.</summary>
    public string? Collection { get; init; }

    /// <summary>Gets the alternative text of the item.</summary>
    public string? Alt { get; init; }

    /// <summary>Gets the width of the item, in pixels.</summary>
    public double? Width { get; init; }

    /// <summary>Gets the height of the item, in pixels.</summary>
    public double? Height { get; init; }

    /// <summary>Gets the occurrence key of the item.</summary>
    public string? OccurrenceKey { get; init; }
}
