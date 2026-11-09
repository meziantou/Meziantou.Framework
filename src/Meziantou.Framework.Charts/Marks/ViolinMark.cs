namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a violin plot per category of x: the probability density of the y values, mirrored on both sides of the
/// middle of the band, from the smallest value to the largest. A thin box from the first to the third quartile and a
/// dot at the median are drawn inside, unless <see cref="ShowBox"/> is off. The widths of the violins are proportional
/// to their densities: the widest point of all the violins fills <see cref="ViolinWidth"/> of the band.
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
public sealed class ViolinMark<T> : Mark<T>
{
    private const string LowerQuartile = "q1";
    private const string Median = "median";
    private const string UpperQuartile = "q3";
    private int _samples = 64;

    /// <summary>Initializes a new instance of the <see cref="ViolinMark{T}"/> class.</summary>
    /// <param name="data">The rows: the values of all the categories.</param>
    /// <param name="x">The channel of the category.</param>
    /// <param name="y">The channel of the value.</param>
    public ViolinMark(IEnumerable<T> data, Func<T, object?> x, Func<T, object?> y)
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

    /// <summary>Gets or sets the bandwidth of the kernel, in the unit of y, or <see langword="null"/> for Silverman's rule of thumb, per category.</summary>
    public double? Bandwidth { get; set; }

    /// <summary>Gets or sets the number of points of each side of a violin. The default is 64.</summary>
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

    /// <summary>Gets or sets the width of the widest violin, as a fraction of the band. The default is 0.9.</summary>
    public double ViolinWidth { get; set; } = 0.9;

    /// <summary>Gets or sets a value that indicates whether the quartiles and the median are drawn inside the violins. The default is <see langword="true"/>.</summary>
    public bool ShowBox { get; set; } = true;

    internal override MarkTraits GetTraits(MarkData data) => MarkTraits.BandX;

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.X = Evaluate(rows, X);
        data.Y = Evaluate(rows, Y);
    }

    internal override MarkData Summarize(MarkData data)
    {
        if (data.X is null || data.Y is not { IsContinuous: true } y)
            return data;

        var firstRows = new List<int>();
        var minimums = new List<double>();
        var maximums = new List<double>();
        var q1 = new List<double>();
        var median = new List<double>();
        var q3 = new List<double>();
        var curves = new List<IReadOnlyList<double>>();
        foreach (var rows in MarkData.GroupBy(data.Count, data.X))
        {
            var values = rows.Select(row => y.Numbers[row]).Where(value => !double.IsNaN(value)).Order().ToArray();
            if (values.Length is 0 || data.X.IsMissing(rows[0]))
                continue;

            var bandwidth = Bandwidth is > 0 ? Bandwidth.Value : KernelDensity.SilvermanBandwidth(values);
            var (points, density) = KernelDensity.Evaluate(values, bandwidth, values[0], values[^1], values[0] == values[^1] ? 2 : Samples);

            // The curve of a violin: the value and its density, alternately
            var curve = new double[points.Length * 2];
            for (var i = 0; i < points.Length; i++)
            {
                curve[2 * i] = points[i];
                curve[(2 * i) + 1] = density[i];
            }

            firstRows.Add(rows[0]);
            minimums.Add(values[0]);
            maximums.Add(values[^1]);
            q1.Add(BoxStatistics.Quantile(values, 0.25));
            median.Add(BoxStatistics.Quantile(values, 0.5));
            q3.Add(BoxStatistics.Quantile(values, 0.75));
            curves.Add(curve);
        }

        var result = data.Select(firstRows);
        result.Y = null;
        result.Titles ??= [.. firstRows.Select((row, index) => data.X.GetCategory(row) + ": median " + NumberFormats.Default(median[index]))];
        result.Y1 = ChannelValues.FromNumbers([.. minimums]);
        result.Y2 = ChannelValues.FromNumbers([.. maximums]);
        result.Extra[LowerQuartile] = ChannelValues.FromNumbers([.. q1]);
        result.Extra[Median] = ChannelValues.FromNumbers([.. median]);
        result.Extra[UpperQuartile] = ChannelValues.FromNumbers([.. q3]);
        result.Sets = [.. curves];
        return result;
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        if (data.X is null || context.X is null || context.Y is null || data.Sets is null)
            return;

        var maxDensity = data.Sets.Max(curve => Enumerable.Range(0, curve.Count / 2).Max(i => curve[(2 * i) + 1]));
        var bandwidth = BarMark<T>.GetBandwidth(context.X, data);
        for (var i = 0; i < data.Count; i++)
        {
            var start = context.MapX(data.X, i);
            if (double.IsNaN(start) || !(maxDensity > 0))
                continue;

            var center = start + (bandwidth / 2);
            var half = bandwidth * ViolinWidth / 2;
            var curve = data.Sets[i];
            var count = curve.Count / 2;
            var left = new List<(double X, double Y)>(count);
            var right = new List<(double X, double Y)>(count);
            for (var j = 0; j < count; j++)
            {
                var y = context.MapYNumber(curve[2 * j]);
                var offset = curve[(2 * j) + 1] / maxDensity * half;
                left.Add(context.Coordinates.Project(center - offset, y));
                right.Add(context.Coordinates.Project(center + offset, y));
            }

            right.Reverse();
            var builder = new PathBuilder();
            CurveBuilder.Append(builder, left, Curve.Linear);
            CurveBuilder.Append(builder, right, Curve.Linear, connect: true);
            builder.Close();

            var fill = GetFill(context, data, i);
            var violin = new SceneGroup();
            violin.Add(new ScenePath(builder.Build())
            {
                Fill = fill.WithOpacity(fill.Opacity * 0.7),
                Stroke = data.Stroke is null && StrokeColor is null ? fill : GetStroke(context, data, i),
                StrokeWidth = StrokeWidth ?? 1,
                LineJoin = SceneLineJoin.Round,
            });

            if (ShowBox)
            {
                var boxHalf = Math.Max(1.5, half * 0.08);
                var box = context.Coordinates.Rectangle(center - boxHalf, center + boxHalf, context.MapYNumber(data.Extra[LowerQuartile].Numbers[i]), context.MapYNumber(data.Extra[UpperQuartile].Numbers[i]));
                box.Fill = context.Theme.Foreground;
                violin.Add(box);
                var (mx, my) = context.Coordinates.Project(center, context.MapYNumber(data.Extra[Median].Numbers[i]));
                violin.Add(new SceneCircle(mx, my, Math.Max(2, boxHalf * 1.2)) { Fill = context.Theme.Background ?? SceneColor.White });
            }

            group.Add(Decorate(violin, data, i));
        }
    }
}
