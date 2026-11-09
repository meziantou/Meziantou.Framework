using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

internal sealed class GraphEdge
{
    public GraphNode? Source { get; set; }

    public GraphNode? Target { get; set; }

    /// <summary>Gets or sets the cluster the edge starts from, when it is attached to a cluster instead of a node.</summary>
    public GraphCluster? SourceCluster { get; set; }

    public GraphCluster? TargetCluster { get; set; }

    public TextBlock? Label { get; set; }

    public int MinLength { get; set; } = 1;

    public double Weight { get; set; } = 1;

    public EdgeCurve Curve { get; set; }

    public SceneColor Color { get; set; }

    public double StrokeWidth { get; set; } = 1;

    public IReadOnlyList<double>? StrokeDashArray { get; set; }

    public EdgeMarker StartMarker { get; set; }

    public EdgeMarker EndMarker { get; set; }

    public SceneColor? MarkerColor { get; set; }

    public SceneColor LabelColor { get; set; }

    public SceneColor? LabelBackground { get; set; }

    /// <summary>Gets or sets a text drawn next to the start of the edge, such as a multiplicity.</summary>
    public TextBlock? StartLabel { get; set; }

    public TextBlock? EndLabel { get; set; }

    public bool Invisible { get; set; }

    public string? CssClass { get; set; }

    public double Opacity { get; set; } = 1;

    public List<LayoutPoint> Points { get; set; } = [];

    public LayoutPoint LabelPosition { get; set; }
}
