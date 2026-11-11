namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a hexbin plot: the plot area is tiled with hexagons, and each hexagon that has points is colored by the number
/// of its points with the color scale (a sequential scale by default). It shows the density of a scatter plot with too
/// many points to draw each one.
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
/// <remarks>
/// The hexagons are regular in pixels, so the points are binned once the scales are laid out: the domain of the color
/// scale includes the counts, unless the scale sets its minimum and its maximum. The <see cref="Mark{T}.Fill"/> channel
/// is not used.
/// </remarks>
public sealed class HexbinMark<T> : Mark<T>
{
    private double _radius = 10;

    /// <summary>Initializes a new instance of the <see cref="HexbinMark{T}"/> class.</summary>
    /// <param name="data">The rows.</param>
    /// <param name="x">The channel of the x position.</param>
    /// <param name="y">The channel of the y position.</param>
    public HexbinMark(IEnumerable<T> data, Func<T, object?> x, Func<T, object?> y)
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

    /// <summary>Gets or sets the radius of the hexagons, from their center to a corner, in pixels. The default is 10.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    public double Radius
    {
        get => _radius;
        set
        {
            if (!(value > 0) || !double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The radius must be a positive finite number.");

            _radius = value;
        }
    }

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.X = Evaluate(rows, X);
        data.Y = Evaluate(rows, Y);
        data.Fill = null;

        // A count of 1 makes the color scale continuous; the counts of the hexagons extend it once they are known
        data.ExtraColor.Add(ChannelValues.FromNumbers([1]));
    }

    internal override IEnumerable<double> GetLateColorValues(RenderContext context, MarkData data) => Bin(context, data).Select(bin => (double)bin.Points.Count);

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        var stroke = context.Theme.Background ?? SceneColor.White;
        foreach (var (x, y, points) in Bin(context, data))
        {
            var corners = Hexbin.Hexagon(context.Frame.X + x, context.Frame.Y + y, Radius);

            var color = context.Color is ResolvedContinuousColorScale scale ? scale.MapNumber(points.Count) : FillColor ?? context.DefaultColor;
            group.Add(new ScenePath(new PathBuilder().AddPolygon(corners).Build())
            {
                Fill = color,
                Stroke = data.Stroke is null && StrokeColor is null ? stroke : GetStroke(context, data, points[0]),
                StrokeWidth = StrokeWidth ?? 0.5,
                LineJoin = SceneLineJoin.Round,
                Tooltip = data.Titles?[points[0]] ?? NumberFormats.Default(points.Count),
            });
        }
    }

    /// <summary>The hexagons, with their centers relative to the top left corner of the plot area, so the bins of the charts of a facet grid match.</summary>
    private List<(double X, double Y, List<int> Points)> Bin(RenderContext context, MarkData data)
    {
        var points = new (double X, double Y)[data.Count];
        for (var i = 0; i < data.Count; i++)
        {
            var (x, y) = context.Coordinates.Project(PointSeries.Center(context.X, context.MapX(data.X, i)), PointSeries.Center(context.Y, context.MapY(data.Y, i)));
            points[i] = (x - context.Frame.X, y - context.Frame.Y);
        }

        return Hexbin.Compute(points, Radius);
    }
}
