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

    /// <summary>
    /// Gets or sets the channel of the <c>data-*</c> attributes of the shape of each row in the SVG output, or
    /// <see langword="null"/> for none. The names are without the <c>data-</c> prefix: lowercase ASCII letters, digits,
    /// hyphens, underscores and dots. The values are formatted with the invariant culture: numbers round-trip, times use
    /// ISO 8601, and <see langword="null"/> skips the attribute. Scripts of a web page read them with
    /// <c>element.dataset</c>. The shapes that draw several rows (the line of a series, a box plot...) get the
    /// attributes of their first row, and the shapes that count rows (the bars of a histogram) get none.
    /// </summary>
    public Func<T, IEnumerable<KeyValuePair<string, object?>>?>? DataAttributes { get; set; }

    /// <summary>Gets or sets the transform applied to the rows before they are drawn (stack, bin, group...), or <see langword="null"/> for none.</summary>
    public Transform? Transform { get; set; }

    /// <summary>
    /// Gets or sets the channel of the column of the facet grid of each row, or <see langword="null"/>. With facets, the
    /// plot is a grid of small charts (small multiples) that share the scales; each row is drawn in the chart of its
    /// facet. A mark without facet channels is drawn in every chart, such as a reference line.
    /// </summary>
    public Func<T, object?>? FacetX { get; set; }

    /// <summary>Gets or sets the channel of the row of the facet grid of each row, or <see langword="null"/>. See <see cref="FacetX"/>.</summary>
    public Func<T, object?>? FacetY { get; set; }

    internal sealed override MarkData Prepare()
    {
        var rows = Data as IReadOnlyList<T> ?? [.. Data];
        var facetX = Evaluate(rows, FacetX)?.ToCategorical();
        var facetY = Evaluate(rows, FacetY)?.ToCategorical();

        // Without rows, there is no facet to prepare: joining no part would also drop the channels of the mark
        if ((facetX is null && facetY is null) || rows.Count is 0)
            return PrepareRows(rows);

        // The rows of each facet are prepared apart, so the transforms (stacks, bins, totals) apply to each facet
        var groups = new Dictionary<(string?, string?), List<T>>();
        var order = new List<(string? X, string? Y)>();
        for (var i = 0; i < rows.Count; i++)
        {
            var key = (facetX?.Categories[i], facetY?.Categories[i]);
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups.Add(key, list);
                order.Add(key);
            }

            list.Add(rows[i]);
        }

        var parts = new List<MarkData>(order.Count);
        foreach (var key in order)
        {
            var part = PrepareRows(groups[key]);
            part.FacetX = facetX is null ? null : ChannelValues.FromCategories([.. Enumerable.Repeat(key.X, part.Count)]);
            part.FacetY = facetY is null ? null : ChannelValues.FromCategories([.. Enumerable.Repeat(key.Y, part.Count)]);
            parts.Add(part);
        }

        return MarkData.Concat(parts);
    }

    private MarkData PrepareRows(IReadOnlyList<T> rows)
    {
        var data = new MarkData(rows.Count)
        {
            Fill = Evaluate(rows, Fill),
            Stroke = Evaluate(rows, Stroke),
            Titles = Evaluate(rows, Title),
            DataAttributes = EvaluateDataAttributes(rows, DataAttributes),
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

    private static KeyValuePair<string, string>[]?[]? EvaluateDataAttributes(IReadOnlyList<T> rows, Func<T, IEnumerable<KeyValuePair<string, object?>>?>? channel)
    {
        if (channel is null)
            return null;

        var values = new KeyValuePair<string, string>[]?[rows.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = DataAttributeValues.Evaluate(channel(rows[i]));
        }

        return values;
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

    /// <summary>Applies the common properties to a shape: the tooltip and the data attributes of the row.</summary>
    private protected static T2 Decorate<T2>(T2 element, MarkData data, int index)
        where T2 : SceneElement
    {
        if (data.Titles is { } titles && index < titles.Length)
        {
            element.Tooltip = titles[index];
        }

        DataAttributeValues.Apply(element, data.DataAttributes, index);
        return element;
    }
}
