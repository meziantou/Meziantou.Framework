namespace Meziantou.Framework.Diagrams;

/// <summary>A note of a class diagram, alone or attached to a class.</summary>
public sealed class ClassDiagramNote
{
    /// <summary>Initializes a new instance of the <see cref="ClassDiagramNote"/> class.</summary>
    /// <param name="text">The text of the note.</param>
    /// <param name="class">The class the note is attached to, or <see langword="null"/> for a note alone.</param>
    public ClassDiagramNote(DiagramText text, ClassDiagramClass? @class = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        Class = @class;
    }

    /// <summary>Gets or sets the text of the note.</summary>
    public DiagramText Text { get; set; }

    /// <summary>Gets or sets the class the note is attached to, or <see langword="null"/> for a note alone.</summary>
    public ClassDiagramClass? Class { get; set; }
}
