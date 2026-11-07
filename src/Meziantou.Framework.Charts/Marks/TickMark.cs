namespace Meziantou.Framework.Charts;

/// <summary>Draws a short line at the x value of each row, across the band of its y category (or across the plot area): a strip plot or a barcode plot.</summary>
/// <typeparam name="T">The type of the rows.</typeparam>
public sealed class TickMark<T> : Mark<T>
{
    /// <summary>Initializes a new instance of the <see cref="TickMark{T}"/> class.</summary>
    /// <param name="data">The rows.</param>
    /// <param name="x">The channel of the x position.</param>
    /// <param name="y">The channel of the category on the y axis, or <see langword="null"/> to span the plot area.</param>
    public TickMark(IEnumerable<T> data, Func<T, object?> x, Func<T, object?>? y = null)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(x);
        X = x;
        Y = y;
    }

    /// <summary>Gets the channel of the x position.</summary>
    public Func<T, object?> X { get; }

    /// <summary>Gets the channel of the category on the y axis.</summary>
    public Func<T, object?>? Y { get; }

    internal override MarkTraits GetTraits(MarkData data) => data.Y is null ? MarkTraits.None : MarkTraits.BandY;

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.X = Evaluate(rows, X);
        data.Y = Evaluate(rows, Y);
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        for (var i = 0; i < data.Count; i++)
        {
            var x = PointSeries.Center(context.X, context.MapX(data.X, i));
            double y1;
            double y2;
            if (data.Y is null || context.Y is null)
            {
                (y1, y2) = context.YExtent;
            }
            else
            {
                y1 = context.MapY(data.Y, i);
                y2 = y1 + context.Y.Bandwidth;
            }

            if (double.IsNaN(x) || double.IsNaN(y1))
                continue;

            var shape = context.Segment(x, y1, x, y2);
            shape.Stroke = data.Stroke is null && StrokeColor is null ? context.DefaultColor : GetStroke(context, data, i);
            shape.StrokeWidth = StrokeWidth ?? 1;
            group.Add(Decorate(shape, data, i));
        }
    }
}
