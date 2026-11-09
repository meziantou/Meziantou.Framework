namespace Meziantou.Framework.Diagrams;

/// <summary>A message of a sequence diagram: an arrow from the lifeline of a participant to another one, with a text.</summary>
public sealed class SequenceMessage : SequenceStep
{
    /// <summary>Initializes a new instance of the <see cref="SequenceMessage"/> class.</summary>
    /// <param name="from">The sender.</param>
    /// <param name="to">The receiver, which can be the sender.</param>
    public SequenceMessage(SequenceParticipant from, SequenceParticipant to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        From = from;
        To = to;
    }

    /// <summary>Gets the sender.</summary>
    public SequenceParticipant From { get; }

    /// <summary>Gets the receiver.</summary>
    public SequenceParticipant To { get; }

    /// <summary>Gets or sets the text, or <see langword="null"/> for none.</summary>
    public DiagramText? Text { get; set; }

    /// <summary>Gets or sets the style of the line.</summary>
    public SequenceLineStyle LineStyle { get; set; }

    /// <summary>Gets or sets the decoration at the sender.</summary>
    public SequenceArrowHead StartArrowHead { get; set; }

    /// <summary>Gets or sets the decoration at the receiver.</summary>
    public SequenceArrowHead EndArrowHead { get; set; }

    /// <summary>Gets or sets a value that indicates whether the message activates the receiver (Mermaid <c>+</c>).</summary>
    public bool ActivatesReceiver { get; set; }

    /// <summary>Gets or sets a value that indicates whether the message deactivates the sender (Mermaid <c>-</c>).</summary>
    public bool DeactivatesSender { get; set; }

    /// <summary>Gets or sets a value that indicates whether the message starts at a circle on the lifeline of the sender (Mermaid <c>()</c>).</summary>
    public bool SenderConnection { get; set; }

    /// <summary>Gets or sets a value that indicates whether the message ends at a circle on the lifeline of the receiver (Mermaid <c>()</c>).</summary>
    public bool ReceiverConnection { get; set; }

    /// <summary>Gets or sets a value that indicates whether the message creates the receiver, which appears at the message instead of the top (Mermaid <c>create</c>).</summary>
    public bool CreatesReceiver { get; set; }

    /// <summary>Gets or sets the participant the message destroys, the sender or the receiver, whose lifeline ends at the message (Mermaid <c>destroy</c>), or <see langword="null"/> for none.</summary>
    public SequenceParticipant? DestroyedParticipant { get; set; }
}
