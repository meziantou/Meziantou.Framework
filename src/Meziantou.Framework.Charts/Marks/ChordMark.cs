namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a chord diagram of the flows between nodes, with a row per flow: the nodes are arcs around a circle, as long as
/// the total of the flows that leave them, and each pair of nodes is joined by a ribbon whose ends are as wide as the
/// flows from each node to the other. The nodes take the colors of the palette of the theme, and a ribbon takes the color
/// of the node of its larger flow, unless the <see cref="Mark{T}.Fill"/> channel colors the ribbons.
/// </summary>
/// <typeparam name="T">The type of the rows: the flows.</typeparam>
/// <remarks>The rows that have the same source and target add up. Missing, infinite and negative values count as zero.</remarks>
public sealed class ChordMark<T> : Mark<T>
{
    private const string SourceColumn = "source";
    private const string TargetColumn = "target";
    private const string ValueColumn = "value";

    /// <summary>Initializes a new instance of the <see cref="ChordMark{T}"/> class.</summary>
    /// <param name="data">The rows: the flows.</param>
    /// <param name="source">The channel of the name of the node the flow leaves.</param>
    /// <param name="target">The channel of the name of the node the flow reaches.</param>
    /// <param name="value">The channel of the value of the flow.</param>
    public ChordMark(IEnumerable<T> data, Func<T, object?> source, Func<T, object?> target, Func<T, object?> value)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(value);
        Source = source;
        Target = target;
        Value = value;
    }

    /// <summary>Gets the channel of the name of the node the flow leaves.</summary>
    public Func<T, object?> Source { get; }

    /// <summary>Gets the channel of the name of the node the flow reaches.</summary>
    public Func<T, object?> Target { get; }

    /// <summary>Gets the channel of the value of the flow.</summary>
    public Func<T, object?> Value { get; }

    /// <summary>Gets or sets the thickness of the arcs of the nodes, in pixels. The default is 12.</summary>
    public double ArcWidth { get; set; } = 12;

    /// <summary>Gets or sets the space between the arcs of two nodes, in pixels along the inner circle. The default is 10.</summary>
    public double NodePadding { get; set; } = 10;

    /// <summary>Gets or sets the opacity of the ribbons, from 0 to 1. The default is 0.7.</summary>
    public double LinkOpacity { get; set; } = 0.7;

    /// <summary>Gets or sets a value that indicates whether the names of the nodes are drawn around the circle. The default is <see langword="true"/>.</summary>
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

        // The nodes, in the order they appear, and the matrix of the flows between them
        var names = new List<string>();
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var flows = new List<(int Source, int Target, double Value, int Row)>();
        for (var i = 0; i < data.Count; i++)
        {
            if (sources.GetCategory(i) is { } source && targets.GetCategory(i) is { } target)
            {
                var value = values.GetNumber(i);
                flows.Add((GetIndex(source), GetIndex(target), double.IsFinite(value) ? Math.Max(0, value) : 0, i));
            }
        }

        if (names.Count is 0)
            return;

        var matrix = new double[names.Count][];
        var rows = new int[names.Count][];
        for (var i = 0; i < names.Count; i++)
        {
            matrix[i] = new double[names.Count];
            rows[i] = new int[names.Count];
        }

        foreach (var (source, target, value, row) in flows)
        {
            matrix[source][target] += value;
            rows[source][target] = row;
        }

        // The circle leaves room for the labels around it, as the angular axis of polar coordinates does
        var font = context.LabelFont;
        var frame = context.Frame;
        var horizontalSpace = 4.0;
        var verticalSpace = 4.0;
        if (ShowLabels)
        {
            horizontalSpace = names.Max(name => context.Measurer.Measure(name, font).Width) + 10;
            verticalSpace = context.Measurer.Measure("Xg", font).Height + 10;
        }

        var outerRadius = Math.Max(1, Math.Min((frame.Width / 2) - horizontalSpace, (frame.Height / 2) - verticalSpace));
        var innerRadius = Math.Max(0.5, outerRadius - Math.Min(ArcWidth, outerRadius / 4));
        var centerX = frame.CenterX;
        var centerY = frame.CenterY;
        var (groups, chords) = ChordLayout.Compute(matrix, Math.Max(0, NodePadding) / innerRadius);

        foreach (var chord in chords)
        {
            var source = chord.Source!;
            var target = chord.Target!;
            var row = rows[source.Index][target.Index];
            var color = data.Fill is null && FillColor is null ? context.Theme.GetPaletteColor(source.Index) : GetFill(context, data, row);
            var tooltip = names[source.Index] + " → " + names[target.Index] + ": " + NumberFormats.Default(source.Value);
            if (source.Index != target.Index && target.Value > 0)
            {
                tooltip += "\n" + names[target.Index] + " → " + names[source.Index] + ": " + NumberFormats.Default(target.Value);
            }

            var shape = group.Add(new ScenePath(ChordLayout.Ribbon(centerX, centerY, innerRadius - 1, source, target))
            {
                Fill = color.WithOpacity(color.Opacity * LinkOpacity),
                Tooltip = data.Titles?[row] ?? tooltip,
            });
            DataAttributeValues.Apply(shape, data.DataAttributes, row);
        }

        foreach (var node in groups)
        {
            group.Add(new ScenePath(PolarGeometry.Sector(centerX, centerY, innerRadius, outerRadius, node.StartAngle, node.EndAngle))
            {
                Fill = context.Theme.GetPaletteColor(node.Index),
                Tooltip = names[node.Index] + ": " + NumberFormats.Default(node.Value),
            });

            if (!ShowLabels)
                continue;

            // The label is outside the middle of the arc, anchored on the side of the circle it is on
            var angle = (node.StartAngle + node.EndAngle) / 2;
            var name = names[node.Index];
            var metrics = context.Measurer.Measure(name, font);
            var (x, y) = PolarGeometry.Point(centerX, centerY, outerRadius + 6, angle);
            var sin = Math.Sin(angle);
            var cos = Math.Cos(angle);
            var anchor = sin > 0.1 ? SceneTextAnchor.Start : sin < -0.1 ? SceneTextAnchor.End : SceneTextAnchor.Middle;
            var baseline = y + (((metrics.Ascent * (1 - cos)) - (metrics.Descent * (1 + cos))) / 2);
            group.Add(new SceneText(x, baseline, name, font, context.Theme.Foreground) { Anchor = anchor, MeasuredWidth = metrics.Width });
        }

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
