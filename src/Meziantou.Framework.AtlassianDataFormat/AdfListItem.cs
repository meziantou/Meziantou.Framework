namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents an item of a bullet or ordered list.</summary>
public sealed class AdfListItem : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.ListItem;
}
