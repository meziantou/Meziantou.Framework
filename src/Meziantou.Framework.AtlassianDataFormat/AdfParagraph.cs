namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a paragraph of inline content.</summary>
public sealed class AdfParagraph : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.Paragraph;
}
