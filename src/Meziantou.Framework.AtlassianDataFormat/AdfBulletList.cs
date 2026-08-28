namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents an unordered list.</summary>
public sealed class AdfBulletList : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.BulletList;
}
