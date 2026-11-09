namespace Meziantou.Framework.Diagrams;

/// <summary>The kind of the entity of a frame of an event model.</summary>
public enum EventModelingEntityKind
{
    /// <summary>A screen, in the UI/Automation swimlane (Mermaid <c>ui</c>).</summary>
    Ui,

    /// <summary>A processor, an automation, in the UI/Automation swimlane (Mermaid <c>pcr</c> or <c>processor</c>).</summary>
    Processor,

    /// <summary>A command, in the Command/Read Model swimlane (Mermaid <c>cmd</c> or <c>command</c>).</summary>
    Command,

    /// <summary>A read model, a view, in the Command/Read Model swimlane (Mermaid <c>rmo</c> or <c>readmodel</c>).</summary>
    ReadModel,

    /// <summary>An event, in the Events swimlane (Mermaid <c>evt</c> or <c>event</c>).</summary>
    Event,
}
