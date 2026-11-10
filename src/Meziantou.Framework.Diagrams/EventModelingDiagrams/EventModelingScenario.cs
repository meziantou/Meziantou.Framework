namespace Meziantou.Framework.Diagrams;

/// <summary>A given/when/then scenario of a frame of an event model (Mermaid <c>gwt</c>). Mermaid does not draw it.</summary>
public sealed class EventModelingScenario
{
    /// <summary>Initializes a new instance of the <see cref="EventModelingScenario"/> class.</summary>
    /// <param name="frame">The frame the scenario describes.</param>
    public EventModelingScenario(EventModelingFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Frame = frame;
    }

    /// <summary>Gets or sets the frame the scenario describes.</summary>
    public EventModelingFrame Frame { get; set; }

    /// <summary>Gets the entities of the initial state. A scenario has one at least.</summary>
    public IList<EventModelingStep> Given { get; } = new List<EventModelingStep>();

    /// <summary>Gets the entities of the action, or an empty list for none.</summary>
    public IList<EventModelingStep> When { get; } = new List<EventModelingStep>();

    /// <summary>Gets the entities of the outcome. A scenario has one at least.</summary>
    public IList<EventModelingStep> Then { get; } = new List<EventModelingStep>();
}
