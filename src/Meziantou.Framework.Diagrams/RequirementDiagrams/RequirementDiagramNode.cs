namespace Meziantou.Framework.Diagrams;

/// <summary>A node of a requirement diagram: a requirement or an element.</summary>
public abstract class RequirementDiagramNode
{
    private protected RequirementDiagramNode(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>Gets the name of the node, unique in the diagram.</summary>
    public string Name { get; }

    /// <summary>Gets or sets the style of the node, which takes precedence over the style of its classes.</summary>
    public DiagramStyle? Style { get; set; }

    /// <summary>Gets the CSS classes of the node: the names of styles of <see cref="RequirementDiagram.ClassDefinitions"/>.</summary>
    public IList<string> CssClasses { get; } = new List<string>();

    /// <summary>Returns the name of the node.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
