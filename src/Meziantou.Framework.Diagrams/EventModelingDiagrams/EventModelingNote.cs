namespace Meziantou.Framework.Diagrams;

/// <summary>A note of an event model, attached to a frame (Mermaid <c>note 01 { ... }</c>). Mermaid does not draw it.</summary>
public sealed class EventModelingNote
{
    /// <summary>Initializes a new instance of the <see cref="EventModelingNote"/> class.</summary>
    /// <param name="frame">The frame the note is attached to.</param>
    /// <param name="data">The text of the note.</param>
    public EventModelingNote(EventModelingFrame frame, EventModelingData data)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(data);
        Frame = frame;
        Data = data;
    }

    /// <summary>Gets or sets the frame the note is attached to.</summary>
    public EventModelingFrame Frame { get; set; }

    /// <summary>Gets or sets the text of the note.</summary>
    public EventModelingData Data { get; set; }
}
