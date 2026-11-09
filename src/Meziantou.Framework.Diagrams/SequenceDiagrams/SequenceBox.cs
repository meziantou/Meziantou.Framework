namespace Meziantou.Framework.Diagrams;

/// <summary>A box of a sequence diagram: a colored background with a title behind a group of participants.</summary>
public sealed class SequenceBox
{
    /// <summary>Gets or sets the title, or <see langword="null"/> for none.</summary>
    public DiagramText? Title { get; set; }

    /// <summary>Gets or sets the color of the background, or <see langword="null"/> for a transparent box.</summary>
    public SceneColor? Color { get; set; }

    /// <summary>Gets the participants of the box. A participant belongs to one box at most.</summary>
    public IList<SequenceParticipant> Participants { get; } = new List<SequenceParticipant>();
}
