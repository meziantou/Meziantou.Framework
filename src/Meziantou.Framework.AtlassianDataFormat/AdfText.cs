namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a run of text.</summary>
public sealed class AdfText : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.Text;

    /// <summary>Gets the text of the node.</summary>
    public required string Text { get; init; }
}
