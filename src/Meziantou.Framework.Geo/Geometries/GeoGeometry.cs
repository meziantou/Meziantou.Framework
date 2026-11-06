using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Geo;

/// <summary>
/// A geometry, as defined by GeoJSON and the Simple Features of the OGC: a <see cref="GeoPoint"/>, a
/// <see cref="GeoMultiPoint"/>, a <see cref="GeoLineString"/>, a <see cref="GeoMultiLineString"/>, a
/// <see cref="GeoPolygon"/>, a <see cref="GeoMultiPolygon"/> or a <see cref="GeoGeometryCollection"/>.
/// </summary>
/// <remarks>The geometries are immutable.</remarks>
public abstract class GeoGeometry
{
    private StrongBox<GeoPosition?>? _labelPosition;

    private protected GeoGeometry()
    {
    }

    /// <summary>Gets the type of the geometry.</summary>
    public abstract GeoGeometryType Type { get; }

    /// <summary>Gets a value that indicates whether the geometry has no position.</summary>
    public bool IsEmpty => !GetPositions().Any();

    /// <summary>
    /// Gets a position to label the geometry: the centroid of the exterior ring of its largest polygon (so the islands of a
    /// country do not pull it away), else the mean of the positions of its lines, else the mean of its points. The centroid
    /// is computed on the longitudes and latitudes, as on an equirectangular map. It is <see langword="null"/> for an empty geometry.
    /// </summary>
    public GeoPosition? LabelPosition => (_labelPosition ??= new StrongBox<GeoPosition?>(ComputeLabelPosition())).Value;

    /// <summary>Gets the positions of the geometry: every position of every point, line and ring, in order.</summary>
    /// <returns>The positions.</returns>
    public abstract IEnumerable<GeoPosition> GetPositions();

    /// <summary>Gets the smallest box that contains the geometry.</summary>
    /// <returns>The box, or <see langword="null"/> for an empty geometry.</returns>
    public GeoBounds? GetBounds() => GeoBounds.FromPositions(GetPositions());

    /// <summary>Gets the simple geometries of the geometry: itself for a simple geometry, its parts for a multi-geometry or a collection, recursively.</summary>
    /// <returns>The points, line strings and polygons of the geometry.</returns>
    public IEnumerable<GeoGeometry> GetSimpleGeometries() => this switch
    {
        GeoMultiPoint multiPoint => multiPoint.Positions.Select(position => (GeoGeometry)new GeoPoint(position)),
        GeoMultiLineString multiLineString => multiLineString.LineStrings,
        GeoMultiPolygon multiPolygon => multiPolygon.Polygons,
        GeoGeometryCollection collection => collection.Geometries.SelectMany(geometry => geometry.GetSimpleGeometries()),
        _ => [this],
    };

    private GeoPosition? ComputeLabelPosition()
    {
        var bestArea = -1.0;
        GeoPosition? best = null;
        var linePositions = new List<GeoPosition>();
        var points = new List<GeoPosition>();
        foreach (var geometry in GetSimpleGeometries())
        {
            switch (geometry)
            {
                case GeoPolygon polygon when polygon.Rings.Count > 0 && polygon.Rings[0].Count > 0:
                    var (area, centroid) = GeoPolygon.GetAreaAndCentroid(polygon.Rings[0]);
                    if (Math.Abs(area) > bestArea)
                    {
                        bestArea = Math.Abs(area);
                        best = centroid;
                    }

                    break;

                case GeoLineString lineString:
                    linePositions.AddRange(lineString.Positions);
                    break;

                case GeoPoint point:
                    points.Add(point.Position);
                    break;
            }
        }

        if (best is not null)
            return best;

        if (linePositions.Count > 0)
            return Mean(linePositions);

        if (points.Count > 0)
            return Mean(points);

        return null;
    }

    private static GeoPosition Mean(List<GeoPosition> positions) => new(positions.Average(position => position.Longitude), positions.Average(position => position.Latitude));

    private protected static GeoPosition[] ToArray(IEnumerable<GeoPosition> positions, string paramName)
    {
        ArgumentNullException.ThrowIfNull(positions, paramName);
        return [.. positions];
    }

    private protected static T[] ToArray<T>(IEnumerable<T> items, string paramName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(items, paramName);
        T[] result = [.. items];
        foreach (var item in result)
        {
            if (item is null)
                throw new ArgumentException("The items must not be null.", paramName);
        }

        return result;
    }
}
