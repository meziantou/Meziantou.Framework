namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a line at the x value of each row, across the plot area or from <see cref="Y1"/> to <see cref="Y2"/>: vertical
/// reference lines, the stems of lollipops. In polar coordinates, the lines go out from the center (the needle of a gauge).
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
public class RuleXMark<T> : Mark<T>
{
    /// <summary>Initializes a new instance of the <see cref="RuleXMark{T}"/> class.</summary>
    /// <param name="data">The rows.</param>
    /// <param name="x">The channel of the x position.</param>
    public RuleXMark(IEnumerable<T> data, Func<T, object?> x)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(x);
        X = x;
    }

    /// <summary>Gets the channel of the x position.</summary>
    public Func<T, object?> X { get; }

    /// <summary>Gets or sets the channel of the start of the line, or <see langword="null"/> for the start of the y scale (the center, in polar coordinates).</summary>
    public Func<T, object?>? Y1 { get; set; }

    /// <summary>Gets or sets the channel of the end of the line, or <see langword="null"/> for the end of the y scale.</summary>
    public Func<T, object?>? Y2 { get; set; }

    /// <summary>Gets or sets the lengths of the dashes and gaps, or <see langword="null"/> for a solid line.</summary>
    public IReadOnlyList<double>? StrokeDashArray { get; set; }

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.X = Evaluate(rows, X);
        data.Y1 = Evaluate(rows, Y1);
        data.Y2 = Evaluate(rows, Y2);
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        var (start, end) = context.YExtent;
        if (context.Coordinates is PolarSystem { Transposed: false })
        {
            start = 0;
        }

        for (var i = 0; i < data.Count; i++)
        {
            var x = PointSeries.Center(context.X, context.MapX(data.X, i));
            var y1 = data.Y1 is null ? start : PointSeries.Center(context.Y, context.MapY(data.Y1, i));
            var y2 = data.Y2 is null ? end : PointSeries.Center(context.Y, context.MapY(data.Y2, i));
            if (double.IsNaN(x) || double.IsNaN(y1) || double.IsNaN(y2))
                continue;

            var shape = context.Segment(x, y1, x, y2);
            Style(shape, context, data, i);
            group.Add(Decorate(shape, data, i));
        }
    }

    private protected void Style(SceneShape shape, RenderContext context, MarkData data, int index)
    {
        shape.Stroke = data.Stroke is null && StrokeColor is null ? context.Theme.Foreground : GetStroke(context, data, index);
        shape.StrokeWidth = StrokeWidth ?? 1;
        shape.StrokeDashArray = StrokeDashArray;
    }
}
