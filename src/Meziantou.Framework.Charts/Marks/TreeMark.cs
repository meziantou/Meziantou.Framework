namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a tree as a node-link diagram: a dot per node and a curve from each node to its children. In Cartesian
/// coordinates the root is at the top; transposed, it is on the left; in polar coordinates, it is at the center (a radial
/// tree). The labels of the leaves are drawn after them, and the labels of the other nodes next to them, except in polar coordinates.
/// </summary>
/// <typeparam name="T">The type of the data of the nodes.</typeparam>
public sealed class TreeMark<T> : HierarchyMark<T>
{
    /// <summary>Initializes a new instance of the <see cref="TreeMark{T}"/> class.</summary>
    /// <param name="root">The root of the tree.</param>
    public TreeMark(HierarchyNode<T> root)
        : base(root)
    {
    }

    /// <summary>Gets or sets the layout: a tidy tree or a dendrogram. The default is <see cref="TreeLayoutKind.Tidy"/>.</summary>
    public TreeLayoutKind Layout { get; set; }

    /// <summary>Gets or sets the radius of the dots. The default is 3.5.</summary>
    public double NodeRadius { get; set; } = 3.5;

    private protected override void Render(RenderContext context, MarkData data, SceneGroup group, Dictionary<HierarchyNode<T>, int> indexes)
    {
        var points = Layout is TreeLayoutKind.Cluster ? new ClusterLayout().Layout(Root) : new TreeLayout().Layout(Root);
        var font = context.LabelFont;
        var lineHeight = context.Measurer.Measure("Xg", font).Height;
        var leafLabelWidth = points.Where(point => point.Node.IsLeaf).Select(point => MeasureLabel(context, data, indexes, point)).DefaultIfEmpty(0).Max();
        var rootLabelWidth = MeasureLabel(context, data, indexes, points[0]);
        var frame = context.Frame;
        var gap = NodeRadius + 4;
        Func<double, double, (double X, double Y)> map;
        if (context.Coordinates is PolarSystem polar)
        {
            var radius = Math.Max(10, polar.OuterRadius - (leafLabelWidth > 0 ? leafLabelWidth + gap : NodeRadius));
            map = (x, y) => PolarGeometry.Point(polar.CenterX, polar.CenterY, y * radius, polar.StartAngle + (x * (polar.EndAngle - polar.StartAngle)));
        }
        else if (context.Coordinates.Transposed)
        {
            var left = frame.X + (rootLabelWidth > 0 ? rootLabelWidth + gap : NodeRadius);
            var right = frame.Right - (leafLabelWidth > 0 ? leafLabelWidth + gap : NodeRadius);
            var top = frame.Y + Math.Max(NodeRadius, lineHeight / 2);
            var bottom = frame.Bottom - Math.Max(NodeRadius, lineHeight / 2);
            map = (x, y) => (left + (y * (right - left)), top + (x * (bottom - top)));
        }
        else
        {
            var top = frame.Y + (rootLabelWidth > 0 ? lineHeight + gap : NodeRadius);
            var bottom = frame.Bottom - (leafLabelWidth > 0 ? leafLabelWidth + gap : NodeRadius);
            var left = frame.X + Math.Max(NodeRadius, lineHeight / 2);
            var right = frame.Right - Math.Max(NodeRadius, lineHeight / 2);
            map = (x, y) => (left + (x * (right - left)), top + (y * (bottom - top)));
        }

        var byNode = points.ToDictionary(point => point.Node);
        foreach (var point in points)
        {
            if (point.Node.Parent is not { } parent)
                continue;

            // A cubic curve whose control points are halfway along the depth, as d3's linkVertical and linkRadial
            var start = byNode[parent];
            var middle = (start.Y + point.Y) / 2;
            var (x0, y0) = map(start.X, start.Y);
            var (c1x, c1y) = map(start.X, middle);
            var (c2x, c2y) = map(point.X, middle);
            var (x1, y1) = map(point.X, point.Y);
            group.Add(new ScenePath(new PathBuilder().MoveTo(x0, y0).CubicTo(c1x, c1y, c2x, c2y, x1, y1).Build()) { Stroke = context.Theme.AxisColor, StrokeWidth = 1.2 });
        }

        foreach (var point in points)
        {
            var index = indexes[point.Node];
            var (x, y) = map(point.X, point.Y);
            var fill = point.Node.Depth is 0 ? context.Theme.Foreground : GetFill(context, data, index);
            group.Add(Decorate(new SceneCircle(x, y, NodeRadius) { Fill = fill, Stroke = context.Theme.Background ?? ChartColor.White }, data, index));
            if (data.Text?[index] is { Length: > 0 } text)
            {
                AddLabel(context, group, point, text, x, y, gap);
            }
        }
    }

    private static double MeasureLabel(RenderContext context, MarkData data, Dictionary<HierarchyNode<T>, int> indexes, HierarchyPoint<T> point)
        => data.Text?[indexes[point.Node]] is { Length: > 0 } text ? context.Measurer.Measure(text, context.LabelFont).Width : 0;

    private static void AddLabel(RenderContext context, SceneGroup group, HierarchyPoint<T> point, string text, double x, double y, double gap)
    {
        var font = context.LabelFont;
        var metrics = context.Measurer.Measure(text, font);
        var middle = (metrics.Ascent - metrics.Descent) / 2;
        SceneText label;
        if (context.Coordinates is PolarSystem polar)
        {
            if (!point.Node.IsLeaf)
                return;

            // Along the radius, outward; on the left half, turned over so it reads from left to right
            var angle = polar.StartAngle + (point.X * (polar.EndAngle - polar.StartAngle));
            var degrees = (angle * 180 / Math.PI) - 90;
            var flipped = Math.Cos(angle - (Math.PI / 2)) < 0;
            var (lx, ly) = (x + (gap * Math.Sin(angle)), y - (gap * Math.Cos(angle)));
            label = new SceneText(lx, ly + middle, text, font, context.Theme.Foreground)
            {
                Anchor = flipped ? SceneTextAnchor.End : SceneTextAnchor.Start,
                Rotation = flipped ? degrees - 180 : degrees,
            };
        }
        else if (context.Coordinates.Transposed)
        {
            label = point.Node.IsLeaf
                ? new SceneText(x + gap, y + middle, text, font, context.Theme.Foreground)
                : new SceneText(x - gap, y + middle, text, font, context.Theme.Foreground) { Anchor = SceneTextAnchor.End };
        }
        else
        {
            label = point.Node.IsLeaf
                ? new SceneText(x - middle, y + gap, text, font, context.Theme.Foreground) { Rotation = 90 }
                : new SceneText(x, y - gap, text, font, context.Theme.Foreground) { Anchor = SceneTextAnchor.Middle };
        }

        label.MeasuredWidth = metrics.Width;
        group.Add(label);
    }
}
