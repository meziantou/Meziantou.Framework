namespace Meziantou.Framework.Diagrams;

/// <summary>An interface of a class diagram drawn as a lollipop: a small circle with its name, linked to the classes that provide it.</summary>
public sealed class ClassDiagramInterface : ClassDiagramElement
{
    /// <summary>Initializes a new instance of the <see cref="ClassDiagramInterface"/> class.</summary>
    /// <param name="id">The identifier, which is the name shown when there is no label.</param>
    /// <param name="label">The name shown, or <see langword="null"/> to show the identifier.</param>
    public ClassDiagramInterface(string id, DiagramText? label = null)
        : base(id)
    {
        Label = label;
    }
}
