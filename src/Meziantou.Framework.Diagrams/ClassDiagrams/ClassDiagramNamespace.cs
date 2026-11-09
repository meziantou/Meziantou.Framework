namespace Meziantou.Framework.Diagrams;

/// <summary>A group of classes of a class diagram, drawn as a frame with a title.</summary>
public sealed class ClassDiagramNamespace
{
    /// <summary>Initializes a new instance of the <see cref="ClassDiagramNamespace"/> class.</summary>
    /// <param name="id">The identifier, which is the title shown when there is no label.</param>
    /// <param name="label">The title, or <see langword="null"/> to show the identifier.</param>
    public ClassDiagramNamespace(string id, DiagramText? label = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        Label = label;
    }

    /// <summary>Gets the identifier of the namespace.</summary>
    public string Id { get; }

    /// <summary>Gets or sets the title, or <see langword="null"/> to show the identifier.</summary>
    public DiagramText? Label { get; set; }

    /// <summary>Gets the classes of the namespace. A class of several namespaces belongs to the innermost one.</summary>
    public IList<ClassDiagramClass> Classes { get; } = new List<ClassDiagramClass>();

    /// <summary>Gets the namespaces nested in this one.</summary>
    public IList<ClassDiagramNamespace> Namespaces { get; } = new List<ClassDiagramNamespace>();
}
