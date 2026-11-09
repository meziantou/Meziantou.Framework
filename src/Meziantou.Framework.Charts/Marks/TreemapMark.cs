namespace Meziantou.Framework.Charts;

/// <summary>Draws a tree as a treemap (<see cref="TreemapLayout"/>): each leaf is a rectangle whose area is proportional to its value, inside the rectangles of its ancestors.</summary>
/// <typeparam name="T">The type of the data of the nodes.</typeparam>
public sealed class TreemapMark<T> : HierarchyMark<T>
{
    /// <summary>Initializes a new instance of the <see cref="TreemapMark{T}"/> class.</summary>
    /// <param name="root">The root of the tree. Its values must be computed (<see cref="HierarchyNode{T}.Sum"/>).</param>
    public TreemapMark(HierarchyNode<T> root)
        : base(root)
    {
    }

    /// <summary>Gets or sets how the rectangle of a node is divided among its children. The default is <see cref="TreemapTiling.Squarified"/>.</summary>
    public TreemapTiling Tiling { get; set; }

    /// <summary>Gets or sets the space between siblings. The default is 1.</summary>
    public double PaddingInner { get; set; } = 1;

    /// <summary>Gets or sets the space between the edges of a group and its children. The default is 0.</summary>
    public double PaddingOuter { get; set; }

    /// <summary>Gets or sets the space between the top edge of a group and its children, where the name of the group is drawn when it fits. The default is 0.</summary>
    public double PaddingTop { get; set; }

    /// <summary>Gets or sets a value that indicates whether the value of a leaf is drawn under its label. The default is <see langword="true"/>.</summary>
    public bool ShowValues { get; set; } = true;

    private protected override void Render(RenderContext context, MarkData data, SceneGroup group, Dictionary<HierarchyNode<T>, int> indexes)
    {
        var frame = context.Frame;
        var layout = new TreemapLayout
        {
            Width = frame.Width,
            Height = frame.Height,
            Tiling = Tiling,
            PaddingInner = PaddingInner,
            PaddingOuter = PaddingOuter,
            PaddingTop = PaddingTop,
        };
        var font = context.LabelFont;
        var lineHeight = context.Measurer.Measure("Xg", font).Height;
        foreach (var rectangle in layout.Layout(Root))
        {
            if (rectangle.Node.Depth is 0 || rectangle.Width <= 0 || rectangle.Height <= 0)
                continue;

            var index = indexes[rectangle.Node];
            var color = GetFill(context, data, index);
            var x = frame.X + rectangle.X0;
            var y = frame.Y + rectangle.Y0;
            var shape = new SceneRectangle(x, y, rectangle.Width, rectangle.Height)
            {
                Fill = rectangle.Node.IsLeaf ? color : color.WithOpacity(color.Opacity * 0.25),
            };
            group.Add(Decorate(shape, data, index));

            var text = data.Text?[index];
            if (string.IsNullOrEmpty(text))
                continue;

            if (!rectangle.Node.IsLeaf)
            {
                // The name of a group goes in the space PaddingTop leaves above its children
                if (PaddingTop >= lineHeight)
                {
                    AddLabel(context, group, text, x + 3, y + ((PaddingTop - lineHeight) / 2), rectangle.Width - 6, context.Theme.Foreground, font);
                }

                continue;
            }

            if (rectangle.Height < lineHeight + 4)
                continue;

            var textColor = RenderContext.GetContrastingColor(color);
            if (AddLabel(context, group, text, x + 4, y + 3, rectangle.Width - 8, textColor, font) && ShowValues && rectangle.Height >= (2 * lineHeight) + 4)
            {
                AddLabel(context, group, NumberFormats.Default(rectangle.Node.Value), x + 4, y + 3 + lineHeight, rectangle.Width - 8, textColor.WithOpacity(0.75), font);
            }
        }
    }

    private static bool AddLabel(RenderContext context, SceneGroup group, string text, double x, double top, double maxWidth, SceneColor color, SceneFont font)
    {
        if (maxWidth <= 0)
            return false;

        var fitted = TextFitting.Truncate(context.Measurer, text, font, maxWidth);
        if (fitted is null)
            return false;

        var metrics = context.Measurer.Measure(fitted, font);
        group.Add(new SceneText(x, top + metrics.Ascent, fitted, font, color) { MeasuredWidth = metrics.Width });
        return true;
    }
}
