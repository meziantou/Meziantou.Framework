namespace Meziantou.Framework.Charts;

/// <summary>Geographic coordinates: a projection, scaled and translated to fit the plot area. The x and y positions are longitudes and latitudes.</summary>
internal sealed class GeoSystem : CoordinateSystem
{
    // The largest step between two intermediate points of a curved meridian or parallel, in degrees, as in d3-geo
    private const double Precision = 2.5;

    private readonly double _scale;
    private readonly double _translateX;
    private readonly double _translateY;

    public GeoSystem(GeoProjection projection, double scale, double translateX, double translateY)
        : base(transposed: false)
    {
        Projection = projection;
        _scale = scale;
        _translateX = translateX;
        _translateY = translateY;
    }

    public GeoProjection Projection { get; }

    public override bool IsPolar => false;

    /// <summary>Scales and centers the projected bounds, in radii of the Earth, to fit a frame, as d3's <c>fitExtent</c>.</summary>
    public static GeoSystem Fit(GeoProjection projection, (double MinX, double MinY, double MaxX, double MaxY) bounds, ChartRectangle frame)
    {
        var width = bounds.MaxX - bounds.MinX;
        var height = bounds.MaxY - bounds.MinY;
        double scale;
        if (width > 0 && height > 0)
        {
            scale = Math.Min(frame.Width / width, frame.Height / height);
        }
        else if (width > 0 || height > 0)
        {
            scale = (width > 0 ? frame.Width : frame.Height) / Math.Max(width, height);
        }
        else
        {
            // A single point: a region of 10 degrees around it
            scale = Math.Min(frame.Width, frame.Height) / (Math.PI / 18);
        }

        var translateX = frame.CenterX - (scale * (bounds.MinX + bounds.MaxX) / 2);
        var translateY = frame.CenterY + (scale * (bounds.MinY + bounds.MaxY) / 2);
        return new GeoSystem(projection, scale, translateX, translateY);
    }

    /// <summary>The bounds of the projected positions, in radii of the Earth, or NaN when no position is finite.</summary>
    public static (double MinX, double MinY, double MaxX, double MaxY) GetProjectedBounds(GeoProjection projection, IEnumerable<GeoPosition> positions)
    {
        var (minX, minY, maxX, maxY) = (double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);
        foreach (var position in positions)
        {
            var (x, y) = projection.Project(position.Longitude, position.Latitude);
            if (!double.IsFinite(x) || !double.IsFinite(y))
                continue;

            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        return minX > maxX ? (double.NaN, double.NaN, double.NaN, double.NaN) : (minX, minY, maxX, maxY);
    }

    /// <summary>The positions along the edges of a box of longitudes and latitudes, with intermediate points where the projection curves them.</summary>
    public static List<GeoPosition> SampleBox(GeoProjection projection, double west, double south, double east, double north)
    {
        var positions = new List<GeoPosition>();
        AddSegment(projection, positions, west, south, east, south);
        AddSegment(projection, positions, east, south, east, north);
        AddSegment(projection, positions, east, north, west, north);
        AddSegment(projection, positions, west, north, west, south);
        return positions;
    }

    /// <summary>Adds the positions of a meridian or a parallel segment, from its start to its end, with intermediate points where the projection curves it.</summary>
    public static void AddSegment(GeoProjection projection, List<GeoPosition> positions, double longitude1, double latitude1, double longitude2, double latitude2)
    {
        var straight = longitude1 == longitude2 ? projection.StraightMeridians : latitude1 == latitude2 && projection.StraightParallels;
        var steps = straight ? 1 : Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(longitude2 - longitude1), Math.Abs(latitude2 - latitude1)) / Precision));
        for (var i = positions.Count > 0 && positions[^1] == new GeoPosition(longitude1, latitude1) ? 1 : 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            positions.Add(new GeoPosition(longitude1 + ((longitude2 - longitude1) * t), latitude1 + ((latitude2 - latitude1) * t)));
        }
    }

    public override (double X, double Y) Project(double x, double y)
    {
        var (px, py) = Projection.Project(x, y);
        return (_translateX + (_scale * px), _translateY - (_scale * py));
    }

    public override SceneShape Rectangle(double x1, double x2, double y1, double y2) => new ScenePath(Polyline(SampleBox(Projection, x1, y1, x2, y2), close: true));

    /// <summary>The path through projected positions.</summary>
    public PathData Polyline(IReadOnlyList<GeoPosition> positions, bool close)
    {
        var builder = new PathBuilder();
        AddPolyline(builder, positions, close);
        return builder.Build();
    }

    /// <summary>Adds a path through projected positions; the positions that do not project are skipped.</summary>
    public void AddPolyline(PathBuilder builder, IReadOnlyList<GeoPosition> positions, bool close)
    {
        var started = false;
        foreach (var position in positions)
        {
            var (x, y) = Project(position.Longitude, position.Latitude);
            if (!double.IsFinite(x) || !double.IsFinite(y))
                continue;

            if (started)
            {
                builder.LineTo(x, y);
            }
            else
            {
                builder.MoveTo(x, y);
                started = true;
            }
        }

        if (started && close)
        {
            builder.Close();
        }
    }
}
