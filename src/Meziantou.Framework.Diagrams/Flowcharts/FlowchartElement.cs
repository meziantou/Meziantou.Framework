namespace Meziantou.Framework.Diagrams;

/// <summary>An element of a flowchart that edges can link: a node or a subgraph.</summary>
public abstract class FlowchartElement
{
    private protected FlowchartElement(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
    }

    /// <summary>Gets the identifier of the element, unique in the flowchart.</summary>
    public string Id { get; }

    /// <summary>Gets or sets the text of the element, or <see langword="null"/> to show its identifier.</summary>
    public DiagramText? Label { get; set; }

    /// <summary>Gets or sets the style of the element, which takes precedence over the style of its classes.</summary>
    public DiagramStyle? Style { get; set; }

    /// <summary>Gets the classes of the element: the names of styles of <see cref="FlowchartDiagram.ClassDefinitions"/>, also written as CSS classes in the SVG.</summary>
    public IList<string> CssClasses { get; } = new List<string>();

    /// <summary>Gets or sets the address the element links to in the SVG, or <see langword="null"/> for none.</summary>
    public string? Link { get; set; }

    /// <summary>Gets or sets the text shown when hovering the element in the SVG, or <see langword="null"/> for none.</summary>
    public string? Tooltip { get; set; }

    /// <summary>Returns the identifier of the element.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => Id;
}
