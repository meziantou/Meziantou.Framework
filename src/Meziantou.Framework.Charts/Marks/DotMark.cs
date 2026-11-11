namespace Meziantou.Framework.Charts;

/// <summary>Draws a dot per row at its x and y positions: a scatter plot, or a bubble chart with the <see cref="Size"/> channel.</summary>
/// <typeparam name="T">The type of the rows.</typeparam>
public sealed class DotMark<T> : Mark<T>
{
    /// <summary>Initializes a new instance of the <see cref="DotMark{T}"/> class.</summary>
    /// <param name="data">The rows.</param>
    /// <param name="x">The channel of the x position.</param>
    /// <param name="y">The channel of the y position.</param>
    public DotMark(IEnumerable<T> data, Func<T, object?> x, Func<T, object?> y)
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

    /// <summary>Gets or sets the channel of the size, mapped by the size scale so the area of the dot is proportional to the value.</summary>
    public Func<T, object?>? Size { get; set; }

    /// <summary>Gets or sets the radius of the dots when <see cref="Size"/> is not set. The default is 3.5.</summary>
    public double Radius { get; set; } = 3.5;

    /// <summary>Gets or sets the shape of the dots. The default is <see cref="DotSymbol.Circle"/>.</summary>
    public DotSymbol Symbol { get; set; }

    /// <summary>Gets or sets the opacity of the fill, from 0 to 1. The default is 1.</summary>
    public double FillOpacity { get; set; } = 1;

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        data.X = Evaluate(rows, X);
        data.Y = Evaluate(rows, Y);
        data.Size = Evaluate(rows, Size);
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        var order = Enumerable.Range(0, data.Count);
        if (data.Size is not null)
        {
            // The large bubbles first, so they do not hide the small ones
            order = order.OrderByDescending(i => data.Size.GetNumber(i) is var value && double.IsNaN(value) ? 0 : value).ThenBy(i => i);
        }

        foreach (var i in order)
        {
            var x = PointSeries.Center(context.X, context.MapX(data.X, i));
            var y = PointSeries.Center(context.Y, context.MapY(data.Y, i));
            if (double.IsNaN(x) || double.IsNaN(y))
                continue;

            var radius = data.Size is null || context.Size is null ? Radius : context.Size.Map(data.Size.GetNumber(i));
            if (radius <= 0)
                continue;

            var (px, py) = context.Coordinates.Project(x, y);
            var shape = CreateSymbol(Symbol, px, py, radius);
            var fill = GetFill(context, data, i);
            shape.Fill = FillOpacity < 1 ? fill.WithOpacity(fill.Opacity * FillOpacity) : fill;
            if (data.Stroke is not null || StrokeColor is not null)
            {
                shape.Stroke = GetStroke(context, data, i);
                shape.StrokeWidth = StrokeWidth ?? 1;
            }

            group.Add(Decorate(shape, data, i));
        }
    }

    internal static SceneShape CreateSymbol(DotSymbol symbol, double x, double y, double radius)
    {
        switch (symbol)
        {
            case DotSymbol.Square:
                {
                    var half = radius * 0.886;
                    return new SceneRectangle(x - half, y - half, 2 * half, 2 * half);
                }

            case DotSymbol.Triangle:
                {
                    var size = radius * 1.347;
                    return new ScenePath(new PathBuilder().AddPolygon((x, y - size), (x + (size * 0.866), y + (size / 2)), (x - (size * 0.866), y + (size / 2))).Build());
                }

            case DotSymbol.Diamond:
                {
                    var size = radius * 1.253;
                    return new ScenePath(new PathBuilder().AddPolygon((x, y - size), (x + size, y), (x, y + size), (x - size, y)).Build());
                }

            case DotSymbol.Cross:
                {
                    var arm = radius * 1.1;
                    var half = radius * 0.4;
                    return new ScenePath(new PathBuilder().AddPolygon(
                        (x - half, y - arm), (x + half, y - arm), (x + half, y - half), (x + arm, y - half),
                        (x + arm, y + half), (x + half, y + half), (x + half, y + arm), (x - half, y + arm),
                        (x - half, y + half), (x - arm, y + half), (x - arm, y - half), (x - half, y - half)).Build());
                }

            default:
                return new SceneCircle(x, y, radius);
        }
    }
}
