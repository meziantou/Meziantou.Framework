namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a mention of a user.</summary>
public sealed class AdfMention : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.Mention;

    /// <summary>Gets the Atlassian account identifier of the mentioned user.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the display text of the mention, usually the name prefixed with <c>@</c>.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the access level of the mentioned user.</summary>
    public string? AccessLevel { get; init; }

    /// <summary>Gets the type of the mentioned user.</summary>
    public string? UserType { get; init; }
}
