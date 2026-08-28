namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents an emoji.</summary>
public sealed class AdfEmoji : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.Emoji;

    /// <summary>Gets the shortcode of the emoji, such as <c>:smile:</c>.</summary>
    public required string ShortName { get; init; }

    /// <summary>Gets the identifier of the emoji.</summary>
    public string? Id { get; init; }

    /// <summary>Gets the literal characters of the emoji.</summary>
    public string? Text { get; init; }

    /// <summary>
    /// Gets the literal characters of the emoji, as stored by the products that write the
    /// <c>fallback</c> attribute instead of <c>text</c>.
    /// </summary>
    public string? Fallback { get; init; }
}
