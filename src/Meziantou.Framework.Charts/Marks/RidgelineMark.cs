namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a ridgeline plot (a joy plot): the probability density of the x values of each category of y, estimated with
/// a Gaussian kernel, as an area that rises from the position of the category. The areas overlap the categories above
/// them: the tallest one is <see cref="Overlap"/> times the space between two categories, and the others are scaled
/// with it, so their heights compare. The categories are drawn from the top down, each one in front of the previous one.
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
/// <remarks>
/// Unless the plot sets its y scale, the y scale is a point scale that leaves room for the tallest area above the first
/// category.
/// </remarks>
public sealed class RidgelineMark<T> : Mark<T>
{
    private int _samples = 100;
    private double _overlap = 1.5;

    /// <summary>Initializes a new instance of the <see cref="RidgelineMark{T}"/> class.</summary>
    /// <param name="data">The rows: the values of all the categories.</param>
    /// <param name="x">The channel of the value.</param>
    /// <param name="y">The channel of the category.</param>
    public RidgelineMark(IEnumerable<T> data, Func<T, object?> x, Func<T, object?> y)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        X = x;
        Y = y;
    }

    /// <summary>Gets the channel of the value.</summary>
    public Func<T, object?> X { get; }

    /// <summary>Gets the channel of the category.</summary>
    public Func<T, object?> Y { get; }

    /// <summary>Gets or sets the bandwidth of the kernel, in the unit of x, or <see langword="null"/> for Silverman's rule of thumb, per category.</summary>
    public double? Bandwidth { get; set; }

    /// <summary>Gets or sets the number of points of each curve. The default is 100.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 2.</exception>
    public int Samples
    {
        get => _samples;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 2);
            _samples = value;
        }
    }

    /// <summary>Gets or sets how far the curves go beyond the smallest and the largest values of their category, in bandwidths. The default is 3, where the density is almost zero.</summary>
    public double Cut { get; set; } = 3;

    /// <summary>Gets or sets the height of the tallest area, as a multiple of the space between two categories. The default is 1.5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    public double Overlap
    {
        get => _overlap;
        set
        {
            if (!(value > 0) || !double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The overlap must be a positive finite number.");

            _overlap = value;
        }
    }

    /// <summary>Gets or sets the opacity of the fill, from 0 to 1. The default is 1.</summary>
    public double FillOpacity { get; set; } = 1;

    /// <summary>Gets or sets how the points of the curves are joined. The default is <see cref="Curve.MonotoneX"/>.</summary>
    public Curve Curve { get; set; } = Curve.MonotoneX;

    internal override double GetYHeadroom() => Overlap;

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.X = Evaluate(rows, X);
        data.Y = Evaluate(rows, Y)?.ToCategorical();
    }

    internal override MarkData Summarize(MarkData data)
    {
        if (data.Y is null || data.X is not { IsContinuous: true } x)
            return data;

        var firstRows = new List<int>();
        var curves = new List<IReadOnlyList<double>>();
        var extents = new List<double>();
        var titles = new List<string?>();
        foreach (var rows in MarkData.GroupBy(data.Count, data.Y))
        {
            // An infinite value has no position on the axis and no density: it is ignored, as a missing value
            var values = rows.Select(row => x.Numbers[row]).Where(double.IsFinite).Order().ToArray();
            if (values.Length is 0 || data.Y.IsMissing(rows[0]))
                continue;

            var bandwidth = Bandwidth is > 0 ? Bandwidth.Value : KernelDensity.SilvermanBandwidth(values);
            var (points, density) = KernelDensity.Evaluate(values, bandwidth, values[0] - (Cut * bandwidth), values[^1] + (Cut * bandwidth), Samples);

            // The curve of a ridge: the value and its density, alternately
            var curve = new double[points.Length * 2];
            for (var i = 0; i < points.Length; i++)
            {
                curve[2 * i] = points[i];
                curve[(2 * i) + 1] = density[i];
            }

            firstRows.Add(rows[0]);
            curves.Add(curve);
            extents.Add(points[0]);
            extents.Add(points[^1]);
            titles.Add(data.Y.GetCategory(rows[0]) + ": median " + NumberFormats.Default(BoxStatistics.Quantile(values, 0.5)));
        }

        var result = data.Select(firstRows);
        result.X = null;
        result.Titles ??= [.. titles];
        result.Sets = [.. curves];
        result.ExtraX.Add(ChannelValues.FromNumbers([.. extents], x.Kind));
        return result;
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        if (data.Y is null || data.Sets is null || data.Sets.Length is 0 || context.X is not ResolvedContinuousScale xScale || context.Y is not ResolvedOrdinalScale scale)
            return;

        var maxDensity = KernelDensity.MaxDensity(data.Sets);
        if (!(maxDensity > 0))
            return;

        // The areas rise toward the previous category: up on a vertical axis, left on a horizontal one
        var height = Overlap * scale.Step;
        for (var i = 0; i < data.Count; i++)
        {
            var baseline = context.MapY(data.Y, i);
            if (double.IsNaN(baseline))
                continue;

            var curve = data.Sets[i];
            var count = curve.Count / 2;
            if (count is 0)
                continue;

            baseline += scale.Bandwidth / 2;
            var top = new List<(double X, double Y)>(count);
            var first = double.NaN;
            var last = double.NaN;
            for (var j = 0; j < count; j++)
            {
                // A point the scale cannot place (a value out of the domain of a log scale...) is left out of the outline
                var x = xScale.MapNumber(curve[2 * j]);
                var y = baseline - (curve[(2 * j) + 1] / maxDensity * height);
                if (double.IsNaN(x) || !double.IsFinite(y))
                    continue;

                top.Add(context.Coordinates.Project(x, y));
                first = double.IsNaN(first) ? x : first;
                last = x;
            }

            if (top.Count is 0)
                continue;

            var start = context.Coordinates.Project(first, baseline);
            var end = context.Coordinates.Project(last, baseline);
            var area = new PathBuilder();
            CurveBuilder.Append(area, top, Curve);
            area.LineTo(end.X, end.Y).LineTo(start.X, start.Y).Close();
            var line = new PathBuilder();
            CurveBuilder.Append(line, top, Curve);

            // The fill is under the line of the top of the area, which is opaque, in the fill color by default
            var fill = GetFill(context, data, i);
            var ridge = new SceneGroup();
            ridge.Add(new ScenePath(area.Build()) { Fill = FillOpacity < 1 ? fill.WithOpacity(fill.Opacity * FillOpacity) : fill });
            ridge.Add(new ScenePath(line.Build())
            {
                Stroke = data.Stroke is null && StrokeColor is null ? fill : GetStroke(context, data, i),
                StrokeWidth = StrokeWidth ?? 1,
                LineJoin = SceneLineJoin.Round,
            });
            group.Add(Decorate(ridge, data, i));
        }
    }
}
