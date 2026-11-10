namespace Meziantou.Framework.Diagrams;

/// <summary>An alignment of an architecture diagram: services and junctions placed on a row or a column, in order (Mermaid <c>align</c>).</summary>
public sealed class ArchitectureAlignment
{
    /// <summary>Initializes a new instance of the <see cref="ArchitectureAlignment"/> class.</summary>
    /// <param name="isRow">Whether the members are on a row; otherwise, on a column.</param>
    public ArchitectureAlignment(bool isRow)
    {
        IsRow = isRow;
    }

    /// <summary>Gets or sets a value that indicates whether the members are on a row, left to right; otherwise, on a column, top to bottom.</summary>
    public bool IsRow { get; set; }

    /// <summary>Gets the members, in order.</summary>
    public IList<ArchitectureNode> Members { get; } = new List<ArchitectureNode>();
}
