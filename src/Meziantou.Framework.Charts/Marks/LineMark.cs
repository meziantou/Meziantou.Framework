namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a line through the rows, one line per series (the <see cref="Series"/> channel, else the
/// <see cref="Mark{T}.Stroke"/> channel). A missing value breaks the line. In polar coordinates with
/// <see cref="Closed"/>, it draws the outline of a radar chart.
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
public sealed class LineMark<T> : Mark<T>
{
    /// <summary>Initializes a new instance of the <see cref="LineMark{T}"/> class.</summary>
    /// <param name="data">The rows, in the order of the line.</param>
    /// <param name="x">The channel of the x position.</param>
    /// <param name="y">The channel of the y position.</param>
    public LineMark(IEnumerable<T> data, Func<T, object?> x, Func<T, object?> y)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        X = x;
        Y = y;
    }

    /// <summary>Gets the channel of the x position.</summary>
    public Func<T, object?> X { get; }

    /// <summary>Gets the channel of the y position.</summary>
    public Func<T, object?> Y { get; }

    /// <summary>Gets or sets the channel of the series, when it differs from <see cref="Mark{T}.Stroke"/>.</summary>
    public Func<T, object?>? Series { get; set; }

    /// <summary>Gets or sets how the points are joined. The default is <see cref="Curve.Linear"/>.</summary>
    public Curve Curve { get; set; }

    /// <summary>Gets or sets a value that indicates whether the last point of each line joins its first point.</summary>
    public bool Closed { get; set; }

    /// <summary>Gets or sets the lengths of the dashes and gaps, or <see langword="null"/> for a solid line.</summary>
    public IReadOnlyList<double>? StrokeDashArray { get; set; }

    /// <summary>Gets or sets the radius of a dot drawn at each point, or 0 for none. The default is 0.</summary>
    public double MarkerRadius { get; set; }

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.X = Evaluate(rows, X);
        data.Y = Evaluate(rows, Y);
        data.Series = Evaluate(rows, Series)?.ToCategorical();
        if (Series is null)
        {
            // The stroke names the series of the lines
            data.Stroke = data.Stroke?.ToCategorical();
        }
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        var y = data.Y2 ?? data.Y;
        foreach (var rows in MarkData.GroupBy(data.Count, data.Series ?? data.Stroke))
        {
            if (rows.Count is 0)
                continue;

            var color = GetStroke(context, data, rows[0]);
            var builder = new PathBuilder();
            var segments = PointSeries.Split(context, data.X, y, rows);
            foreach (var segment in segments)
            {
                CurveBuilder.Append(builder, segment.Select(point => point.Point).ToList(), Curve, closed: Closed && segments.Count is 1);
            }

            if (!builder.IsEmpty)
            {
                group.Add(Decorate(new ScenePath(builder.Build())
                {
                    Stroke = color,
                    StrokeWidth = StrokeWidth ?? context.Theme.LineWidth,
                    StrokeDashArray = StrokeDashArray,
                    LineJoin = SceneLineJoin.Round,
                    LineCap = SceneLineCap.Round,
                }, data, rows[0]));
            }

            if (MarkerRadius > 0)
            {
                foreach (var segment in segments)
                {
                    foreach (var (point, row) in segment)
                    {
                        group.Add(Decorate(new SceneCircle(point.X, point.Y, MarkerRadius) { Fill = color }, data, row));
                    }
                }
            }
        }
    }
}
