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
    public static GeoSystem Fit(GeoProjection projection, (double MinX, double MinY, double MaxX, double MaxY) bounds, SceneBounds frame)
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
        var straight = longitude1 == longitude2 ? projection.HasStraightMeridians : latitude1 == latitude2 && projection.HasStraightParallels;
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

    /// <summary>
    /// Adds a path through projected positions; the positions that do not project are skipped. A segment along a parallel
    /// or a meridian that the projection curves gets intermediate points, such as the edge of a shape cut to a box on the
    /// map of a pole.
    /// </summary>
    public void AddPolyline(PathBuilder builder, IReadOnlyList<GeoPosition> positions, bool close)
    {
        var started = false;
        for (var i = 0; i < positions.Count; i++)
        {
            if (i > 0)
            {
                AddIntermediatePoints(builder, positions[i - 1], positions[i], ref started);
            }

            AddPoint(builder, positions[i], ref started);
        }

        if (started && close)
        {
            if (positions.Count > 1 && positions[^1] != positions[0])
            {
                AddIntermediatePoints(builder, positions[^1], positions[0], ref started);
            }

            builder.Close();
        }
    }

    /// <summary>
    /// Gets a value that indicates whether two meridians are drawn as the same line, such as the meridians 180°W and 180°E
    /// on the map of a pole: the shapes cut along one of them, as the data cuts the shapes that cross the antimeridian,
    /// have an edge there that is not an edge of the region.
    /// </summary>
    public bool JoinsMeridians(double longitude1, double longitude2)
    {
        foreach (var latitude in (ReadOnlySpan<double>)[-45, 0, 45])
        {
            var (x1, y1) = Projection.Project(longitude1, latitude);
            var (x2, y2) = Projection.Project(longitude2, latitude);
            if (!(Math.Abs(x1 - x2) < 1e-9 && Math.Abs(y1 - y2) < 1e-9))
                return false;
        }

        return true;
    }

    /// <summary>Gets a value that indicates whether a ring has an edge along one of two meridians.</summary>
    public static bool HasEdgeAlong(IReadOnlyList<GeoPosition> positions, double longitude1, double longitude2)
    {
        for (var i = 0; i < positions.Count; i++)
        {
            if (IsEdgeAlong(positions[i], positions[(i + 1) % positions.Count], longitude1, longitude2))
                return true;
        }

        return false;
    }

    /// <summary>Adds the outline of a ring, without its edges along two meridians that the map joins: the outline is open where they are.</summary>
    public void AddRingOutline(PathBuilder builder, IReadOnlyList<GeoPosition> positions, double longitude1, double longitude2)
    {
        var count = positions.Count;
        var edges = count > 1 && positions[^1] == positions[0] ? count - 1 : count;
        var started = false;
        for (var i = 0; i < edges; i++)
        {
            var from = positions[i];
            var to = positions[(i + 1) % count];
            if (IsEdgeAlong(from, to, longitude1, longitude2))
            {
                started = false;
                continue;
            }

            if (!started)
            {
                AddPoint(builder, from, ref started);
            }

            AddIntermediatePoints(builder, from, to, ref started);
            AddPoint(builder, to, ref started);
        }
    }

    private static bool IsEdgeAlong(GeoPosition from, GeoPosition to, double longitude1, double longitude2) =>
        from.Longitude == to.Longitude && from.Latitude != to.Latitude && (from.Longitude == longitude1 || from.Longitude == longitude2);

    private void AddIntermediatePoints(PathBuilder builder, GeoPosition from, GeoPosition to, ref bool started)
    {
        var alongParallel = from.Latitude == to.Latitude && from.Longitude != to.Longitude && !Projection.HasStraightParallels;
        var alongMeridian = from.Longitude == to.Longitude && from.Latitude != to.Latitude && !Projection.HasStraightMeridians;
        if (!alongParallel && !alongMeridian)
            return;

        var steps = (int)Math.Ceiling(Math.Max(Math.Abs(to.Longitude - from.Longitude), Math.Abs(to.Latitude - from.Latitude)) / Precision);
        for (var i = 1; i < steps; i++)
        {
            var t = (double)i / steps;
            AddPoint(builder, new GeoPosition(from.Longitude + ((to.Longitude - from.Longitude) * t), from.Latitude + ((to.Latitude - from.Latitude) * t)), ref started);
        }
    }

    private void AddPoint(PathBuilder builder, GeoPosition position, ref bool started)
    {
        var (x, y) = Project(position.Longitude, position.Latitude);
        if (!double.IsFinite(x) || !double.IsFinite(y))
            return;

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
}
