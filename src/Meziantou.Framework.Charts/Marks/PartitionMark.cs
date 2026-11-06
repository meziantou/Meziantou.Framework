namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a tree as adjacent layers, one per depth (<see cref="PartitionLayout"/>): an icicle chart in Cartesian
/// coordinates (horizontal layers from the top down, or vertical layers from the left when transposed), and a sunburst
/// in polar coordinates (rings from the center out). The size of a node along its layer is proportional to its value.
/// </summary>
/// <typeparam name="T">The type of the data of the nodes.</typeparam>
public sealed class PartitionMark<T> : HierarchyMark<T>
{
    /// <summary>Initializes a new instance of the <see cref="PartitionMark{T}"/> class.</summary>
    /// <param name="root">The root of the tree. Its values must be computed (<see cref="HierarchyNode{T}.Sum"/>).</param>
    public PartitionMark(HierarchyNode<T> root)
        : base(root)
    {
    }

    /// <summary>Gets or sets a value that indicates whether the root is drawn, as the first layer. The default is <see langword="false"/>.</summary>
    public bool ShowRoot { get; set; }

    private protected override void Render(RenderContext context, MarkData data, SceneGroup group, Dictionary<HierarchyNode<T>, int> indexes)
    {
        var rectangles = new PartitionLayout().Layout(Root);
        var layers = Root.Height + 1;
        var skip = ShowRoot ? 0.0 : 1.0 / layers;
        if (skip >= 1)
        {
            skip = 0;
        }

        foreach (var rectangle in rectangles)
        {
            if ((!ShowRoot && rectangle.Node.Depth is 0 && layers > 1) || rectangle.Width <= 0)
                continue;

            var index = indexes[rectangle.Node];
            var color = GetFill(context, data, index);
            var depthFade = rectangle.Node.Depth <= 1 ? 1 : Math.Max(0.45, 1 - (0.2 * (rectangle.Node.Depth - 1)));
            var fill = color.WithOpacity(color.Opacity * depthFade);
            var y0 = (rectangle.Y0 - skip) / (1 - skip);
            var y1 = (rectangle.Y1 - skip) / (1 - skip);
            SceneShape shape;
            if (context.Coordinates is PolarSystem polar)
            {
                var a0 = polar.StartAngle + (rectangle.X0 * (polar.EndAngle - polar.StartAngle));
                var a1 = polar.StartAngle + (rectangle.X1 * (polar.EndAngle - polar.StartAngle));
                var r0 = polar.InnerRadius + (y0 * (polar.OuterRadius - polar.InnerRadius));
                var r1 = polar.InnerRadius + (y1 * (polar.OuterRadius - polar.InnerRadius));
                shape = new ScenePath(PolarGeometry.Sector(polar.CenterX, polar.CenterY, r0, r1, a0, a1));
                AddShape(context, data, group, index, shape, fill);
                var (labelX, labelY) = PolarGeometry.Point(polar.CenterX, polar.CenterY, r0 < 1 ? 0 : (r0 + r1) / 2, (a0 + a1) / 2);
                var arcLength = r0 < 1 ? 2 * r1 : Math.Min((a1 - a0) * (r0 + r1) / 2, 2 * r1);
                context.AddSectorLabel(group, data.Text?[index], labelX, labelY, fill.WithOpacity(1), r0 < 1 ? 0 : (a0 + a1) / 2, r1 - r0, arcLength);
            }
            else
            {
                var frame = context.Frame;
                var (x0, x1, top, bottom) = context.Coordinates.Transposed
                    ? (frame.X + (y0 * frame.Width), frame.X + (y1 * frame.Width), frame.Y + (rectangle.X0 * frame.Height), frame.Y + (rectangle.X1 * frame.Height))
                    : (frame.X + (rectangle.X0 * frame.Width), frame.X + (rectangle.X1 * frame.Width), frame.Y + (y0 * frame.Height), frame.Y + (y1 * frame.Height));
                shape = new SceneRectangle(x0, top, x1 - x0, bottom - top);
                AddShape(context, data, group, index, shape, fill);
                context.AddCenteredLabel(group, data.Text?[index], (x0 + x1) / 2, (top + bottom) / 2, fill.WithOpacity(1), x1 - x0 - 4, bottom - top);
            }
        }
    }

    private static void AddShape(RenderContext context, MarkData data, SceneGroup group, int index, SceneShape shape, ChartColor fill)
    {
        shape.Fill = fill;
        shape.Stroke = context.Theme.MarkStrokeColor;
        group.Add(Decorate(shape, data, index));
    }
}
