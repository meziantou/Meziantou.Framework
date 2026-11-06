namespace Meziantou.Framework.Charts;

/// <summary>
/// Draws a geometry per row on a map: its polygons are filled (a choropleth when the <see cref="Mark{T}.Fill"/> channel
/// is mapped by the color scale), its lines are stroked and its points are dots. The plot uses
/// <see cref="GeoCoordinates"/> by default when it has a geo mark.
/// </summary>
/// <typeparam name="T">The type of the rows, such as <see cref="GeoFeature"/>.</typeparam>
/// <remarks>
/// Without a fill, the polygons are light gray, a base map; a row whose fill value is missing is light gray too. The
/// polygons are filled with the even-odd rule, so their holes stay empty whatever the order of their rings. The mark
/// draws only in geographic coordinates.
/// </remarks>
public sealed class GeoMark<T> : Mark<T>
{
    // The geometry of a row is stored as a list of numbers: a code, then the positions
    private const double RingCode = 1;
    private const double LineCode = 2;
    private const double PointCode = 3;

    /// <summary>Initializes a new instance of the <see cref="GeoMark{T}"/> class.</summary>
    /// <param name="data">The rows.</param>
    /// <param name="geometry">The channel of the geometry of a row, such as <see cref="GeoFeature.Geometry"/>.</param>
    public GeoMark(IEnumerable<T> data, Func<T, GeoGeometry?> geometry)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        Geometry = geometry;
    }

    /// <summary>Gets the channel of the geometry of a row.</summary>
    public Func<T, GeoGeometry?> Geometry { get; }

    /// <summary>Gets or sets the radius of the dots of the points of the geometries, in pixels. The default is 3.</summary>
    public double PointRadius { get; set; } = 3;

    internal override MarkTraits GetTraits(MarkData data) => MarkTraits.PreferGeo;

    private protected override void PrepareChannels(IReadOnlyList<T> rows, MarkData data)
    {
        var sets = new IReadOnlyList<double>[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            sets[i] = Encode(Geometry(rows[i]));
        }

        data.Sets = sets;
    }

    internal override IEnumerable<GeoPosition> GetGeoPositions(MarkData data)
    {
        if (data.Sets is null)
            yield break;

        foreach (var set in data.Sets)
        {
            foreach (var (_, positions) in Decode(set))
            {
                foreach (var position in positions)
                {
                    yield return position;
                }
            }
        }
    }

    internal override void Render(RenderContext context, MarkData data, SceneGroup group)
    {
        if (data.Sets is null || context.Coordinates is not GeoSystem system)
            return;

        for (var i = 0; i < data.Count; i++)
        {
            var areas = new PathBuilder();
            var lines = new PathBuilder();
            var points = new List<GeoPosition>();
            foreach (var (code, positions) in Decode(data.Sets[i]))
            {
                switch (code)
                {
                    case RingCode:
                        system.AddPolyline(areas, positions, close: true);
                        break;
                    case LineCode:
                        system.AddPolyline(lines, positions, close: false);
                        break;
                    default:
                        points.AddRange(positions);
                        break;
                }
            }

            if (!areas.IsEmpty)
            {
                var fill = data.Fill is null ? FillColor ?? context.Theme.GridColor : data.Fill.IsMissing(i) ? context.Theme.GridColor : GetFill(context, data, i);
                group.Add(Decorate(new ScenePath(areas.Build())
                {
                    Fill = fill,
                    FillRule = SceneFillRule.EvenOdd,
                    Stroke = data.Stroke is null && StrokeColor is null ? context.Theme.MarkStrokeColor : GetStroke(context, data, i),
                    StrokeWidth = StrokeWidth ?? 0.5,
                    LineJoin = SceneLineJoin.Round,
                }, data, i));
            }

            if (!lines.IsEmpty)
            {
                group.Add(Decorate(new ScenePath(lines.Build())
                {
                    Stroke = GetStroke(context, data, i),
                    StrokeWidth = StrokeWidth ?? context.Theme.LineWidth,
                    LineJoin = SceneLineJoin.Round,
                    LineCap = SceneLineCap.Round,
                }, data, i));
            }

            foreach (var point in points)
            {
                var (x, y) = system.Project(point.Longitude, point.Latitude);
                if (!double.IsFinite(x) || !double.IsFinite(y))
                    continue;

                var circle = new SceneCircle(x, y, PointRadius) { Fill = GetFill(context, data, i) };
                if (data.Stroke is not null || StrokeColor is not null)
                {
                    circle.Stroke = GetStroke(context, data, i);
                    circle.StrokeWidth = StrokeWidth ?? 1;
                }

                group.Add(Decorate(circle, data, i));
            }
        }
    }

    private static double[] Encode(GeoGeometry? geometry)
    {
        if (geometry is null)
            return [];

        var values = new List<double>();
        void Add(double code, IReadOnlyList<GeoPosition> positions)
        {
            values.Add(code);
            values.Add(positions.Count);
            foreach (var position in positions)
            {
                values.Add(position.Longitude);
                values.Add(position.Latitude);
            }
        }

        foreach (var polygon in geometry.Polygons)
        {
            foreach (var ring in polygon.Rings)
            {
                Add(RingCode, ring);
            }
        }

        foreach (var line in geometry.Lines)
        {
            Add(LineCode, line);
        }

        if (geometry.Points.Count > 0)
        {
            Add(PointCode, geometry.Points);
        }

        return [.. values];
    }

    private static IEnumerable<(double Code, GeoPosition[] Positions)> Decode(IReadOnlyList<double>? values)
    {
        if (values is null)
            yield break;

        var index = 0;
        while (index + 1 < values.Count)
        {
            var code = values[index];
            var count = (int)values[index + 1];
            index += 2;
            var positions = new GeoPosition[count];
            for (var j = 0; j < count; j++)
            {
                positions[j] = new GeoPosition(values[index], values[index + 1]);
                index += 2;
            }

            yield return (code, positions);
        }
    }
}
