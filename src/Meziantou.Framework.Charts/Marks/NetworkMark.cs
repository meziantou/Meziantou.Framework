namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a network graph (a node-link diagram) with a row per link: the nodes are placed by the force-directed layout of
/// d3-force, where the links are springs and the nodes repel each other, then the drawing is fitted to the plot area.
/// The layout is deterministic: it runs a fixed number of ticks from fixed starting positions.
/// </summary>
/// <typeparam name="T">The type of the rows: the links.</typeparam>
/// <remarks>
/// The nodes are the names that the links reference, in the order they appear. <see cref="NodeFill"/> colors them with
/// the color scale; <see cref="Mark{T}.Stroke"/> colors the links.
/// </remarks>
public sealed class NetworkMark<T> : Mark<T>
{
    private const string SourceColumn = "source";
    private const string TargetColumn = "target";
    private int _iterations = ForceLayout.DefaultTicks;

    /// <summary>Initializes a new instance of the <see cref="NetworkMark{T}"/> class.</summary>
    /// <param name="data">The rows: the links.</param>
    /// <param name="source">The channel of the name of a node of the link.</param>
    /// <param name="target">The channel of the name of the other node of the link.</param>
    public NetworkMark(IEnumerable<T> data, Func<T, object?> source, Func<T, object?> target)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Source = source;
        Target = target;
    }

    /// <summary>Gets the channel of the name of a node of the link.</summary>
    public Func<T, object?> Source { get; }

    /// <summary>Gets the channel of the name of the other node of the link.</summary>
    public Func<T, object?> Target { get; }

    /// <summary>Gets or sets the channel of the fill color of a node, from its name, mapped by the color scale, or <see langword="null"/> to use <see cref="Mark{T}.FillColor"/>.</summary>
    public Func<string, object?>? NodeFill { get; set; }

    /// <summary>Gets or sets the radius of the nodes. The default is 5.</summary>
    public double NodeRadius { get; set; } = 5;

    /// <summary>Gets or sets the length of the springs of the links, before the drawing is fitted to the plot area. The default is 30, as d3.</summary>
    public double LinkDistance { get; set; } = 30;

    /// <summary>Gets or sets the strength of the force between every pair of nodes: negative values repel, positive values attract. The default is -30, as d3.</summary>
    public double Charge { get; set; } = -30;

    /// <summary>Gets or sets the number of ticks of the simulation. The default is 300, as d3: the number of ticks until the simulation cools down.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int Iterations
    {
        get => _iterations;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _iterations = value;
        }
    }

    /// <summary>Gets or sets the opacity of the links, from 0 to 1. The default is 0.6.</summary>
    public double LinkOpacity { get; set; } = 0.6;

    /// <summary>Gets or sets a value that indicates whether the names of the nodes are drawn on their right. The default is <see langword="true"/>.</summary>
    public bool ShowLabels { get; set; } = true;

    internal override MarkTraits GetTraits(MarkData data) => MarkTraits.Frame | MarkTraits.HideXAxis | MarkTraits.HideYAxis;

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.Extra[SourceColumn] = Evaluate(rows, Source)!.ToCategorical();
        data.Extra[TargetColumn] = Evaluate(rows, Target)!.ToCategorical();
        if (NodeFill is not null)
        {
            // The colors of the nodes are part of the domain of the color scale
            data.ExtraColor.Add(GetNodeFills(GetNodes(data).Names));
        }
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        var (names, links, rows) = GetNodes(data);
        if (names.Count is 0)
            return;

        var layout = new ForceLayout(names.Count, links) { LinkDistance = LinkDistance, ManyBodyStrength = Charge };
        layout.Run(Iterations);

        // The drawing is scaled to fit the plot area, without changing the size of the nodes and of the labels
        var font = context.LabelFont;
        var labelWidths = names.Select(name => ShowLabels ? context.Measurer.Measure(name, font).Width + NodeRadius + 3 : 0).ToArray();
        var frame = context.Frame;
        var margin = NodeRadius + 2;
        var minX = layout.X.Min();
        var maxX = layout.X.Max();
        var minY = layout.Y.Min();
        var maxY = layout.Y.Max();
        var availableWidth = Math.Max(1, frame.Width - (2 * margin) - labelWidths.Max());
        var availableHeight = Math.Max(1, frame.Height - (2 * margin));
        var scale = Math.Min(maxX > minX ? availableWidth / (maxX - minX) : double.PositiveInfinity, maxY > minY ? availableHeight / (maxY - minY) : double.PositiveInfinity);
        if (!double.IsFinite(scale))
        {
            scale = 1;
        }

        var offsetX = frame.X + margin + ((availableWidth - ((maxX - minX) * scale)) / 2) - (minX * scale);
        var offsetY = frame.Y + margin + ((availableHeight - ((maxY - minY) * scale)) / 2) - (minY * scale);
        double PositionX(int node) => offsetX + (layout.X[node] * scale);
        double PositionY(int node) => offsetY + (layout.Y[node] * scale);

        for (var i = 0; i < links.Count; i++)
        {
            var (source, target) = links[i];
            var row = rows[i];
            var color = data.Stroke is null && StrokeColor is null ? context.Theme.MutedForeground : GetStroke(context, data, row);
            group.Add(Decorate(new SceneLine(PositionX(source), PositionY(source), PositionX(target), PositionY(target))
            {
                Stroke = color.WithOpacity(color.Opacity * LinkOpacity),
                StrokeWidth = StrokeWidth ?? 1.5,
            }, data, row));
        }

        var fills = NodeFill is null ? null : GetNodeFills(names);
        var metrics = context.Measurer.Measure("Xg", font);
        for (var i = 0; i < names.Count; i++)
        {
            var (x, y) = (PositionX(i), PositionY(i));
            group.Add(new SceneCircle(x, y, NodeRadius)
            {
                Fill = context.GetColor(fills, i, FillColor),
                Stroke = context.Theme.Background ?? ChartColor.White,
                StrokeWidth = 1.5,
                Tooltip = names[i],
            });

            if (ShowLabels)
            {
                group.Add(new SceneText(x + NodeRadius + 3, y + ((metrics.Ascent - metrics.Descent) / 2), names[i], font, context.Theme.Foreground)
                {
                    MeasuredWidth = labelWidths[i] - NodeRadius - 3,
                });
            }
        }
    }

    private ChannelValues GetNodeFills(List<string> names) => ChannelValues.FromObjects([.. names.Select(name => NodeFill!(name))]);

    /// <summary>The names of the nodes, in the order the links reference them, and the links between them.</summary>
    private static (List<string> Names, List<(int Source, int Target)> Links, List<int> Rows) GetNodes(MarkData data)
    {
        var sources = data.Extra[SourceColumn];
        var targets = data.Extra[TargetColumn];
        var names = new List<string>();
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var links = new List<(int, int)>();
        var rows = new List<int>();
        for (var i = 0; i < data.Count; i++)
        {
            if (sources.GetCategory(i) is { } source && targets.GetCategory(i) is { } target)
            {
                links.Add((GetIndex(source), GetIndex(target)));
                rows.Add(i);
            }
        }

        return (names, links, rows);

        int GetIndex(string name)
        {
            if (!indexes.TryGetValue(name, out var index))
            {
                index = names.Count;
                indexes.Add(name, index);
                names.Add(name);
            }

            return index;
        }
    }
}
