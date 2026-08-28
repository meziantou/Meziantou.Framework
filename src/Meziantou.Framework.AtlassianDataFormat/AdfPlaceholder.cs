namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents editor-only placeholder text.</summary>
public sealed class AdfPlaceholder : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.Placeholder;

    /// <summary>Gets the placeholder text.</summary>
    public required string Text { get; init; }
}
