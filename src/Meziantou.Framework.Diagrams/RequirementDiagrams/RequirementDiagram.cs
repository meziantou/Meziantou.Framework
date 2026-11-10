using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A requirement diagram, in the style of SysML: requirements, the elements that satisfy or verify them, and the
/// relationships between them (Mermaid <c>requirementDiagram</c>).
/// </summary>
public sealed class RequirementDiagram : Diagram
{
    /// <summary>Gets or sets the direction of the relationships. The default is <see cref="DiagramDirection.TopToBottom"/>.</summary>
    public DiagramDirection Direction { get; set; }

    /// <summary>Gets the requirements.</summary>
    public IList<RequirementDiagramRequirement> Requirements { get; } = new List<RequirementDiagramRequirement>();

    /// <summary>Gets the elements: the parts of other documents that requirements relate to.</summary>
    public IList<RequirementDiagramElement> Elements { get; } = new List<RequirementDiagramElement>();

    /// <summary>Gets the relationships, drawn in order.</summary>
    public IList<RequirementRelationship> Relationships { get; } = new List<RequirementRelationship>();

    /// <summary>Gets the styles of the CSS classes of the nodes, by name (Mermaid <c>classDef</c>). The class <c>default</c> applies to every node.</summary>
    public IDictionary<string, DiagramStyle> ClassDefinitions { get; } = new Dictionary<string, DiagramStyle>(StringComparer.Ordinal);

    /// <summary>Adds a requirement.</summary>
    /// <param name="name">The name, unique in the diagram.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>The requirement.</returns>
    public RequirementDiagramRequirement AddRequirement(string name, RequirementKind kind = RequirementKind.Requirement)
    {
        var requirement = new RequirementDiagramRequirement(name) { Kind = kind };
        Requirements.Add(requirement);
        return requirement;
    }

    /// <summary>Adds an element.</summary>
    /// <param name="name">The name, unique in the diagram.</param>
    /// <returns>The element.</returns>
    public RequirementDiagramElement AddElement(string name)
    {
        var element = new RequirementDiagramElement(name);
        Elements.Add(element);
        return element;
    }

    /// <summary>Adds a relationship.</summary>
    /// <param name="source">The source, such as the element that satisfies a requirement.</param>
    /// <param name="target">The target.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>The relationship.</returns>
    public RequirementRelationship AddRelationship(RequirementDiagramNode source, RequirementDiagramNode target, RequirementRelationshipKind kind)
    {
        var relationship = new RequirementRelationship(source, target, kind);
        Relationships.Add(relationship);
        return relationship;
    }

    /// <summary>Finds a requirement or an element by name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The node, or <see langword="null"/> when there is none.</returns>
    public RequirementDiagramNode? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var requirement in Requirements)
        {
            if (string.Equals(requirement.Name, name, StringComparison.Ordinal))
                return requirement;
        }

        foreach (var element in Elements)
        {
            if (string.Equals(element.Name, name, StringComparison.Ordinal))
                return element;
        }

        return null;
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => RequirementDiagramRenderer.Render(this, context, group);
}
