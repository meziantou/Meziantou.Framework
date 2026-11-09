namespace Meziantou.Framework.Diagrams;

/// <summary>An edge of a flowchart, from a node or a subgraph to another.</summary>
public sealed class FlowchartEdge
{
    private int _length = 1;

    /// <summary>Initializes a new instance of the <see cref="FlowchartEdge"/> class.</summary>
    /// <param name="source">The element the edge starts from.</param>
    /// <param name="target">The element the edge goes to.</param>
    /// <param name="label">The text of the edge, or <see langword="null"/> for none.</param>
    public FlowchartEdge(FlowchartElement source, FlowchartElement target, DiagramText? label = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Source = source;
        Target = target;
        Label = label;
    }

    /// <summary>Gets the element the edge starts from.</summary>
    public FlowchartElement Source { get; }

    /// <summary>Gets the element the edge goes to.</summary>
    public FlowchartElement Target { get; }

    /// <summary>Gets or sets the identifier of the edge, or <see langword="null"/> (Mermaid <c>e1@--&gt;</c>).</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the text of the edge, or <see langword="null"/> for none.</summary>
    public DiagramText? Label { get; set; }

    /// <summary>Gets or sets the line of the edge.</summary>
    public FlowchartLineStyle LineStyle { get; set; }

    /// <summary>Gets or sets the decoration of the start of the edge.</summary>
    public FlowchartArrowHead StartArrowHead { get; set; }

    /// <summary>Gets or sets the decoration of the end of the edge. The default is <see cref="FlowchartArrowHead.Arrow"/>.</summary>
    public FlowchartArrowHead EndArrowHead { get; set; } = FlowchartArrowHead.Arrow;

    /// <summary>Gets or sets the minimum number of ranks between the source and the target: an edge of length 2 is twice as long. The default is 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1 or greater than 100.</exception>
    public int Length
    {
        get => _length;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
            _length = value;
        }
    }

    /// <summary>Gets or sets the style of the edge (Mermaid <c>linkStyle</c>): <see cref="DiagramStyle.Stroke"/>, <see cref="DiagramStyle.StrokeWidth"/>, <see cref="DiagramStyle.StrokeDashArray"/>, and <see cref="DiagramStyle.TextColor"/> for the label.</summary>
    public DiagramStyle? Style { get; set; }

    /// <summary>Gets or sets the curve of the edge, or <see langword="null"/> for <see cref="FlowchartDiagram.Curve"/>.</summary>
    public EdgeCurve? Curve { get; set; }

    /// <summary>Gets the classes of the edge: the names of styles of <see cref="FlowchartDiagram.ClassDefinitions"/>, also written as CSS classes in the SVG.</summary>
    public IList<string> CssClasses { get; } = new List<string>();
}
