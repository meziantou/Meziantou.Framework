namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a hyperlink.</summary>
public sealed class AdfLinkMark : AdfMark
{
    /// <inheritdoc />
    public override AdfMarkKind Kind => AdfMarkKind.Link;

    /// <summary>Gets the target of the link.</summary>
    public required string Href { get; init; }

    /// <summary>Gets the title of the link.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the identifier of the linked resource.</summary>
    public string? Id { get; init; }

    /// <summary>Gets the collection of the linked resource.</summary>
    public string? Collection { get; init; }

    /// <summary>Gets the occurrence key of the linked resource.</summary>
    public string? OccurrenceKey { get; init; }
}
