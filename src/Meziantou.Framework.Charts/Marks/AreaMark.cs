namespace Meziantou.Framework.Charts;

/// <summary>
/// Fills the area between a line through the rows and a baseline, one area per series (the <see cref="Series"/>
/// channel, else <see cref="Mark{T}.Fill"/>). The baseline is <see cref="Y1"/>, else zero. With a
/// <see cref="StackTransform"/>, the areas are stacked; with <see cref="StackOffset.Silhouette"/>, they make a streamgraph.
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
public sealed class AreaMark<T> : Mark<T>
{
    /// <summary>Initializes a new instance of the <see cref="AreaMark{T}"/> class.</summary>
    /// <param name="data">The rows, in the order of the line.</param>
    /// <param name="x">The channel of the x position.</param>
    /// <param name="y">The channel of the y position of the top of the area.</param>
    public AreaMark(IEnumerable<T> data, Func<T, object?> x, Func<T, object?> y)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        X = x;
        Y = y;
    }

    /// <summary>Gets the channel of the x position.</summary>
    public Func<T, object?> X { get; }

    /// <summary>Gets the channel of the y position of the top of the area.</summary>
    public Func<T, object?> Y { get; }

    /// <summary>Gets or sets the channel of the baseline, or <see langword="null"/> for zero.</summary>
    public Func<T, object?>? Y1 { get; set; }

    /// <summary>Gets or sets the channel of the series, when it differs from <see cref="Mark{T}.Fill"/>.</summary>
    public Func<T, object?>? Series { get; set; }

    /// <summary>Gets or sets how the points are joined. The default is <see cref="Curve.Linear"/>.</summary>
    public Curve Curve { get; set; }

    /// <summary>Gets or sets a value that indicates whether the area is a closed polygon, for radar charts in polar coordinates.</summary>
    public bool Closed { get; set; }

    /// <summary>Gets or sets the opacity of the fill, from 0 to 1. The default is 1.</summary>
    public double FillOpacity { get; set; } = 1;

    internal override MarkTraits GetTraits(MarkData data) => MarkTraits.ZeroY;

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.X = Evaluate(rows, X);
        data.Y = Evaluate(rows, Y);
        data.Y1 = Evaluate(rows, Y1);
        data.Series = Evaluate(rows, Series)?.ToCategorical();
        if (Series is null)
        {
            // The fill names the series of the areas
            data.Fill = data.Fill?.ToCategorical();
        }
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        if (context.Y is null)
            return;

        var top = data.Y2 ?? data.Y;
        foreach (var rows in MarkData.GroupBy(data.Count, data.Series ?? data.Fill))
        {
            if (rows.Count is 0)
                continue;

            var builder = new PathBuilder();
            var evenOdd = false;
            foreach (var segment in PointSeries.Split(context, data.X, top, rows))
            {
                var topPoints = segment.Select(point => point.Point).ToList();
                var bottomPoints = new List<(double X, double Y)>(segment.Count);
                var x = data.X;
                foreach (var (_, row) in segment)
                {
                    var baseline = data.Y1 is null ? context.Y.Baseline : context.MapY(data.Y1, row);
                    if (double.IsNaN(baseline))
                    {
                        baseline = context.Y.Baseline;
                    }

                    bottomPoints.Add(context.Coordinates.Project(PointSeries.Center(context.X, context.MapX(x, row)), baseline));
                }

                if (Closed)
                {
                    CurveBuilder.Append(builder, topPoints, Curve, closed: true);
                    if (bottomPoints.Any(point => context.Coordinates is not PolarSystem polar || Math.Abs(point.X - polar.CenterX) > 0.01 || Math.Abs(point.Y - polar.CenterY) > 0.01))
                    {
                        bottomPoints.Reverse();
                        CurveBuilder.Append(builder, bottomPoints, Curve, closed: true);
                        evenOdd = true;
                    }

                    continue;
                }

                bottomPoints.Reverse();
                CurveBuilder.Append(builder, topPoints, Curve);
                CurveBuilder.Append(builder, bottomPoints, Curve, connect: true);
                builder.Close();
            }

            if (builder.IsEmpty)
                continue;

            var fill = GetFill(context, data, rows[0]);
            var path = new ScenePath(builder.Build())
            {
                Fill = FillOpacity < 1 ? fill.WithOpacity(fill.Opacity * FillOpacity) : fill,
                FillRule = evenOdd ? SceneFillRule.EvenOdd : SceneFillRule.NonZero,
            };
            if (data.Stroke is not null || StrokeColor is not null)
            {
                path.Stroke = GetStroke(context, data, rows[0]);
                path.StrokeWidth = StrokeWidth ?? context.Theme.LineWidth;
                path.LineJoin = SceneLineJoin.Round;
            }

            group.Add(Decorate(path, data, rows[0]));
        }
    }
}
