namespace Meziantou.Framework.Diagrams;

/// <summary>A group of nodes of a flowchart, drawn as a frame with a title. Its <see cref="FlowchartElement.Label"/> is its title.</summary>
public sealed class FlowchartSubgraph : FlowchartElement
{
    /// <summary>Initializes a new instance of the <see cref="FlowchartSubgraph"/> class.</summary>
    /// <param name="id">The identifier of the subgraph, unique in the flowchart.</param>
    /// <param name="title">The title, or <see langword="null"/> to show the identifier.</param>
    public FlowchartSubgraph(string id, DiagramText? title = null)
        : base(id)
    {
        Label = title;
    }

    /// <summary>
    /// Gets or sets the direction of the content of the subgraph, or <see langword="null"/> for the direction of the
    /// flowchart. As in Mermaid, it applies only when no edge links a node of the subgraph to a node outside of it.
    /// </summary>
    public DiagramDirection? Direction { get; set; }

    /// <summary>Gets the nodes of the subgraph. A node of several subgraphs belongs to the innermost one.</summary>
    public IList<FlowchartNode> Nodes { get; } = new List<FlowchartNode>();

    /// <summary>Gets the subgraphs nested in this one.</summary>
    public IList<FlowchartSubgraph> Subgraphs { get; } = new List<FlowchartSubgraph>();
}
