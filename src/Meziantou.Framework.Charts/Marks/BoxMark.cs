namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a box plot per category of x: a box from the first to the third quartile, a line at the median, whiskers to
/// the most extreme values within 1.5 times the interquartile range, and the outliers as dots (see <see cref="BoxStatistics"/>).
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
public sealed class BoxMark<T> : Mark<T>
{
    private const string LowerQuartile = "q1";
    private const string Median = "median";
    private const string UpperQuartile = "q3";

    /// <summary>Initializes a new instance of the <see cref="BoxMark{T}"/> class.</summary>
    /// <param name="data">The rows: the values of all the categories.</param>
    /// <param name="x">The channel of the category.</param>
    /// <param name="y">The channel of the value.</param>
    public BoxMark(IEnumerable<T> data, Func<T, object?> x, Func<T, object?> y)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        X = x;
        Y = y;
    }

    /// <summary>Gets the channel of the category.</summary>
    public Func<T, object?> X { get; }

    /// <summary>Gets the channel of the value.</summary>
    public Func<T, object?> Y { get; }

    /// <summary>Gets or sets the width of the box, as a fraction of the band. The default is 0.6.</summary>
    public double BoxWidth { get; set; } = 0.6;

    internal override MarkTraits GetTraits(MarkData data) => MarkTraits.BandX;

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.X = Evaluate(rows, X);
        data.Y = Evaluate(rows, Y);
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        if (data.X is null || context.X is null || context.Y is null || data.Extra.Count is 0 || data.Sets is null)
            return;

        var q1 = data.Extra[LowerQuartile];
        var median = data.Extra[Median];
        var q3 = data.Extra[UpperQuartile];
        var outliers = data.Sets;
        var bandwidth = BarMark<T>.GetBandwidth(context.X, data);
        for (var i = 0; i < data.Count; i++)
        {
            var start = context.MapX(data.X, i);
            if (double.IsNaN(start))
                continue;

            var center = start + (bandwidth / 2);
            var half = bandwidth * BoxWidth / 2;
            var color = GetFill(context, data, i);
            var stroke = data.Stroke is null && StrokeColor is null ? color : GetStroke(context, data, i);
            var box = new SceneGroup();
            AddLine(box, context.Segment(center, context.MapY(data.Y1, i), center, MapNumber(context, q1.Numbers[i])), stroke);
            AddLine(box, context.Segment(center, MapNumber(context, q3.Numbers[i]), center, context.MapY(data.Y2, i)), stroke);
            AddLine(box, context.Segment(center - (half / 2), context.MapY(data.Y1, i), center + (half / 2), context.MapY(data.Y1, i)), stroke);
            AddLine(box, context.Segment(center - (half / 2), context.MapY(data.Y2, i), center + (half / 2), context.MapY(data.Y2, i)), stroke);
            var rectangle = context.Coordinates.Rectangle(center - half, center + half, MapNumber(context, q1.Numbers[i]), MapNumber(context, q3.Numbers[i]));
            rectangle.Fill = color.WithOpacity(color.Opacity * 0.35);
            rectangle.Stroke = stroke;
            box.Add(rectangle);
            var medianLine = context.Segment(center - half, MapNumber(context, median.Numbers[i]), center + half, MapNumber(context, median.Numbers[i]));
            AddLine(box, medianLine, stroke);
            medianLine.StrokeWidth = 2;
            foreach (var outlier in outliers[i])
            {
                var (px, py) = context.Coordinates.Project(center, MapNumber(context, outlier));
                box.Add(new SceneCircle(px, py, 2.5) { Stroke = stroke, Fill = null });
            }

            group.Add(Decorate(box, data, i));
        }

        static double MapNumber(RenderContext context, double value) => context.Y is ResolvedContinuousScale scale ? scale.MapNumber(value) : double.NaN;

        static void AddLine(SceneGroup target, SceneShape line, SceneColor color)
        {
            line.Stroke = color;
            target.Add(line);
        }
    }

    internal override MarkData Summarize(MarkData data)
    {
        if (data.X is null || data.Y is not { IsContinuous: true } y)
            return data;

        var groups = MarkData.GroupBy(data.Count, data.X);
        var firstRows = new List<int>();
        var whiskersLow = new List<double>();
        var whiskersHigh = new List<double>();
        var q1 = new List<double>();
        var median = new List<double>();
        var q3 = new List<double>();
        var outliers = new List<IReadOnlyList<double>>();
        var allOutliers = new List<double>();
        foreach (var rows in groups)
        {
            if (BoxStatistics.Summarize(rows.Select(row => y.Numbers[row])) is not { } summary || data.X.IsMissing(rows[0]))
                continue;

            firstRows.Add(rows[0]);
            whiskersLow.Add(summary.LowerWhisker);
            whiskersHigh.Add(summary.UpperWhisker);
            q1.Add(summary.LowerQuartile);
            median.Add(summary.Median);
            q3.Add(summary.UpperQuartile);
            outliers.Add(summary.Outliers);
            allOutliers.AddRange(summary.Outliers);
        }

        var result = data.Select(firstRows);
        result.Y = null;
        result.Titles ??= [.. firstRows.Select((row, index) => data.X.GetCategory(row) + ": median " + NumberFormats.Default(median[index]))];
        result.Y1 = ChannelValues.FromNumbers([.. whiskersLow]);
        result.Y2 = ChannelValues.FromNumbers([.. whiskersHigh]);
        result.Extra[LowerQuartile] = ChannelValues.FromNumbers([.. q1]);
        result.Extra[Median] = ChannelValues.FromNumbers([.. median]);
        result.Extra[UpperQuartile] = ChannelValues.FromNumbers([.. q3]);
        result.ExtraY.Add(ChannelValues.FromNumbers([.. allOutliers]));
        result.Sets = [.. outliers];
        return result;
    }
}
