namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a horizontal rule.</summary>
public sealed class AdfRule : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.Rule;
}
