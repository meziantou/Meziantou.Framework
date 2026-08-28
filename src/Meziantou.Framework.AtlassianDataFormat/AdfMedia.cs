namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a media item such as an image or an attachment.</summary>
public sealed class AdfMedia : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.Media;

    /// <summary>Gets the kind of media item.</summary>
    public AdfMediaType Type { get; init; }

    /// <summary>Gets the identifier of the item in the Atlassian media store.</summary>
    public string? Id { get; init; }

    /// <summary>Gets the collection of the item in the Atlassian media store.</summary>
    public string? Collection { get; init; }

    /// <summary>
    /// Gets the URL of the item. It is only set when <see cref="Type"/> is
    /// <see cref="AdfMediaType.External"/>; media stored by Atlassian must be resolved
    /// through the media API.
    /// </summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The document stores the raw value, which is not guaranteed to be a well-formed URI.")]
    public string? Url { get; init; }

    /// <summary>Gets the alternative text of the item.</summary>
    public string? Alt { get; init; }

    /// <summary>Gets the width of the item, in pixels.</summary>
    public double? Width { get; init; }

    /// <summary>Gets the height of the item, in pixels.</summary>
    public double? Height { get; init; }

    /// <summary>Gets the occurrence key of the item.</summary>
    public string? OccurrenceKey { get; init; }
}
