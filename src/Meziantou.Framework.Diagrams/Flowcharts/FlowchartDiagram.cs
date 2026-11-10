using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A flowchart: nodes of various shapes linked by edges, grouped in subgraphs, laid out in ranks in the direction of the
/// flow (Mermaid <c>flowchart</c>).
/// </summary>
public sealed class FlowchartDiagram : Diagram
{
    private double _nodeSpacing = 50;
    private double _rankSpacing = 50;
    private double? _wrappingWidth = 200;

    /// <summary>Gets or sets the direction of the flow. The default is <see cref="DiagramDirection.TopToBottom"/>.</summary>
    public DiagramDirection Direction { get; set; }

    /// <summary>Gets the nodes. The nodes of the subgraphs that are not in this list are drawn too.</summary>
    public IList<FlowchartNode> Nodes { get; } = new List<FlowchartNode>();

    /// <summary>Gets the edges, drawn in order.</summary>
    public IList<FlowchartEdge> Edges { get; } = new List<FlowchartEdge>();

    /// <summary>Gets the top-level subgraphs.</summary>
    public IList<FlowchartSubgraph> Subgraphs { get; } = new List<FlowchartSubgraph>();

    /// <summary>
    /// Gets the styles of the classes of the nodes, subgraphs and edges, by name (Mermaid <c>classDef</c>). The class
    /// <c>default</c> applies to every node.
    /// </summary>
    public IDictionary<string, DiagramStyle> ClassDefinitions { get; } = new Dictionary<string, DiagramStyle>(StringComparer.Ordinal);

    /// <summary>Gets or sets the style of every edge, which the style of an edge takes precedence over (Mermaid <c>linkStyle default</c>).</summary>
    public DiagramStyle? DefaultEdgeStyle { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the top-level subgraphs are drawn as swimlanes: bands across the
    /// diagram, in their order, that hold their nodes (Mermaid <c>swimlane-beta</c>). The nested subgraphs are boxes in their lane.
    /// </summary>
    public bool Swimlanes { get; set; }

    /// <summary>Gets or sets the curve of the edges. The default is <see cref="EdgeCurve.Basis"/>.</summary>
    public EdgeCurve Curve { get; set; }

    /// <summary>Gets or sets the space between two nodes of a rank, in pixels. The default is 50.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double NodeSpacing
    {
        get => _nodeSpacing;
        set => _nodeSpacing = ValidateSpacing(value);
    }

    /// <summary>Gets or sets the space between two ranks, in pixels. The default is 50.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double RankSpacing
    {
        get => _rankSpacing;
        set => _rankSpacing = ValidateSpacing(value);
    }

    /// <summary>Gets or sets the width at which the labels wrap, in pixels, or <see langword="null"/> to wrap them only at their line feeds. The default is 200.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    public double? WrappingWidth
    {
        get => _wrappingWidth;
        set
        {
            if (value is { } width && (!(width > 0) || !double.IsFinite(width)))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The wrapping width must be a positive finite number.");

            _wrappingWidth = value;
        }
    }

    /// <summary>Adds a node.</summary>
    /// <param name="id">The identifier of the node, unique in the flowchart.</param>
    /// <param name="label">The text of the node, or <see langword="null"/> to show its identifier.</param>
    /// <param name="shape">The shape of the node.</param>
    /// <returns>The node.</returns>
    public FlowchartNode AddNode(string id, DiagramText? label = null, FlowchartNodeShape shape = FlowchartNodeShape.Rectangle)
    {
        var node = new FlowchartNode(id, label, shape);
        Nodes.Add(node);
        return node;
    }

    /// <summary>Adds an edge.</summary>
    /// <param name="source">The element the edge starts from.</param>
    /// <param name="target">The element the edge goes to.</param>
    /// <param name="label">The text of the edge, or <see langword="null"/> for none.</param>
    /// <returns>The edge.</returns>
    public FlowchartEdge AddEdge(FlowchartElement source, FlowchartElement target, DiagramText? label = null)
    {
        var edge = new FlowchartEdge(source, target, label);
        Edges.Add(edge);
        return edge;
    }

    /// <summary>Adds an edge between two elements found by identifier; a missing node is added.</summary>
    /// <param name="sourceId">The identifier of the element the edge starts from.</param>
    /// <param name="targetId">The identifier of the element the edge goes to.</param>
    /// <param name="label">The text of the edge, or <see langword="null"/> for none.</param>
    /// <returns>The edge.</returns>
    public FlowchartEdge AddEdge(string sourceId, string targetId, DiagramText? label = null) => AddEdge(Find(sourceId) ?? AddNode(sourceId), Find(targetId) ?? AddNode(targetId), label);

    /// <summary>Adds a top-level subgraph.</summary>
    /// <param name="id">The identifier of the subgraph, unique in the flowchart.</param>
    /// <param name="title">The title, or <see langword="null"/> to show the identifier.</param>
    /// <returns>The subgraph.</returns>
    public FlowchartSubgraph AddSubgraph(string id, DiagramText? title = null)
    {
        var subgraph = new FlowchartSubgraph(id, title);
        Subgraphs.Add(subgraph);
        return subgraph;
    }

    /// <summary>Finds a node or a subgraph by identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The element, or <see langword="null"/> when there is none.</returns>
    public FlowchartElement? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        foreach (var node in Nodes)
        {
            if (string.Equals(node.Id, id, StringComparison.Ordinal))
                return node;
        }

        foreach (var subgraph in EnumerateSubgraphs())
        {
            if (string.Equals(subgraph.Id, id, StringComparison.Ordinal))
                return subgraph;

            foreach (var node in subgraph.Nodes)
            {
                if (string.Equals(node.Id, id, StringComparison.Ordinal))
                    return node;
            }
        }

        return null;
    }

    /// <summary>Enumerates the subgraphs and their nested subgraphs, depth first.</summary>
    /// <returns>The subgraphs.</returns>
    public IEnumerable<FlowchartSubgraph> EnumerateSubgraphs()
    {
        var visited = new HashSet<FlowchartSubgraph>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<FlowchartSubgraph>(Subgraphs.Reverse());
        while (stack.Count > 0)
        {
            var subgraph = stack.Pop();
            if (!visited.Add(subgraph))
                continue;

            yield return subgraph;
            for (var i = subgraph.Subgraphs.Count - 1; i >= 0; i--)
            {
                stack.Push(subgraph.Subgraphs[i]);
            }
        }
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => FlowchartRenderer.Render(this, context, group);

    private static double ValidateSpacing(double value)
    {
        if (!(value >= 0) || !double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "The spacing must be a positive finite number.");

        return value;
    }
}
