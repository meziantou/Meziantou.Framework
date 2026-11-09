namespace Meziantou.Framework.Diagrams;

/// <summary>Where a note of a sequence diagram is, relative to its participants.</summary>
public enum SequenceNotePosition
{
    /// <summary>On the left of the participant (Mermaid <c>left of</c>).</summary>
    LeftOf,

    /// <summary>On the right of the participant (Mermaid <c>right of</c>).</summary>
    RightOf,

    /// <summary>Over the participant, or across two participants (Mermaid <c>over</c>).</summary>
    Over,
}
