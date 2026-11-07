namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a group of media items rendered as a list of attachments.</summary>
public sealed class AdfMediaGroup : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.MediaGroup;
}
