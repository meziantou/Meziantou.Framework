using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>The geometry of a node, centered on the origin.</summary>
internal sealed class ShapeDrawing
{
    private List<List<LayoutPoint>>? _flattened;

    public double Width { get; init; }

    public double Height { get; init; }

    /// <summary>Gets the outline: filled and stroked, and the edges stop on it.</summary>
    public required PathData Outline { get; init; }

    public bool FillOutline { get; init; } = true;

    public bool StrokeOutline { get; init; } = true;

    /// <summary>Gets a value that indicates whether the outline is filled with the color of the lines, as a start state.</summary>
    public bool FillWithLineColor { get; init; }

    /// <summary>Gets the shapes drawn behind the outline, filled and stroked as it.</summary>
    public List<PathData> Underlays { get; } = [];

    /// <summary>Gets the lines drawn over the outline.</summary>
    public List<PathData> Decorations { get; } = [];

    /// <summary>Gets the shapes drawn over the outline, filled with the color of its stroke.</summary>
    public List<PathData> FilledDecorations { get; } = [];

    public double LabelOffsetX { get; init; }

    public double LabelOffsetY { get; init; }

    public bool ShowLabel { get; init; } = true;

    public List<List<LayoutPoint>> Polygons => _flattened ??= Geometry.Flatten(Outline);

    /// <summary>Returns where an edge from the center of the shape toward a point leaves it.</summary>
    public LayoutPoint Intersect(LayoutPoint center, LayoutPoint toward)
    {
        // Far enough to be outside of the shape
        var dx = toward.X - center.X;
        var dy = toward.Y - center.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-9)
            return center;

        var reach = (Width + Height) * 2 / length;
        var outside = new LayoutPoint(dx * reach, dy * reach);
        var point = Geometry.Intersect(Polygons, new LayoutPoint(0, 0), outside);
        return new LayoutPoint(center.X + point.X, center.Y + point.Y);
    }
}
