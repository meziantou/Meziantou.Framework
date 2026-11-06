namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a line at the y value of each row, across the plot area or from <see cref="X1"/> to <see cref="X2"/>:
/// horizontal reference lines and thresholds, the bars of dumbbells. In polar coordinates, the lines are circles.
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
public class RuleYMark<T> : Mark<T>
{
    /// <summary>Initializes a new instance of the <see cref="RuleYMark{T}"/> class.</summary>
    /// <param name="data">The rows.</param>
    /// <param name="y">The channel of the y position.</param>
    public RuleYMark(IEnumerable<T> data, Func<T, object?> y)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(y);
        Y = y;
    }

    /// <summary>Gets the channel of the y position.</summary>
    public Func<T, object?> Y { get; }

    /// <summary>Gets or sets the channel of the start of the line, or <see langword="null"/> for the start of the x scale.</summary>
    public Func<T, object?>? X1 { get; set; }

    /// <summary>Gets or sets the channel of the end of the line, or <see langword="null"/> for the end of the x scale.</summary>
    public Func<T, object?>? X2 { get; set; }

    /// <summary>Gets or sets the lengths of the dashes and gaps, or <see langword="null"/> for a solid line.</summary>
    public IReadOnlyList<double>? StrokeDashArray { get; set; }

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.Y = Evaluate(rows, Y);
        data.X1 = Evaluate(rows, X1);
        data.X2 = Evaluate(rows, X2);
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        var (start, end) = context.XExtent;
        for (var i = 0; i < data.Count; i++)
        {
            var y = PointSeries.Center(context.Y, context.MapY(data.Y, i));
            var x1 = data.X1 is null ? start : PointSeries.Center(context.X, context.MapX(data.X1, i));
            var x2 = data.X2 is null ? end : PointSeries.Center(context.X, context.MapX(data.X2, i));
            if (double.IsNaN(y) || double.IsNaN(x1) || double.IsNaN(x2))
                continue;

            var shape = context.Segment(x1, y, x2, y);
            shape.Stroke = data.Stroke is null && StrokeColor is null ? context.Theme.Foreground : GetStroke(context, data, i);
            shape.StrokeWidth = StrokeWidth ?? 1;
            shape.StrokeDashArray = StrokeDashArray;
            group.Add(Decorate(shape, data, i));
        }
    }
}
