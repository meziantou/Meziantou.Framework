namespace Meziantou.Framework.Diagrams;

/// <summary>A state of a state diagram. A state with regions is a composite state, drawn as a frame around them.</summary>
public sealed class StateDiagramState
{
    /// <summary>Initializes a new instance of the <see cref="StateDiagramState"/> class.</summary>
    /// <param name="id">The identifier, unique in the diagram, which is the name shown when there is no label.</param>
    /// <param name="label">The name shown, or <see langword="null"/> to show the identifier.</param>
    /// <param name="kind">The kind of the state.</param>
    public StateDiagramState(string id, DiagramText? label = null, StateKind kind = StateKind.Normal)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        Label = label;
        Kind = kind;
    }

    /// <summary>Gets the identifier of the state.</summary>
    public string Id { get; }

    /// <summary>Gets or sets the name shown, or <see langword="null"/> to show the identifier.</summary>
    public DiagramText? Label { get; set; }

    /// <summary>Gets or sets the kind of the state.</summary>
    public StateKind Kind { get; set; }

    /// <summary>Gets the regions of a composite state: one for a composite state, several for concurrent parts (Mermaid <c>--</c>).</summary>
    public IList<StateDiagramRegion> Regions { get; } = new List<StateDiagramRegion>();

    /// <summary>Gets a value that indicates whether the state has regions.</summary>
    public bool IsComposite => Regions.Count > 0;

    /// <summary>Gets or sets the direction of the content of a composite state, or <see langword="null"/> for the direction of the diagram. As in Mermaid, it applies only when no transition crosses the border of the state.</summary>
    public DiagramDirection? Direction { get; set; }

    /// <summary>Gets or sets the style of the state, which takes precedence over the style of its classes.</summary>
    public DiagramStyle? Style { get; set; }

    /// <summary>Gets the CSS classes of the state: the names of styles of <see cref="StateDiagram.ClassDefinitions"/>.</summary>
    public IList<string> CssClasses { get; } = new List<string>();

    /// <summary>Adds a state to the last region of this state, which becomes a composite state.</summary>
    /// <param name="id">The identifier of the state, unique in the diagram.</param>
    /// <param name="label">The name shown, or <see langword="null"/> to show the identifier.</param>
    /// <param name="kind">The kind of the state.</param>
    /// <returns>The state.</returns>
    public StateDiagramState AddState(string id, DiagramText? label = null, StateKind kind = StateKind.Normal)
    {
        if (Regions.Count is 0)
        {
            Regions.Add(new StateDiagramRegion());
        }

        var state = new StateDiagramState(id, label, kind);
        Regions[^1].States.Add(state);
        return state;
    }

    /// <summary>Returns the identifier of the state.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => Id;
}
