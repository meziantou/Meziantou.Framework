namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a funnel: a centered bar per stage, from the top down, whose width is proportional to its value, joined to
/// the next stage by a lighter trapezoid. Each bar shows its value, and the y axis shows the stages.
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
public sealed class FunnelMark<T> : Mark<T>
{
    /// <summary>Initializes a new instance of the <see cref="FunnelMark{T}"/> class.</summary>
    /// <param name="data">The rows: the stages, in order.</param>
    /// <param name="stage">The channel of the name of the stage.</param>
    /// <param name="value">The channel of the value of the stage.</param>
    public FunnelMark(IEnumerable<T> data, Func<T, object?> stage, Func<T, object?> value)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(value);
        Stage = stage;
        Value = value;
    }

    /// <summary>Gets the channel of the name of the stage.</summary>
    public Func<T, object?> Stage { get; }

    /// <summary>Gets the channel of the value of the stage.</summary>
    public Func<T, object?> Value { get; }

    /// <summary>Gets or sets the channel of the label of each bar, or <see langword="null"/> for its value.</summary>
    public Func<T, string?>? Label { get; set; }

    internal override MarkTraits GetTraits(MarkData data) => MarkTraits.BandY | MarkTraits.ExactX | MarkTraits.HideXAxis;

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        var values = Evaluate(rows, Value)!;
        var x1 = new double[rows.Count];
        var x2 = new double[rows.Count];
        var labels = Evaluate(rows, Label) ?? new string?[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var value = values.GetNumber(i);
            x1[i] = -value / 2;
            x2[i] = value / 2;
            if (Label is null && !double.IsNaN(value))
            {
                labels[i] = NumberFormats.Default(value);
            }
        }

        data.Y = Evaluate(rows, Stage);
        data.X1 = ChannelValues.FromNumbers(x1);
        data.X2 = ChannelValues.FromNumbers(x2);
        data.Text = labels;
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        if (context.X is null || context.Y is null || data.Y is null)
            return;

        var previous = (Left: double.NaN, Right: double.NaN, Bottom: double.NaN);
        for (var i = 0; i < data.Count; i++)
        {
            var top = context.MapY(data.Y, i);
            var left = context.MapX(data.X1, i);
            var right = context.MapX(data.X2, i);
            if (double.IsNaN(top) || double.IsNaN(left) || double.IsNaN(right))
                continue;

            var bottom = top + context.Y.Bandwidth;
            var fill = GetFill(context, data, i);
            if (!double.IsNaN(previous.Bottom) && context.Coordinates is CartesianSystem { Transposed: false })
            {
                group.Add(new ScenePath(new PathBuilder().MoveTo(previous.Left, previous.Bottom).LineTo(previous.Right, previous.Bottom).LineTo(right, top).LineTo(left, top).Close().Build())
                {
                    Fill = fill.WithOpacity(fill.Opacity * 0.3),
                });
            }

            var shape = context.Coordinates.Rectangle(left, right, top, bottom);
            shape.Fill = fill;
            group.Add(Decorate(shape, data, i));
            if (shape is SceneRectangle rectangle)
            {
                context.AddCenteredLabel(group, data.Text?[i], rectangle.X + (rectangle.Width / 2), rectangle.Y + (rectangle.Height / 2), fill, rectangle.Width - 4, rectangle.Height);
            }

            previous = (Math.Min(left, right), Math.Max(left, right), bottom);
        }
    }
}
