namespace Meziantou.Framework.Diagrams;

/// <summary>A section of a block of a sequence diagram: a text, such as the condition of an alternative, and steps.</summary>
public sealed class SequenceBlockSection
{
    /// <summary>Initializes a new instance of the <see cref="SequenceBlockSection"/> class.</summary>
    /// <param name="label">The text, or <see langword="null"/> for none.</param>
    public SequenceBlockSection(DiagramText? label = null)
    {
        Label = label;
    }

    /// <summary>Gets or sets the text, shown between brackets, or <see langword="null"/> for none.</summary>
    public DiagramText? Label { get; set; }

    /// <summary>Gets the steps of the section.</summary>
    public IList<SequenceStep> Steps { get; } = new List<SequenceStep>();
}
