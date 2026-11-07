namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws the density contours of points: the two-dimensional density of the points, estimated with a Gaussian kernel on
/// a grid, is cut at round thresholds with marching squares, and the region above each threshold is filled (or outlined)
/// with the color of the threshold, mapped by the color scale (a sequential scale by default).
/// </summary>
/// <typeparam name="T">The type of the rows.</typeparam>
/// <remarks>The <see cref="Mark{T}.Fill"/> channel is not used: the thresholds give the colors.</remarks>
public sealed class ContourMark<T> : Mark<T>
{
    private int _gridSize = 64;
    private int _thresholds = 10;

    /// <summary>Initializes a new instance of the <see cref="ContourMark{T}"/> class.</summary>
    /// <param name="data">The rows.</param>
    /// <param name="x">The channel of the x position.</param>
    /// <param name="y">The channel of the y position.</param>
    public ContourMark(IEnumerable<T> data, Func<T, object?> x, Func<T, object?> y)
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

    /// <summary>Gets or sets the bandwidth of the kernel along x, in the unit of x, or <see langword="null"/> for Silverman's rule of thumb.</summary>
    public double? BandwidthX { get; set; }

    /// <summary>Gets or sets the bandwidth of the kernel along y, in the unit of y, or <see langword="null"/> for Silverman's rule of thumb.</summary>
    public double? BandwidthY { get; set; }

    /// <summary>Gets or sets the number of points of the grid of the density along each axis. The default is 64.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 2.</exception>
    public int GridSize
    {
        get => _gridSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 2);
            _gridSize = value;
        }
    }

    /// <summary>Gets or sets the approximate number of thresholds: the contours are at round values of the density. The default is 10.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int Thresholds
    {
        get => _thresholds;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _thresholds = value;
        }
    }

    /// <summary>Gets or sets a value that indicates whether the regions are filled; otherwise their outlines are drawn. The default is <see langword="true"/>.</summary>
    public bool Filled { get; set; } = true;

    /// <summary>Gets or sets the opacity of the fill, from 0 to 1. The default is 1.</summary>
    public double FillOpacity { get; set; } = 1;

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.X = Evaluate(rows, X);
        data.Y = Evaluate(rows, Y);
        data.Fill = null;
    }

    internal override MarkData Summarize(MarkData data)
    {
        if (data.X is not { IsContinuous: true } x || data.Y is not { IsContinuous: true } y)
            return new MarkData(0);

        var xs = new List<double>();
        var ys = new List<double>();
        for (var i = 0; i < data.Count; i++)
        {
            if (!double.IsNaN(x.Numbers[i]) && !double.IsNaN(y.Numbers[i]))
            {
                xs.Add(x.Numbers[i]);
                ys.Add(y.Numbers[i]);
            }
        }

        if (xs.Count is 0)
            return new MarkData(0);

        var bandwidthX = BandwidthX is > 0 ? BandwidthX.Value : KernelDensity.SilvermanBandwidth([.. xs.Order()]);
        var bandwidthY = BandwidthY is > 0 ? BandwidthY.Value : KernelDensity.SilvermanBandwidth([.. ys.Order()]);
        var (grid, x0, stepX, y0, stepY) = KernelDensity.Evaluate2D([.. xs], [.. ys], bandwidthX, bandwidthY, GridSize);
        var maximum = grid.Max();
        var thresholds = TickGenerator.Ticks(0, maximum, Thresholds).Where(value => value > 0 && value <= maximum).ToArray();

        // A row per threshold: its rings, in the unit of the data, separated by NaN
        var rings = new List<IReadOnlyList<double>>();
        foreach (var threshold in thresholds)
        {
            var points = new List<double>();
            foreach (var polygon in Contours.Compute(grid, GridSize, GridSize, threshold))
            {
                foreach (var ring in polygon)
                {
                    if (points.Count > 0)
                    {
                        points.Add(double.NaN);
                        points.Add(double.NaN);
                    }

                    foreach (var (gx, gy) in ring)
                    {
                        points.Add(x0 + ((gx - 0.5) * stepX));
                        points.Add(y0 + ((gy - 0.5) * stepY));
                    }
                }
            }

            rings.Add(points);
        }

        var result = data.Select([.. Enumerable.Repeat(0, thresholds.Length)]);
        result.X = null;
        result.Y = null;
        result.Fill = ChannelValues.FromNumbers(thresholds);
        result.Sets = [.. rings];
        result.Titles = data.Titles is null ? [.. thresholds.Select(threshold => "≥ " + NumberFormats.Default(threshold))] : result.Titles;
        result.ExtraX.Add(ChannelValues.FromNumbers([x0, x0 + ((GridSize - 1) * stepX)], x.Kind));
        result.ExtraY.Add(ChannelValues.FromNumbers([y0, y0 + ((GridSize - 1) * stepY)], y.Kind));
        return result;
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        if (data.Sets is null || context.X is not ResolvedContinuousScale xScale || context.Y is not ResolvedContinuousScale yScale)
            return;

        for (var i = 0; i < data.Count; i++)
        {
            var builder = new PathBuilder();
            var points = data.Sets[i];
            var start = true;
            for (var j = 0; j + 1 < points.Count; j += 2)
            {
                if (double.IsNaN(points[j]))
                {
                    builder.Close();
                    start = true;
                    continue;
                }

                var (px, py) = context.Coordinates.Project(xScale.MapNumber(points[j]), yScale.MapNumber(points[j + 1]));
                if (start)
                {
                    builder.MoveTo(px, py);
                    start = false;
                }
                else
                {
                    builder.LineTo(px, py);
                }
            }

            if (builder.IsEmpty)
                continue;

            builder.Close();
            var color = context.GetColor(data.Fill, i, FillColor);
            var path = new ScenePath(builder.Build()) { LineJoin = SceneLineJoin.Round };
            if (Filled)
            {
                path.Fill = FillOpacity < 1 ? color.WithOpacity(color.Opacity * FillOpacity) : color;
                if (data.Stroke is not null || StrokeColor is not null)
                {
                    path.Stroke = GetStroke(context, data, i);
                    path.StrokeWidth = StrokeWidth ?? 0.5;
                }
            }
            else
            {
                path.Stroke = data.Stroke is null && StrokeColor is null ? color : GetStroke(context, data, i);
                path.StrokeWidth = StrokeWidth ?? 1;
            }

            group.Add(Decorate(path, data, i));
        }
    }
}
