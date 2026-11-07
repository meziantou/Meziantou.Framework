namespace Meziantou.Framework.Charts;

/// <summary>Draws a tree as packed circles (<see cref="PackLayout"/>): each leaf is a circle whose area is proportional to its value, inside the circles of its ancestors.</summary>
/// <typeparam name="T">The type of the data of the nodes.</typeparam>
public sealed class PackMark<T> : HierarchyMark<T>
{
    /// <summary>Initializes a new instance of the <see cref="PackMark{T}"/> class.</summary>
    /// <param name="root">The root of the tree. Its values must be computed (<see cref="HierarchyNode{T}.Sum"/>).</param>
    public PackMark(HierarchyNode<T> root)
        : base(root)
    {
    }

    /// <summary>Gets or sets the space between sibling circles, and between a parent and its children. The default is 3.</summary>
    public double Padding { get; set; } = 3;

    private protected override void Render(RenderContext context, MarkData data, SceneGroup group, Dictionary<HierarchyNode<T>, int> indexes)
    {
        var frame = context.Frame;
        var size = Math.Min(frame.Width, frame.Height);
        var layout = new PackLayout { Width = size, Height = size, Padding = Padding };
        var offsetX = frame.X + ((frame.Width - size) / 2);
        var offsetY = frame.Y + ((frame.Height - size) / 2);
        var circles = layout.Layout(Root);
        var labels = new List<(HierarchyCircle<T> Circle, ChartColor Color)>();
        foreach (var circle in circles)
        {
            if (circle.Radius <= 0)
                continue;

            var index = indexes[circle.Node];
            var shape = new SceneCircle(offsetX + circle.X, offsetY + circle.Y, circle.Radius);
            var color = GetFill(context, data, index);
            if (circle.Node.Depth is 0)
            {
                shape.Fill = context.Theme.GridColor.WithOpacity(0.4);
            }
            else if (!circle.Node.IsLeaf)
            {
                shape.Fill = color.WithOpacity(color.Opacity * 0.2);
                shape.Stroke = color.WithOpacity(color.Opacity * 0.6);
            }
            else
            {
                shape.Fill = color;
                labels.Add((circle, color));
            }

            group.Add(Decorate(shape, data, index));
        }

        foreach (var (circle, color) in labels)
        {
            var text = data.Text?[indexes[circle.Node]];
            if (string.IsNullOrEmpty(text))
                continue;

            var metrics = context.Measurer.Measure(text, context.LabelFont);
            if (TextFitting.FitsInCircle(metrics, circle.Radius - 2))
            {
                context.AddCenteredLabel(group, text, offsetX + circle.X, offsetY + circle.Y, color, double.PositiveInfinity, double.PositiveInfinity);
            }
        }
    }
}
