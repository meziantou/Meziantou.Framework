namespace Meziantou.Framework.Diagrams;

/// <summary>A system boundary of a use case diagram: a frame with a title around the actors and the use cases of a system.</summary>
public sealed class UseCaseBoundary
{
    /// <summary>Initializes a new instance of the <see cref="UseCaseBoundary"/> class.</summary>
    /// <param name="id">The identifier, unique in the diagram.</param>
    /// <param name="title">The title, or <see langword="null"/> to show the identifier.</param>
    public UseCaseBoundary(string id, DiagramText? title = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        Title = title;
    }

    /// <summary>Gets the identifier of the boundary, unique in the diagram.</summary>
    public string Id { get; }

    /// <summary>Gets or sets the title, or <see langword="null"/> to show the identifier.</summary>
    public DiagramText? Title { get; set; }

    /// <summary>Gets or sets the shape of the frame.</summary>
    public UseCaseBoundaryKind Kind { get; set; }

    /// <summary>Gets the actors and the use cases in the boundary. An element belongs to one boundary at most.</summary>
    public IList<UseCaseDiagramElement> Elements { get; } = new List<UseCaseDiagramElement>();

    /// <summary>Gets or sets the style of the boundary, which takes precedence over the style of its classes.</summary>
    public DiagramStyle? Style { get; set; }

    /// <summary>Gets the CSS classes of the boundary: the names of styles of <see cref="UseCaseDiagram.ClassDefinitions"/>.</summary>
    public IList<string> CssClasses { get; } = new List<string>();

    /// <summary>Returns the identifier of the boundary.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => Id;
}
