namespace Meziantou.Framework.Diagrams;

/// <summary>An element of a use case diagram that relationships can link: an actor, a use case or a table.</summary>
public abstract class UseCaseDiagramElement
{
    private protected UseCaseDiagramElement(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
    }

    /// <summary>Gets the identifier of the element, unique in the diagram. It is the text shown when there is no <see cref="Label"/>.</summary>
    public string Id { get; }

    /// <summary>Gets or sets the text shown, or <see langword="null"/> to show the identifier.</summary>
    public DiagramText? Label { get; set; }

    /// <summary>Gets or sets the style of the element, which takes precedence over the style of its classes.</summary>
    public DiagramStyle? Style { get; set; }

    /// <summary>Gets the CSS classes of the element: the names of styles of <see cref="UseCaseDiagram.ClassDefinitions"/>.</summary>
    public IList<string> CssClasses { get; } = new List<string>();

    /// <summary>Returns the identifier of the element.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => Id;
}
