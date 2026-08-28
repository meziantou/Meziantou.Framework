namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents an embedded smart link rendered as a block.</summary>
public sealed class AdfEmbedCard : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.EmbedCard;

    /// <summary>Gets the URL of the embedded resource.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The document stores the raw value, which is not guaranteed to be a well-formed URI.")]
    public required string Url { get; init; }

    /// <summary>Gets the layout of the card.</summary>
    public string? Layout { get; init; }

    /// <summary>Gets the width of the card, as a percentage.</summary>
    public double? Width { get; init; }
}
