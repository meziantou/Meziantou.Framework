namespace Meziantou.Framework.Diagrams;

/// <summary>A block of a sequence diagram: a frame around steps, such as a loop, or the sections of an alternative.</summary>
public sealed class SequenceBlock : SequenceStep
{
    /// <summary>Initializes a new instance of the <see cref="SequenceBlock"/> class.</summary>
    /// <param name="kind">The kind.</param>
    public SequenceBlock(SequenceBlockKind kind)
    {
        Kind = kind;
    }

    /// <summary>Gets or sets the kind, shown in the corner of the frame.</summary>
    public SequenceBlockKind Kind { get; set; }

    /// <summary>
    /// Gets the sections, separated by dashed lines: the alternatives of <see cref="SequenceBlockKind.Alternative"/>
    /// (Mermaid <c>else</c>), the branches of <see cref="SequenceBlockKind.Parallel"/> (<c>and</c>), the options of
    /// <see cref="SequenceBlockKind.Critical"/> (<c>option</c>). The other kinds have one section.
    /// </summary>
    public IList<SequenceBlockSection> Sections { get; } = new List<SequenceBlockSection>();

    /// <summary>Gets or sets the background of a <see cref="SequenceBlockKind.Rectangle"/>, or <see langword="null"/> for the default.</summary>
    public SceneColor? Color { get; set; }
}
