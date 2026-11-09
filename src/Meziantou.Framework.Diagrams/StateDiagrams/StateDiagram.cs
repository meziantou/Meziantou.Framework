using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A state diagram: states, linked by transitions, with start and end states, choices, forks and joins, and composite
/// states that hold other states, in concurrent regions or not (Mermaid <c>stateDiagram-v2</c>).
/// </summary>
public sealed class StateDiagram : Diagram
{
    /// <summary>Gets or sets the direction of the transitions. The default is <see cref="DiagramDirection.TopToBottom"/>.</summary>
    public DiagramDirection Direction { get; set; }

    /// <summary>Gets the top-level states.</summary>
    public IList<StateDiagramState> States { get; } = new List<StateDiagramState>();

    /// <summary>Gets the transitions, drawn in order.</summary>
    public IList<StateTransition> Transitions { get; } = new List<StateTransition>();

    /// <summary>Gets the notes.</summary>
    public IList<StateNote> Notes { get; } = new List<StateNote>();

    /// <summary>Gets the styles of the CSS classes of the states, by name (Mermaid <c>classDef</c>). The class <c>default</c> applies to every state.</summary>
    public IDictionary<string, DiagramStyle> ClassDefinitions { get; } = new Dictionary<string, DiagramStyle>(StringComparer.Ordinal);

    /// <summary>Adds a top-level state.</summary>
    /// <param name="id">The identifier of the state, unique in the diagram.</param>
    /// <param name="label">The name shown, or <see langword="null"/> to show the identifier.</param>
    /// <param name="kind">The kind of the state.</param>
    /// <returns>The state.</returns>
    public StateDiagramState AddState(string id, DiagramText? label = null, StateKind kind = StateKind.Normal)
    {
        var state = new StateDiagramState(id, label, kind);
        States.Add(state);
        return state;
    }

    /// <summary>Adds a transition.</summary>
    /// <param name="from">The state the transition leaves.</param>
    /// <param name="to">The state the transition enters.</param>
    /// <param name="label">The text of the transition, or <see langword="null"/> for none.</param>
    /// <returns>The transition.</returns>
    public StateTransition AddTransition(StateDiagramState from, StateDiagramState to, DiagramText? label = null)
    {
        var transition = new StateTransition(from, to, label);
        Transitions.Add(transition);
        return transition;
    }

    /// <summary>Adds a note next to a state.</summary>
    /// <param name="state">The state.</param>
    /// <param name="text">The text of the note.</param>
    /// <param name="position">The side of the state.</param>
    /// <returns>The note.</returns>
    public StateNote AddNote(StateDiagramState state, DiagramText text, StateNotePosition position = StateNotePosition.Right)
    {
        var note = new StateNote(state, text, position);
        Notes.Add(note);
        return note;
    }

    /// <summary>Finds a state by identifier, in the diagram and its composite states.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The state, or <see langword="null"/> when there is none.</returns>
    public StateDiagramState? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return EnumerateStates().FirstOrDefault(state => string.Equals(state.Id, id, StringComparison.Ordinal));
    }

    /// <summary>Enumerates the states and the states of their regions, depth first.</summary>
    /// <returns>The states.</returns>
    public IEnumerable<StateDiagramState> EnumerateStates()
    {
        var visited = new HashSet<StateDiagramState>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<StateDiagramState>(States.Reverse());
        while (stack.Count > 0)
        {
            var state = stack.Pop();
            if (!visited.Add(state))
                continue;

            yield return state;
            for (var i = state.Regions.Count - 1; i >= 0; i--)
            {
                for (var j = state.Regions[i].States.Count - 1; j >= 0; j--)
                {
                    stack.Push(state.Regions[i].States[j]);
                }
            }
        }
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => StateDiagramRenderer.Render(this, context, group);
}
