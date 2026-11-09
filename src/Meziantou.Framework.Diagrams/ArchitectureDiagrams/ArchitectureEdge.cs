namespace Meziantou.Framework.Diagrams;

/// <summary>An edge of an architecture diagram, from a side of a node to a side of another node.</summary>
public sealed class ArchitectureEdge
{
    /// <summary>Initializes a new instance of the <see cref="ArchitectureEdge"/> class.</summary>
    /// <param name="from">The first node.</param>
    /// <param name="fromSide">The side of <paramref name="from"/> the edge leaves.</param>
    /// <param name="to">The second node.</param>
    /// <param name="toSide">The side of <paramref name="to"/> the edge reaches.</param>
    public ArchitectureEdge(ArchitectureNode from, ArchitectureSide fromSide, ArchitectureNode to, ArchitectureSide toSide)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        From = from;
        FromSide = fromSide;
        To = to;
        ToSide = toSide;
    }

    /// <summary>Gets the first node.</summary>
    public ArchitectureNode From { get; }

    /// <summary>Gets or sets the side of <see cref="From"/> the edge leaves.</summary>
    public ArchitectureSide FromSide { get; set; }

    /// <summary>Gets the second node.</summary>
    public ArchitectureNode To { get; }

    /// <summary>Gets or sets the side of <see cref="To"/> the edge reaches.</summary>
    public ArchitectureSide ToSide { get; set; }

    /// <summary>Gets or sets a value that indicates whether an arrowhead points to <see cref="From"/> (Mermaid <c>&lt;</c>).</summary>
    public bool FromArrow { get; set; }

    /// <summary>Gets or sets a value that indicates whether an arrowhead points to <see cref="To"/> (Mermaid <c>&gt;</c>).</summary>
    public bool ToArrow { get; set; }

    /// <summary>Gets or sets a value that indicates whether the edge starts at the frame of the group of <see cref="From"/> (Mermaid <c>{group}</c>).</summary>
    public bool FromGroup { get; set; }

    /// <summary>Gets or sets a value that indicates whether the edge ends at the frame of the group of <see cref="To"/> (Mermaid <c>{group}</c>).</summary>
    public bool ToGroup { get; set; }

    /// <summary>Gets or sets the text of the edge, or <see langword="null"/> for none.</summary>
    public DiagramText? Label { get; set; }
}
