namespace Meziantou.Framework.Diagrams;

/// <summary>A note of a sequence diagram, on a side of a participant or over one or two participants.</summary>
public sealed class SequenceNote : SequenceStep
{
    /// <summary>Initializes a new instance of the <see cref="SequenceNote"/> class.</summary>
    /// <param name="text">The text.</param>
    /// <param name="position">Where the note is, relative to the participants.</param>
    public SequenceNote(DiagramText text, SequenceNotePosition position)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        Position = position;
    }

    /// <summary>Gets or sets the text.</summary>
    public DiagramText Text { get; set; }

    /// <summary>Gets or sets where the note is, relative to the participants.</summary>
    public SequenceNotePosition Position { get; set; }

    /// <summary>Gets the participants: one, or two for a note over both.</summary>
    public IList<SequenceParticipant> Participants { get; } = new List<SequenceParticipant>();
}
