namespace Meziantou.Framework.Diagrams;

/// <summary>A relationship of a C4 diagram: a line from an item to another, with its text and its technology.</summary>
public sealed class C4Relationship
{
    /// <summary>Initializes a new instance of the <see cref="C4Relationship"/> class.</summary>
    /// <param name="from">The source.</param>
    /// <param name="to">The target.</param>
    /// <param name="label">The text.</param>
    public C4Relationship(C4Item from, C4Item to, DiagramText label)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(label);
        From = from;
        To = to;
        Label = label;
    }

    /// <summary>Gets the source.</summary>
    public C4Item From { get; }

    /// <summary>Gets the target.</summary>
    public C4Item To { get; }

    /// <summary>Gets or sets the text.</summary>
    public DiagramText Label { get; set; }

    /// <summary>Gets or sets the kind, which sets the arrowheads. The direction hints of Mermaid (<c>Rel_U</c>...) do not move the items.</summary>
    public C4RelationshipKind Kind { get; set; }

    /// <summary>Gets or sets the technology, shown between brackets below the text, or <see langword="null"/> for none.</summary>
    public string? Technology { get; set; }

    /// <summary>Gets or sets the description, written to Mermaid but not drawn, or <see langword="null"/> for none.</summary>
    public DiagramText? Description { get; set; }

    /// <summary>Gets or sets the colors of the line (<see cref="DiagramStyle.Stroke"/>) and of the text (<see cref="DiagramStyle.TextColor"/>) (Mermaid <c>UpdateRelStyle</c>).</summary>
    public DiagramStyle? Style { get; set; }

    /// <summary>Gets or sets how far the text is moved right from the middle of the line, in pixels.</summary>
    public double LabelOffsetX { get; set; }

    /// <summary>Gets or sets how far the text is moved down from the middle of the line, in pixels.</summary>
    public double LabelOffsetY { get; set; }

    /// <summary>Gets or sets the tags, written to Mermaid but not drawn, or <see langword="null"/> for none.</summary>
    public string? Tags { get; set; }

    /// <summary>Gets or sets the address of the relationship, written to Mermaid but not drawn, or <see langword="null"/> for none.</summary>
    public string? Link { get; set; }
}
