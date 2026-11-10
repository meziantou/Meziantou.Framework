using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A use case diagram, in the style of UML: actors, the use cases of a system they take part in, the system
/// boundaries, and the relationships between them (Mermaid <c>usecase-beta</c>).
/// </summary>
public sealed class UseCaseDiagram : Diagram
{
    /// <summary>Gets or sets the direction of the relationships. The default is <see cref="DiagramDirection.LeftToRight"/>.</summary>
    public DiagramDirection Direction { get; set; } = DiagramDirection.LeftToRight;

    /// <summary>Gets the actors. The actors of the boundaries that are not in this list are drawn too.</summary>
    public IList<UseCaseActor> Actors { get; } = new List<UseCaseActor>();

    /// <summary>Gets the use cases. The use cases of the boundaries that are not in this list are drawn too.</summary>
    public IList<UseCase> UseCases { get; } = new List<UseCase>();

    /// <summary>Gets the tables that show JSON data.</summary>
    public IList<UseCaseJsonTable> Tables { get; } = new List<UseCaseJsonTable>();

    /// <summary>Gets the system boundaries.</summary>
    public IList<UseCaseBoundary> Boundaries { get; } = new List<UseCaseBoundary>();

    /// <summary>Gets the relationships, drawn in order.</summary>
    public IList<UseCaseRelationship> Relationships { get; } = new List<UseCaseRelationship>();

    /// <summary>Gets the notes.</summary>
    public IList<UseCaseNote> Notes { get; } = new List<UseCaseNote>();

    /// <summary>Gets the styles of the CSS classes of the elements, by name (Mermaid <c>classDef</c>). The class <c>default</c> applies to every actor, use case and table.</summary>
    public IDictionary<string, DiagramStyle> ClassDefinitions { get; } = new Dictionary<string, DiagramStyle>(StringComparer.Ordinal);

    /// <summary>Gets or sets the space between two elements of the same rank, in pixels.</summary>
    public double NodeSpacing { get; set; } = 50;

    /// <summary>Gets or sets the space between two ranks, in pixels.</summary>
    public double RankSpacing { get; set; } = 50;

    /// <summary>Adds an actor.</summary>
    /// <param name="id">The identifier, which is shown when there is no label.</param>
    /// <param name="label">The name shown, or <see langword="null"/> to show the identifier.</param>
    /// <returns>The actor.</returns>
    public UseCaseActor AddActor(string id, DiagramText? label = null)
    {
        var actor = new UseCaseActor(id, label);
        Actors.Add(actor);
        return actor;
    }

    /// <summary>Adds a use case.</summary>
    /// <param name="id">The identifier, which is shown when there is no label.</param>
    /// <param name="label">The text shown, or <see langword="null"/> to show the identifier.</param>
    /// <param name="shape">The shape.</param>
    /// <returns>The use case.</returns>
    public UseCase AddUseCase(string id, DiagramText? label = null, UseCaseShape shape = UseCaseShape.Ellipse)
    {
        var useCase = new UseCase(id, label) { Shape = shape };
        UseCases.Add(useCase);
        return useCase;
    }

    /// <summary>Adds a table that shows JSON data.</summary>
    /// <param name="id">The identifier, shown as the title of the table.</param>
    /// <param name="json">The data: a JSON object.</param>
    /// <returns>The table.</returns>
    public UseCaseJsonTable AddTable(string id, string json)
    {
        var table = new UseCaseJsonTable(id, json);
        Tables.Add(table);
        return table;
    }

    /// <summary>Adds a system boundary.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="title">The title, or <see langword="null"/> to show the identifier.</param>
    /// <returns>The boundary.</returns>
    public UseCaseBoundary AddBoundary(string id, DiagramText? title = null)
    {
        var boundary = new UseCaseBoundary(id, title);
        Boundaries.Add(boundary);
        return boundary;
    }

    /// <summary>Adds a relationship.</summary>
    /// <param name="from">The source.</param>
    /// <param name="to">The target.</param>
    /// <param name="kind">The kind. An association has an arrowhead at <paramref name="to"/>.</param>
    /// <param name="label">The text of an association, or <see langword="null"/> for none.</param>
    /// <returns>The relationship.</returns>
    public UseCaseRelationship AddRelationship(UseCaseDiagramElement from, UseCaseDiagramElement to, UseCaseRelationshipKind kind = UseCaseRelationshipKind.Association, DiagramText? label = null)
    {
        var relationship = new UseCaseRelationship(from, to, kind) { Label = label };
        if (kind is UseCaseRelationshipKind.Association)
        {
            relationship.ToArrowHead = UseCaseArrowHead.Arrow;
        }

        Relationships.Add(relationship);
        return relationship;
    }

    /// <summary>Adds a note.</summary>
    /// <param name="target">The actor or the use case the note is attached to.</param>
    /// <param name="text">The text.</param>
    /// <returns>The note.</returns>
    public UseCaseNote AddNote(UseCaseDiagramElement target, DiagramText text)
    {
        var note = new UseCaseNote(target, text);
        Notes.Add(note);
        return note;
    }

    /// <summary>Finds an actor, a use case or a table by identifier, in the diagram and its boundaries.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The element, or <see langword="null"/> when there is none.</returns>
    public UseCaseDiagramElement? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        foreach (var element in Actors.Cast<UseCaseDiagramElement>().Concat(UseCases).Concat(Tables).Concat(Boundaries.SelectMany(boundary => boundary.Elements)))
        {
            if (string.Equals(element.Id, id, StringComparison.Ordinal))
                return element;
        }

        return null;
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => UseCaseDiagramRenderer.Render(this, context, group);
}
