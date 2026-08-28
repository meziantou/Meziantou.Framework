using System.Text.Json;

namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a smart link rendered inline with surrounding text.</summary>
public sealed class AdfInlineCard : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.InlineCard;

    /// <summary>Gets the URL of the linked resource, when the card is defined by a URL.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The document stores the raw value, which is not guaranteed to be a well-formed URI.")]
    public string? Url { get; init; }

    /// <summary>Gets the JSON-LD description of the linked resource, when the card is defined by data.</summary>
    public JsonElement? Data { get; init; }
}
