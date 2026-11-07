namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a rectangle per row. On each axis, the rectangle covers the band of a category (<see cref="X"/>,
/// <see cref="Y"/>), the interval between two values (<see cref="X1"/> and <see cref="X2"/>, <see cref="Y1"/> and
/// <see cref="Y2"/>), or the whole plot area when no channel is set. Two band axes and a numeric fill make a heatmap; a
/// <see cref="BinTransform"/> on x makes a histogram.
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
public sealed class RectMark<T> : Mark<T>
{
    /// <summary>Initializes a new instance of the <see cref="RectMark{T}"/> class.</summary>
    /// <param name="data">The rows.</param>
    public RectMark(IEnumerable<T> data)
        : base(data)
    {
    }

    /// <summary>Gets or sets the channel of the category on the x axis (a band), or of the values to bin.</summary>
    public Func<T, object?>? X { get; set; }

    /// <summary>Gets or sets the channel of the start of the rectangle on the x axis.</summary>
    public Func<T, object?>? X1 { get; set; }

    /// <summary>Gets or sets the channel of the end of the rectangle on the x axis.</summary>
    public Func<T, object?>? X2 { get; set; }

    /// <summary>Gets or sets the channel of the category on the y axis (a band).</summary>
    public Func<T, object?>? Y { get; set; }

    /// <summary>Gets or sets the channel of the start of the rectangle on the y axis.</summary>
    public Func<T, object?>? Y1 { get; set; }

    /// <summary>Gets or sets the channel of the end of the rectangle on the y axis. Without <see cref="Y1"/>, the rectangle starts at the baseline.</summary>
    public Func<T, object?>? Y2 { get; set; }

    /// <summary>Gets or sets the channel of a label drawn in the middle of the rectangle when it fits.</summary>
    public Func<T, string?>? Text { get; set; }

    /// <summary>Gets or sets the space between adjacent rectangles, in pixels. The default is 1.</summary>
    public double Inset { get; set; } = 1;

    /// <summary>Gets or sets the radius of the corners. The default is 0.</summary>
    public double CornerRadius { get; set; }

    internal override MarkTraits GetTraits(MarkData data)
    {
        var traits = MarkTraits.None;
        if (data.X is not null && data.X1 is null && data.X2 is null)
        {
            traits |= MarkTraits.BandX;
        }

        if (data.Y is not null && data.Y1 is null && data.Y2 is null)
        {
            traits |= MarkTraits.BandY;
        }

        if (data.Y2 is not null && data.Y1 is null)
        {
            traits |= MarkTraits.ZeroY;
        }

        if (data.X2 is not null && data.X1 is null)
        {
            traits |= MarkTraits.ZeroX;
        }

        return traits;
    }

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.X = Evaluate(rows, X);
        data.X1 = Evaluate(rows, X1);
        data.X2 = Evaluate(rows, X2);
        data.Y = Evaluate(rows, Y);
        data.Y1 = Evaluate(rows, Y1);
        data.Y2 = Evaluate(rows, Y2);
        data.Text = Evaluate(rows, Text);
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        for (var i = 0; i < data.Count; i++)
        {
            var (x1, x2) = GetInterval(context.X, context.XExtent, data.X, data.X1, data.X2, i);
            var (y1, y2) = GetInterval(context.Y, context.YExtent, data.Y, data.Y1, data.Y2, i);
            if (double.IsNaN(x1) || double.IsNaN(x2) || double.IsNaN(y1) || double.IsNaN(y2))
                continue;

            var shape = context.Coordinates.Rectangle(x1, x2, y1, y2);
            if (shape is SceneRectangle rectangle)
            {
                var inset = Math.Min(Inset / 2, Math.Min(rectangle.Width, rectangle.Height) / 4);
                shape = new SceneRectangle(rectangle.X + inset, rectangle.Y + inset, Math.Max(0, rectangle.Width - (2 * inset)), Math.Max(0, rectangle.Height - (2 * inset))) { CornerRadius = CornerRadius };
            }

            var fill = GetFill(context, data, i);
            shape.Fill = fill;
            if (data.Stroke is not null || StrokeColor is not null)
            {
                shape.Stroke = GetStroke(context, data, i);
                shape.StrokeWidth = StrokeWidth ?? 1;
            }

            group.Add(Decorate(shape, data, i));
            if (data.Text is { } text && shape is SceneRectangle box)
            {
                context.AddCenteredLabel(group, text[i], box.X + (box.Width / 2), box.Y + (box.Height / 2), fill, box.Width - 2, box.Height);
            }
        }
    }

    private static (double Start, double End) GetInterval(ResolvedScale? scale, (double Start, double End) extent, ChannelValues? band, ChannelValues? start, ChannelValues? end, int index)
    {
        if (scale is null)
            return extent;

        if (start is null && end is null)
        {
            if (band is null)
                return extent;

            var position = scale.Map(band, index);
            return (position, position + scale.Bandwidth);
        }

        var from = start is null ? scale.Baseline : scale.Map(start, index);
        var to = end is null ? scale.Baseline : scale.Map(end, index);
        return (from, to);
    }
}
