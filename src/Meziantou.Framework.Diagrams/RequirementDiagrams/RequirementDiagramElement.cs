namespace Meziantou.Framework.Diagrams;

/// <summary>An element of a requirement diagram: a part of another document, such as a test suite, that requirements relate to.</summary>
public sealed class RequirementDiagramElement : RequirementDiagramNode
{
    /// <summary>Initializes a new instance of the <see cref="RequirementDiagramElement"/> class.</summary>
    /// <param name="name">The name, unique in the diagram.</param>
    public RequirementDiagramElement(string name)
        : base(name)
    {
    }

    /// <summary>Gets or sets the type of the element, such as <c>test suite</c>, or <see langword="null"/> for none.</summary>
    public DiagramText? Type { get; set; }

    /// <summary>Gets or sets the reference of the document, such as a path, or <see langword="null"/> for none.</summary>
    public DiagramText? DocumentReference { get; set; }
}
