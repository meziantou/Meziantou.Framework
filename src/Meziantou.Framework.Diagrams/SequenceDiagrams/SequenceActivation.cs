namespace Meziantou.Framework.Diagrams;

/// <summary>The start or the end of an activation of a participant of a sequence diagram: a bar on its lifeline (Mermaid <c>activate</c> and <c>deactivate</c>).</summary>
public sealed class SequenceActivation : SequenceStep
{
    /// <summary>Initializes a new instance of the <see cref="SequenceActivation"/> class.</summary>
    /// <param name="participant">The participant.</param>
    /// <param name="isActivation">Whether the activation starts; otherwise, it ends.</param>
    public SequenceActivation(SequenceParticipant participant, bool isActivation = true)
    {
        ArgumentNullException.ThrowIfNull(participant);
        Participant = participant;
        IsActivation = isActivation;
    }

    /// <summary>Gets the participant.</summary>
    public SequenceParticipant Participant { get; }

    /// <summary>Gets or sets a value that indicates whether the activation starts; otherwise, it ends. The activations of a participant nest.</summary>
    public bool IsActivation { get; set; }
}
