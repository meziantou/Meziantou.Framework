namespace Meziantou.Framework.Charts;

/// <summary>A mark that draws a sequence of rows of type <typeparamref name="T"/>, one shape (or one point of a line) per row.</summary>
/// <typeparam name="T">The type of the rows.</typeparam>
/// <remarks>
/// A channel is a function of a row. It returns a number (any numeric type), a time (<see cref="DateTime"/>,
/// <see cref="DateTimeOffset"/>, <see cref="DateOnly"/>) or a category (strings and anything else, formatted with the
/// invariant culture). <see langword="null"/> and <see cref="double.NaN"/> are missing values: the row is not drawn, and
/// lines break around it.
/// </remarks>
public abstract class Mark<T> : Mark
{
    private protected Mark(IEnumerable<T> data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
    }

    /// <summary>Gets the rows of the mark.</summary>
    public IEnumerable<T> Data { get; }

    /// <summary>Gets or sets the channel of the fill color, mapped by the color scale, or <see langword="null"/> to use <see cref="FillColor"/>.</summary>
    public Func<T, object?>? Fill { get; set; }

    /// <summary>Gets or sets the fill color when <see cref="Fill"/> is not set, or <see langword="null"/> for the first color of the theme.</summary>
    public ChartColor? FillColor { get; set; }

    /// <summary>Gets or sets the channel of the stroke color, mapped by the color scale, or <see langword="null"/> to use <see cref="StrokeColor"/>.</summary>
    public Func<T, object?>? Stroke { get; set; }

    /// <summary>Gets or sets the stroke color when <see cref="Stroke"/> is not set, or <see langword="null"/> for the default of the mark.</summary>
    public ChartColor? StrokeColor { get; set; }

    /// <summary>Gets or sets the stroke width, or <see langword="null"/> for the default of the mark.</summary>
    public double? StrokeWidth { get; set; }

    /// <summary>Gets or sets the channel of the tooltips (an SVG <c>title</c> per shape), or <see langword="null"/> for none.</summary>
    public Func<T, string?>? Title { get; set; }

    /// <summary>Gets or sets the transform applied to the rows before they are drawn (stack, bin, group...), or <see langword="null"/> for none.</summary>
    public Transform? Transform { get; set; }

    internal sealed override MarkData Prepare()
    {
        var rows = Data as IReadOnlyList<T> ?? [.. Data];
        var data = new MarkData(rows.Count)
        {
            Fill = Evaluate(rows, Fill),
            Stroke = Evaluate(rows, Stroke),
            Titles = Evaluate(rows, Title),
        };

        PrepareChannels(rows, data);
        data = Transform is null ? data : Transform.Apply(data);
        return Summarize(data);
    }

    /// <summary>Derives the rows the mark draws from the rows of the data, after the transform (the statistics of box plots...).</summary>
    internal virtual MarkData Summarize(MarkData data) => data;

    /// <summary>Evaluates the channels of the mark.</summary>
    private protected abstract void PrepareChannels(IReadOnlyList<T> rows, MarkData data);

    private protected static ChannelValues? Evaluate(IReadOnlyList<T> rows, Func<T, object?>? channel)
    {
        if (channel is null)
            return null;

        var values = new object?[rows.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = channel(rows[i]);
        }

        return ChannelValues.FromObjects(values);
    }

    private protected static string?[]? Evaluate(IReadOnlyList<T> rows, Func<T, string?>? channel)
    {
        if (channel is null)
            return null;

        var values = new string?[rows.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = channel(rows[i]);
        }

        return values;
    }

    private protected ChartColor GetFill(RenderContext context, MarkData data, int index) => context.GetColor(data.Fill, index, FillColor);

    private protected ChartColor GetStroke(RenderContext context, MarkData data, int index) => context.GetColor(data.Stroke, index, StrokeColor);

    /// <summary>Applies the common properties to a shape: the tooltip of the row.</summary>
    private protected static T2 Decorate<T2>(T2 element, MarkData data, int index)
        where T2 : SceneElement
    {
        if (data.Titles is { } titles && index < titles.Length)
        {
            element.Tooltip = titles[index];
        }

        return element;
    }
}
