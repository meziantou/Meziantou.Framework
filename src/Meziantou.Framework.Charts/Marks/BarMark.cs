namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a bar per row: a band of the x scale (a category), from the baseline to the y value. With a
/// <see cref="StackTransform"/>, the bars of a category are stacked; with <see cref="Group"/>, they are side by side.
/// Transposed coordinates make horizontal bars; polar coordinates make a rose (or radial bars, transposed).
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
public sealed class BarMark<T> : Mark<T>
{
    /// <summary>Initializes a new instance of the <see cref="BarMark{T}"/> class.</summary>
    /// <param name="data">The rows.</param>
    /// <param name="x">The channel of the category of the bar.</param>
    /// <param name="y">The channel of the value of the bar.</param>
    public BarMark(IEnumerable<T> data, Func<T, object?> x, Func<T, object?> y)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        X = x;
        Y = y;
    }

    /// <summary>Gets the channel of the category of the bar.</summary>
    public Func<T, object?> X { get; }

    /// <summary>Gets the channel of the value of the bar.</summary>
    public Func<T, object?> Y { get; }

    /// <summary>Gets or sets the channel that divides each band into side-by-side bars (grouped bars), or <see langword="null"/>.</summary>
    public Func<T, object?>? Group { get; set; }

    /// <summary>Gets or sets the channel of the series to stack, when it differs from <see cref="Mark{T}.Fill"/>.</summary>
    public Func<T, object?>? Series { get; set; }

    /// <summary>Gets or sets the radius of the corners of the bars. The default is 0.</summary>
    public double CornerRadius { get; set; }

    /// <summary>Gets or sets the space between the bars of a group, as a fraction of their width. The default is 0.05.</summary>
    public double GroupPadding { get; set; } = 0.05;

    internal override MarkTraits GetTraits(MarkData data) => MarkTraits.BandX | MarkTraits.ZeroY;

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.X = Evaluate(rows, X);
        data.Y = Evaluate(rows, Y);
        data.Group = Evaluate(rows, Group)?.ToCategorical();
        data.Series = Evaluate(rows, Series)?.ToCategorical();
        if (Group is not null || Transform is StackTransform)
        {
            // The colors name the groups or the stacked series
            data.Fill = data.Fill?.ToCategorical();
            data.Stroke = data.Stroke?.ToCategorical();
        }
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        if (context.X is null || context.Y is null || data.X is null)
            return;

        var groups = data.Group is null ? null : ScaleResolver.GetCategories([data.Group]);
        var bandwidth = GetBandwidth(context.X, data);
        for (var i = 0; i < data.Count; i++)
        {
            var start = context.MapX(data.X, i);
            if (double.IsNaN(start))
                continue;

            if (!context.X.IsOrdinal)
            {
                start -= bandwidth / 2;
            }

            var width = bandwidth;
            if (groups is { Count: > 0 } && data.Group!.GetCategory(i) is { } category)
            {
                var step = bandwidth / groups.Count;
                start += step * groups.IndexOf(category) + (step * GroupPadding / 2);
                width = step * (1 - GroupPadding);
            }

            double y1;
            double y2;
            if (data.Y1 is not null && data.Y2 is not null)
            {
                y1 = context.MapY(data.Y1, i);
                y2 = context.MapY(data.Y2, i);
            }
            else
            {
                y1 = context.Y.Baseline;
                y2 = context.MapY(data.Y, i);
            }

            if (double.IsNaN(y1) || double.IsNaN(y2))
                continue;

            var shape = context.Coordinates.Rectangle(start, start + width, y1, y2);
            shape.Fill = GetFill(context, data, i);
            if (data.Stroke is not null || StrokeColor is not null)
            {
                shape.Stroke = GetStroke(context, data, i);
                shape.StrokeWidth = StrokeWidth ?? 1;
            }

            if (shape is SceneRectangle rectangle)
            {
                rectangle.CornerRadius = CornerRadius;
            }

            group.Add(Decorate(shape, data, i));
        }
    }

    /// <summary>The width of a bar: the band, or a fraction of the smallest distance between two bars on a continuous scale.</summary>
    internal static double GetBandwidth(ResolvedScale scale, MarkData data)
    {
        if (scale.IsOrdinal)
            return scale.Bandwidth > 0 ? scale.Bandwidth : ((ResolvedOrdinalScale)scale).Step * 0.6;

        var positions = new List<double>();
        for (var i = 0; i < data.Count; i++)
        {
            var position = scale.Map(data.X!, i);
            if (!double.IsNaN(position))
            {
                positions.Add(position);
            }
        }

        positions.Sort();
        var spacing = double.PositiveInfinity;
        for (var i = 1; i < positions.Count; i++)
        {
            if (positions[i] > positions[i - 1])
            {
                spacing = Math.Min(spacing, positions[i] - positions[i - 1]);
            }
        }

        return double.IsFinite(spacing) ? spacing * 0.6 : Math.Abs(scale.RangeEnd - scale.RangeStart) * 0.1;
    }
}
