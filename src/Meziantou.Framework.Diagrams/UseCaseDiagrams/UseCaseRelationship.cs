namespace Meziantou.Framework.Diagrams;

/// <summary>A relationship between two elements of a use case diagram: an association, an include, an extend or a generalization.</summary>
public sealed class UseCaseRelationship
{
    /// <summary>Initializes a new instance of the <see cref="UseCaseRelationship"/> class.</summary>
    /// <param name="from">The source.</param>
    /// <param name="to">The target.</param>
    /// <param name="kind">The kind.</param>
    public UseCaseRelationship(UseCaseDiagramElement from, UseCaseDiagramElement to, UseCaseRelationshipKind kind = UseCaseRelationshipKind.Association)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        From = from;
        To = to;
        Kind = kind;
    }

    /// <summary>Gets the source: the use case that includes or extends, or the specialized element of a generalization.</summary>
    public UseCaseDiagramElement From { get; }

    /// <summary>Gets the target.</summary>
    public UseCaseDiagramElement To { get; }

    /// <summary>Gets or sets the kind of the relationship.</summary>
    public UseCaseRelationshipKind Kind { get; set; }

    /// <summary>Gets or sets the decoration of an association at <see cref="From"/>.</summary>
    public UseCaseArrowHead FromArrowHead { get; set; }

    /// <summary>Gets or sets the decoration of an association at <see cref="To"/>.</summary>
    public UseCaseArrowHead ToArrowHead { get; set; }

    /// <summary>Gets or sets the text of an association, or <see langword="null"/> for none. An include or an extend shows its kind.</summary>
    public DiagramText? Label { get; set; }

    /// <summary>Gets or sets the minimum number of ranks the association spans. The default is 1.</summary>
    public int MinLength { get; set; } = 1;

    /// <summary>Gets or sets the identifier of the relationship, which <c>class</c> and <c>style</c> statements of Mermaid use, or <see langword="null"/> for none.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the style of the line.</summary>
    public DiagramStyle? Style { get; set; }

    /// <summary>Gets the CSS classes of the relationship.</summary>
    public IList<string> CssClasses { get; } = new List<string>();
}
