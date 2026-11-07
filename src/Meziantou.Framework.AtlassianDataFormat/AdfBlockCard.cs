using System.Text.Json;

namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a smart link rendered as a block.</summary>
public sealed class AdfBlockCard : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.BlockCard;

    /// <summary>Gets the URL of the linked resource, when the card is defined by a URL.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The document stores the raw value, which is not guaranteed to be a well-formed URI.")]
    public string? Url { get; init; }

    /// <summary>Gets the JSON-LD description of the linked resource, when the card is defined by data.</summary>
    public JsonElement? Data { get; init; }

    /// <summary>Gets the datasource of the card, when the card is backed by a query.</summary>
    public JsonElement? Datasource { get; init; }

    /// <summary>Gets the layout of the card.</summary>
    public string? Layout { get; init; }

    /// <summary>Gets the width of the card.</summary>
    public double? Width { get; init; }
}
