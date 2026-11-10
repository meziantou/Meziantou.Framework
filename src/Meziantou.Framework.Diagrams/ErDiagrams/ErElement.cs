namespace Meziantou.Framework.Diagrams;

/// <summary>An element of an entity relationship diagram that relationships can link: an entity or a subgraph.</summary>
public abstract class ErElement
{
    private protected ErElement(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
    }

    /// <summary>Gets the identifier of the element, unique in the diagram. It is the name shown when there is no <see cref="Label"/>.</summary>
    public string Id { get; }

    /// <summary>Gets or sets the name shown, or <see langword="null"/> to show the identifier.</summary>
    public DiagramText? Label { get; set; }

    /// <summary>Gets or sets the style of the element, which takes precedence over the style of its classes.</summary>
    public DiagramStyle? Style { get; set; }

    /// <summary>Gets the CSS classes of the element: the names of styles of <see cref="ErDiagram.ClassDefinitions"/>.</summary>
    public IList<string> CssClasses { get; } = new List<string>();

    /// <summary>Returns the identifier of the element.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => Id;
}
