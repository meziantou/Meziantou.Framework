namespace Meziantou.Framework.Diagrams;

/// <summary>A group of entities of an entity relationship diagram, drawn as a frame with a title. Its <see cref="ErElement.Label"/> is its title.</summary>
public sealed class ErSubgraph : ErElement
{
    /// <summary>Initializes a new instance of the <see cref="ErSubgraph"/> class.</summary>
    /// <param name="id">The identifier, unique in the diagram.</param>
    /// <param name="title">The title, or <see langword="null"/> to show the identifier.</param>
    public ErSubgraph(string id, DiagramText? title = null)
        : base(id)
    {
        Label = title;
    }

    /// <summary>
    /// Gets or sets the direction of the content of the subgraph, or <see langword="null"/> for the direction of the
    /// diagram. It applies only when no relationship links an entity of the subgraph to an element outside of it.
    /// </summary>
    public DiagramDirection? Direction { get; set; }

    /// <summary>Gets the entities of the subgraph. An entity of several subgraphs belongs to the innermost one.</summary>
    public IList<ErEntity> Entities { get; } = new List<ErEntity>();

    /// <summary>Gets the subgraphs nested in this one.</summary>
    public IList<ErSubgraph> Subgraphs { get; } = new List<ErSubgraph>();
}
