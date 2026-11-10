namespace Meziantou.Framework.Diagrams;

/// <summary>A note of a use case diagram, attached to an actor or a use case by a dotted line.</summary>
public sealed class UseCaseNote
{
    /// <summary>Initializes a new instance of the <see cref="UseCaseNote"/> class.</summary>
    /// <param name="target">The actor or the use case the note is attached to.</param>
    /// <param name="text">The text.</param>
    public UseCaseNote(UseCaseDiagramElement target, DiagramText text)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(text);
        Target = target;
        Text = text;
    }

    /// <summary>Gets or sets the element the note is attached to.</summary>
    public UseCaseDiagramElement Target { get; set; }

    /// <summary>Gets or sets the text.</summary>
    public DiagramText Text { get; set; }
}
