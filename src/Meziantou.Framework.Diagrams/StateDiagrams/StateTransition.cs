namespace Meziantou.Framework.Diagrams;

/// <summary>A transition from a state to another.</summary>
public sealed class StateTransition
{
    /// <summary>Initializes a new instance of the <see cref="StateTransition"/> class.</summary>
    /// <param name="from">The state the transition leaves.</param>
    /// <param name="to">The state the transition enters.</param>
    /// <param name="label">The text of the transition, such as its event, or <see langword="null"/> for none.</param>
    public StateTransition(StateDiagramState from, StateDiagramState to, DiagramText? label = null)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        From = from;
        To = to;
        Label = label;
    }

    /// <summary>Gets the state the transition leaves.</summary>
    public StateDiagramState From { get; }

    /// <summary>Gets the state the transition enters.</summary>
    public StateDiagramState To { get; }

    /// <summary>Gets or sets the text of the transition, or <see langword="null"/> for none.</summary>
    public DiagramText? Label { get; set; }
}
