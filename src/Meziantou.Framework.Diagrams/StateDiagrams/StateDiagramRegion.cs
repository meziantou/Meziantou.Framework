namespace Meziantou.Framework.Diagrams;

/// <summary>A region of a composite state: the states of a composite state, or one of its concurrent parts.</summary>
public sealed class StateDiagramRegion
{
    /// <summary>Gets the states of the region.</summary>
    public IList<StateDiagramState> States { get; } = new List<StateDiagramState>();
}
