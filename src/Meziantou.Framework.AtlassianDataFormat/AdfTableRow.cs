namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a table row.</summary>
public sealed class AdfTableRow : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.TableRow;
}
