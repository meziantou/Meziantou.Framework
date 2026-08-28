namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a block quotation.</summary>
public sealed class AdfBlockquote : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.Blockquote;
}
