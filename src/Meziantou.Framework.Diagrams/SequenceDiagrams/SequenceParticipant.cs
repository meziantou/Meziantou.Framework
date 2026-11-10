namespace Meziantou.Framework.Diagrams;

/// <summary>A participant of a sequence diagram: a box, a person or a symbol at the top of its lifeline.</summary>
public sealed class SequenceParticipant
{
    /// <summary>Initializes a new instance of the <see cref="SequenceParticipant"/> class.</summary>
    /// <param name="id">The identifier, unique in the diagram, which is shown when there is no label.</param>
    /// <param name="label">The name shown, or <see langword="null"/> to show the identifier.</param>
    public SequenceParticipant(string id, DiagramText? label = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        Label = label;
    }

    /// <summary>Gets the identifier, unique in the diagram.</summary>
    public string Id { get; }

    /// <summary>Gets or sets the name shown, or <see langword="null"/> to show the identifier (Mermaid alias).</summary>
    public DiagramText? Label { get; set; }

    /// <summary>Gets or sets how the participant is drawn.</summary>
    public SequenceParticipantKind Kind { get; set; }

    /// <summary>Returns the identifier.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => Id;
}
