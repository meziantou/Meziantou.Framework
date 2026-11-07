namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a column of a multi-column layout.</summary>
public sealed class AdfLayoutColumn : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.LayoutColumn;

    /// <summary>Gets the width of the column, as a percentage.</summary>
    public double? Width { get; init; }
}
