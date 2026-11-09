namespace Meziantou.Framework.Diagrams;

/// <summary>How a participant of a sequence diagram is drawn.</summary>
public enum SequenceParticipantKind
{
    /// <summary>A box with the name (Mermaid <c>participant</c>).</summary>
    Participant,

    /// <summary>A person: a stick figure with the name below (Mermaid <c>actor</c>).</summary>
    Actor,

    /// <summary>The boundary symbol of UML: a circle on a vertical line.</summary>
    Boundary,

    /// <summary>The control symbol of UML: a circle with an arrowhead.</summary>
    Control,

    /// <summary>The entity symbol of UML: a circle on a horizontal line.</summary>
    Entity,

    /// <summary>A database: a cylinder.</summary>
    Database,

    /// <summary>A collection: stacked boxes.</summary>
    Collections,

    /// <summary>A queue: a horizontal cylinder.</summary>
    Queue,
}
