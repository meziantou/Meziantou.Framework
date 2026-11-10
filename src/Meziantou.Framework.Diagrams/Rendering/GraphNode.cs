using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

internal sealed class GraphNode
{
    public double Width { get; set; }

    public double Height { get; set; }

    public GraphCluster? Cluster { get; set; }

    /// <summary>Gets or sets the drawing of the node, centered on a point.</summary>
    public Action<SceneGroup, double, double>? Draw { get; set; }

    /// <summary>Gets or sets where an edge from the center toward a point leaves the node, or <see langword="null"/> for its bounding rectangle.</summary>
    public Func<LayoutPoint, LayoutPoint, LayoutPoint>? Intersect { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public SceneBounds Bounds => new(X - (Width / 2), Y - (Height / 2), Width, Height);

    public LayoutPoint Center => new(X, Y);

    public LayoutPoint IntersectToward(LayoutPoint toward)
    {
        if (Intersect is not null)
            return Intersect(Center, toward);

        return Geometry.IntersectRectangle(Bounds, toward);
    }
}
