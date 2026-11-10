namespace Meziantou.Framework.Diagrams;

/// <summary>A note next to a state.</summary>
public sealed class StateNote
{
    /// <summary>Initializes a new instance of the <see cref="StateNote"/> class.</summary>
    /// <param name="state">The state the note is about.</param>
    /// <param name="text">The text of the note.</param>
    /// <param name="position">The side of the state the note is on.</param>
    public StateNote(StateDiagramState state, DiagramText text, StateNotePosition position = StateNotePosition.Right)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(text);
        State = state;
        Text = text;
        Position = position;
    }

    /// <summary>Gets the state the note is about.</summary>
    public StateDiagramState State { get; }

    /// <summary>Gets or sets the text of the note.</summary>
    public DiagramText Text { get; set; }

    /// <summary>Gets or sets the side of the state the note is on.</summary>
    public StateNotePosition Position { get; set; }
}
