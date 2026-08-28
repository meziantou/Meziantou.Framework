namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents the caption of a media item.</summary>
public sealed class AdfCaption : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.Caption;
}
