namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a waterfall chart: each bar goes from the running total before its row to the running total after it, in the
/// positive color of the theme for an increase and in its negative color for a decrease. A total row (<see cref="IsTotal"/>)
/// goes from zero to the running total, in the neutral color. Dashed lines join the bars.
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
public sealed class WaterfallMark<T> : Mark<T>
{
    private const string KindColumn = "kind";

    /// <summary>Initializes a new instance of the <see cref="WaterfallMark{T}"/> class.</summary>
    /// <param name="data">The rows, in order.</param>
    /// <param name="x">The channel of the category of the bar.</param>
    /// <param name="y">The channel of the change. It is ignored for the total rows.</param>
    public WaterfallMark(IEnumerable<T> data, Func<T, object?> x, Func<T, object?> y)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        X = x;
        Y = y;
    }

    /// <summary>Gets the channel of the category of the bar.</summary>
    public Func<T, object?> X { get; }

    /// <summary>Gets the channel of the change.</summary>
    public Func<T, object?> Y { get; }

    /// <summary>Gets or sets the function that tells whether a row is a total, or <see langword="null"/> for none.</summary>
    public Func<T, bool>? IsTotal { get; set; }

    internal override MarkTraits GetTraits(MarkData data) => MarkTraits.BandX | MarkTraits.ZeroY;

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.X = Evaluate(rows, X);
        var values = Evaluate(rows, Y)!;
        var isTotal = IsTotal is null ? null : rows.Select(IsTotal).ToArray();
        var numbers = values.IsContinuous ? values.Numbers : new double[rows.Count];
        var (y1, y2) = CumulativeTransform.Accumulate(numbers, MarkData.GroupBy(rows.Count, key: null), isTotal);
        data.Y1 = ChannelValues.FromNumbers(y1);
        data.Y2 = ChannelValues.FromNumbers(y2);

        // 1 for an increase, -1 for a decrease, 0 for a total
        var kinds = new double[rows.Count];
        for (var i = 0; i < kinds.Length; i++)
        {
            kinds[i] = isTotal?[i] is true ? 0 : y2[i] >= y1[i] ? 1 : -1;
        }

        data.Extra[KindColumn] = ChannelValues.FromNumbers(kinds);
        data.Titles ??= [.. Enumerable.Range(0, rows.Count).Select(i => data.X?.GetCategory(i) + ": " + NumberFormats.Default(isTotal?[i] is true ? y2[i] : y2[i] - y1[i]))];
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        if (context.X is null || context.Y is null || data.X is null)
            return;

        var kinds = data.Extra[KindColumn];
        var bandwidth = BarMark<T>.GetBandwidth(context.X, data);
        (double End, double Level)? previous = null;
        for (var i = 0; i < data.Count; i++)
        {
            var start = context.MapX(data.X, i);
            var y1 = context.MapY(data.Y1, i);
            var y2 = context.MapY(data.Y2, i);
            if (double.IsNaN(start) || double.IsNaN(y1) || double.IsNaN(y2))
                continue;

            if (previous is { } link)
            {
                var connector = context.Segment(link.End, link.Level, start, link.Level);
                connector.Stroke = context.Theme.AxisColor;
                connector.StrokeDashArray = [3, 3];
                group.Add(connector);
            }

            var shape = context.Coordinates.Rectangle(start, start + bandwidth, y1, y2);
            shape.Fill = data.Fill is not null || FillColor is not null
                ? GetFill(context, data, i)
                : kinds.Numbers[i] switch
                {
                    > 0 => context.Theme.PositiveColor,
                    < 0 => context.Theme.NegativeColor,
                    _ => context.Theme.NeutralColor,
                };
            group.Add(Decorate(shape, data, i));
            previous = (start + bandwidth, y2);
        }
    }
}
