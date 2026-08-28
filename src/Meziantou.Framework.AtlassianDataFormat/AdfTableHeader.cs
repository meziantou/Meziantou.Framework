namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a table header cell.</summary>
public sealed class AdfTableHeader : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.TableHeader;

    /// <summary>Gets the number of columns the cell spans.</summary>
    public int? ColSpan { get; init; }

    /// <summary>Gets the number of rows the cell spans.</summary>
    public int? RowSpan { get; init; }

    /// <summary>Gets the background color of the cell.</summary>
    public string? Background { get; init; }

    /// <summary>Gets the vertical alignment of the cell content.</summary>
    public string? VerticalAlignment { get; init; }
}
