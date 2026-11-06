namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a Sankey diagram of the flows between nodes, with a row per link: the nodes are bars in columns whose heights
/// are their values, and the links are bands whose widths are their values. The nodes take the colors of the palette of
/// the theme, and a link takes the color of its source, unless the <see cref="Mark{T}.Fill"/> channel colors the links.
/// </summary>
/// <typeparam name="T">The type of the rows: the links.</typeparam>
/// <remarks>The links must not form a cycle: drawing the plot throws an <see cref="InvalidOperationException"/>.</remarks>
public sealed class SankeyMark<T> : Mark<T>
{
    private const string SourceColumn = "source";
    private const string TargetColumn = "target";
    private const string ValueColumn = "value";

    /// <summary>Initializes a new instance of the <see cref="SankeyMark{T}"/> class.</summary>
    /// <param name="data">The rows: the links.</param>
    /// <param name="source">The channel of the name of the node the link leaves.</param>
    /// <param name="target">The channel of the name of the node the link reaches.</param>
    /// <param name="value">The channel of the value of the link.</param>
    public SankeyMark(IEnumerable<T> data, Func<T, object?> source, Func<T, object?> target, Func<T, object?> value)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(value);
        Source = source;
        Target = target;
        Value = value;
    }

    /// <summary>Gets the channel of the name of the node the link leaves.</summary>
    public Func<T, object?> Source { get; }

    /// <summary>Gets the channel of the name of the node the link reaches.</summary>
    public Func<T, object?> Target { get; }

    /// <summary>Gets the channel of the value of the link.</summary>
    public Func<T, object?> Value { get; }

    /// <summary>Gets or sets the width of the nodes. The default is 15.</summary>
    public double NodeWidth { get; set; } = 15;

    /// <summary>Gets or sets the space between the nodes of a column. The default is 10.</summary>
    public double NodePadding { get; set; } = 10;

    /// <summary>Gets or sets how the nodes are placed in columns. The default is <see cref="SankeyAlignment.Justify"/>.</summary>
    public SankeyAlignment Alignment { get; set; }

    /// <summary>Gets or sets the opacity of the links, from 0 to 1. The default is 0.4.</summary>
    public double LinkOpacity { get; set; } = 0.4;

    /// <summary>Gets or sets a value that indicates whether the names of the nodes are drawn. The default is <see langword="true"/>.</summary>
    public bool ShowLabels { get; set; } = true;

    internal override MarkTraits GetTraits(MarkData data) => MarkTraits.Frame | MarkTraits.HideXAxis | MarkTraits.HideYAxis;

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.Extra[SourceColumn] = Evaluate(rows, Source)!.ToCategorical();
        data.Extra[TargetColumn] = Evaluate(rows, Target)!.ToCategorical();
        data.Extra[ValueColumn] = Evaluate(rows, Value)!;
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        var sources = data.Extra[SourceColumn];
        var targets = data.Extra[TargetColumn];
        var values = data.Extra[ValueColumn];
        var links = new List<(string Source, string Target, double Value)>(data.Count);
        var rows = new List<int>(data.Count);
        for (var i = 0; i < data.Count; i++)
        {
            if (sources.GetCategory(i) is { } source && targets.GetCategory(i) is { } target)
            {
                links.Add((source, target, values.GetNumber(i)));
                rows.Add(i);
            }
        }

        var layout = SankeyLayout.Compute(links, context.Frame, NodeWidth, NodePadding, iterations: 6, Alignment);
        foreach (var link in layout.Links)
        {
            var row = rows[link.Index];
            var middle = (link.Source.X1 + link.Target.X0) / 2;
            var path = new PathBuilder()
                .MoveTo(link.Source.X1, link.Y0)
                .CubicTo(middle, link.Y0, middle, link.Y1, link.Target.X0, link.Y1)
                .Build();
            var color = data.Fill is null && FillColor is null ? context.Theme.GetPaletteColor(link.Source.Index) : GetFill(context, data, row);
            group.Add(new ScenePath(path)
            {
                Stroke = color.WithOpacity(color.Opacity * LinkOpacity),
                StrokeWidth = Math.Max(1, link.Width),
                Tooltip = data.Titles?[row] ?? link.Source.Name + " → " + link.Target.Name + ": " + NumberFormats.Default(link.Value),
            });
        }

        var font = context.LabelFont;
        foreach (var node in layout.Nodes)
        {
            group.Add(new SceneRectangle(node.X0, node.Y0, node.X1 - node.X0, Math.Max(1, node.Y1 - node.Y0))
            {
                Fill = context.Theme.GetPaletteColor(node.Index),
                Tooltip = node.Name + ": " + NumberFormats.Default(node.Value),
            });

            if (!ShowLabels)
                continue;

            // The labels of the left half are on the right of their nodes, the others on the left
            var metrics = context.Measurer.Measure(node.Name, font);
            var left = node.X0 < context.Frame.CenterX;
            group.Add(new SceneText(left ? node.X1 + 6 : node.X0 - 6, ((node.Y0 + node.Y1) / 2) + ((metrics.Ascent - metrics.Descent) / 2), node.Name, font, context.Theme.Foreground)
            {
                Anchor = left ? SceneTextAnchor.Start : SceneTextAnchor.End,
                MeasuredWidth = metrics.Width,
            });
        }
    }
}
