namespace Meziantou.Framework.Diagrams;

/// <summary>A relation between two elements of a class diagram, such as an inheritance or a composition.</summary>
public sealed class ClassRelation
{
    /// <summary>Initializes a new instance of the <see cref="ClassRelation"/> class.</summary>
    /// <param name="from">The first element, written on the left in Mermaid.</param>
    /// <param name="to">The second element, written on the right in Mermaid.</param>
    public ClassRelation(ClassDiagramElement from, ClassDiagramElement to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        From = from;
        To = to;
    }

    /// <summary>Gets the first element.</summary>
    public ClassDiagramElement From { get; }

    /// <summary>Gets the second element.</summary>
    public ClassDiagramElement To { get; }

    /// <summary>Gets or sets the decoration of the end at <see cref="From"/>: <c>A &lt;|-- B</c> has an <see cref="ClassRelationEnd.Inheritance"/> at A.</summary>
    public ClassRelationEnd FromEnd { get; set; }

    /// <summary>Gets or sets the decoration of the end at <see cref="To"/>.</summary>
    public ClassRelationEnd ToEnd { get; set; }

    /// <summary>Gets or sets a value that indicates whether the line is dashed: a dependency or a realization (Mermaid <c>..</c>).</summary>
    public bool IsDashed { get; set; }

    /// <summary>Gets or sets the text in the middle of the relation, or <see langword="null"/> for none.</summary>
    public DiagramText? Label { get; set; }

    /// <summary>Gets or sets the multiplicity at <see cref="From"/>, such as <c>1</c> or <c>0..*</c>, or <see langword="null"/> for none.</summary>
    public string? FromCardinality { get; set; }

    /// <summary>Gets or sets the multiplicity at <see cref="To"/>, or <see langword="null"/> for none.</summary>
    public string? ToCardinality { get; set; }
}
