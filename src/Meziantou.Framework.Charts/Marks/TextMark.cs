namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a text per row at its x and y positions. A position without a channel is the middle of the plot area (the
/// center, in polar coordinates), which places a label in the middle of a donut or a gauge.
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
public sealed class TextMark<T> : Mark<T>
{
    /// <summary>Initializes a new instance of the <see cref="TextMark{T}"/> class.</summary>
    /// <param name="data">The rows.</param>
    /// <param name="text">The channel of the text.</param>
    /// <param name="x">The channel of the x position, or <see langword="null"/> for the middle of the plot area.</param>
    /// <param name="y">The channel of the y position, or <see langword="null"/> for the middle of the plot area.</param>
    public TextMark(IEnumerable<T> data, Func<T, string?> text, Func<T, object?>? x = null, Func<T, object?>? y = null)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        X = x;
        Y = y;
    }

    /// <summary>Gets the channel of the text.</summary>
    public Func<T, string?> Text { get; }

    /// <summary>Gets the channel of the x position.</summary>
    public Func<T, object?>? X { get; }

    /// <summary>Gets the channel of the y position.</summary>
    public Func<T, object?>? Y { get; }

    /// <summary>Gets or sets where the text is anchored horizontally. The default is <see cref="SceneTextAnchor.Middle"/>.</summary>
    public SceneTextAnchor Anchor { get; set; } = SceneTextAnchor.Middle;

    /// <summary>Gets or sets the horizontal offset of the text, in pixels.</summary>
    public double OffsetX { get; set; }

    /// <summary>Gets or sets the vertical offset of the text, in pixels: a negative value moves it up.</summary>
    public double OffsetY { get; set; }

    /// <summary>Gets or sets the size of the font, or <see langword="null"/> for the size of the labels of the theme.</summary>
    public double? FontSize { get; set; }

    /// <summary>Gets or sets a value that indicates whether the text is bold.</summary>
    public bool Bold { get; set; }

    /// <summary>Gets or sets the rotation of the text in degrees, clockwise.</summary>
    public double Rotation { get; set; }

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.X = Evaluate(rows, X);
        data.Y = Evaluate(rows, Y);
        data.Text = Evaluate(rows, Text);
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        var font = new SceneFont(FontSize ?? context.Theme.FontSize, Bold);
        for (var i = 0; i < data.Count; i++)
        {
            var text = data.Text![i];
            if (string.IsNullOrEmpty(text))
                continue;

            var (px, py) = GetPosition(context, data, i);
            if (double.IsNaN(px) || double.IsNaN(py))
                continue;

            var metrics = context.Measurer.Measure(text, font);
            var color = data.Fill is null && FillColor is null ? context.Theme.Foreground : GetFill(context, data, i);
            group.Add(Decorate(new SceneText(px + OffsetX, py + OffsetY + ((metrics.Ascent - metrics.Descent) / 2), text, font, color)
            {
                Anchor = Anchor,
                Rotation = Rotation,
                MeasuredWidth = metrics.Width,
            }, data, i));
        }
    }

    private static (double X, double Y) GetPosition(RenderContext context, MarkData data, int index)
    {
        if (data.X is null && data.Y is null)
        {
            return context.Coordinates is PolarSystem polar ? (polar.CenterX, polar.CenterY) : (context.Frame.CenterX, context.Frame.CenterY);
        }

        var (xStart, xEnd) = context.XExtent;
        var (yStart, yEnd) = context.YExtent;
        var x = data.X is null ? (xStart + xEnd) / 2 : PointSeries.Center(context.X, context.MapX(data.X, index));
        var y = data.Y is null ? (yStart + yEnd) / 2 : PointSeries.Center(context.Y, context.MapY(data.Y, index));
        return double.IsNaN(x) || double.IsNaN(y) ? (double.NaN, double.NaN) : context.Coordinates.Project(x, y);
    }
}
