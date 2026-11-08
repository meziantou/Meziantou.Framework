using System.Runtime.CompilerServices;
using Meziantou.Framework.Geo.Internals;

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

    /// <summary>
    /// Repairs the polygons of the geometry: a ring that crosses itself or another ring of its polygon, a hole outside its
    /// exterior ring, rings in the wrong direction or repeated positions. Each polygon becomes the area its rings enclose
    /// an odd number of times (a bow-tie becomes two triangles), and a multi-polygon the union of the areas of its polygons.
    /// The exterior rings of the result turn counter-clockwise and its holes clockwise, as RFC 7946 wants them.
    /// </summary>
    /// <returns>
    /// The repaired geometry: a polygon becomes a <see cref="GeoPolygon"/>, or a <see cref="GeoMultiPolygon"/> when it is
    /// cut in several parts. The positions of the polygons move to the nearest ten-millionth of a degree (about a centimeter),
    /// and lose their altitude, measure and time. The consecutive repeated positions of the lines (same coordinates and
    /// altitude) are removed, the first one being kept; the points are unchanged.
    /// </returns>
    /// <exception cref="InvalidOperationException">A coordinate of a polygon is not a number between -100,000 and 100,000.</exception>
    /// <remarks>The computation is exact on the grid of ten-millionths of a degree, as on an equirectangular map: the edges are straight in degrees.</remarks>
    public GeoGeometry MakeValid() => GeoOperations.MakeValid(this);

    /// <summary>Cuts the geometry to a box: the parts of the points, lines and polygons that are inside it.</summary>
    /// <param name="bounds">The box.</param>
    /// <returns>
    /// The part of the geometry that is inside the box, or <see langword="null"/> when nothing is. The geometry itself when
    /// it is entirely inside. A line that leaves the box and comes back becomes a <see cref="GeoMultiLineString"/>. The
    /// polygons are cut as by <see cref="Intersection"/>. Where a line crosses the side of the box, the new position gets
    /// the altitude, the measure and the time interpolated between the positions around it, when both have one.
    /// </returns>
    /// <exception cref="ArgumentException">The box is empty: its west side is east of its east side, or its south side north of its north side.</exception>
    public GeoGeometry? Clip(GeoBounds bounds) => GeoOperations.Clip(this, bounds);

    /// <summary>
    /// Simplifies the lines and the rings of the geometry with the Douglas–Peucker algorithm: the positions that are closer
    /// than the tolerance to the line between the positions kept around them are removed. The simplified polygons are
    /// repaired with <see cref="MakeValid"/>, since removing positions can make a ring cross itself or another one.
    /// </summary>
    /// <param name="tolerance">The largest distance between the geometry and its simplification, in degrees: 0.001° is about 100 meters.</param>
    /// <returns>The simplified geometry, or <see langword="null"/> when every line and ring collapses. The points are unchanged.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is negative, infinite or not a number.</exception>
    public GeoGeometry? Simplify(double tolerance) => GeoOperations.Simplify(this, tolerance);

    /// <summary>Gets the union of the area of this geometry and of the area of another one.</summary>
    /// <param name="other">The other geometry: a polygon, a multi-polygon or a collection of polygons.</param>
    /// <returns>The union, or <see langword="null"/> when it is empty.</returns>
    /// <exception cref="InvalidOperationException">This geometry is not a polygon, a multi-polygon or a collection of polygons.</exception>
    /// <exception cref="ArgumentException"><paramref name="other"/> is not a polygon, a multi-polygon or a collection of polygons.</exception>
    /// <remarks>
    /// The exterior ring of each polygon adds its area, and its holes remove theirs, whatever their direction: call
    /// <see cref="MakeValid"/> first on polygons whose rings cross. The result is a <see cref="GeoPolygon"/> or a
    /// <see cref="GeoMultiPolygon"/> whose positions are on the grid of ten-millionths of a degree.
    /// </remarks>
    public GeoGeometry? Union(GeoGeometry other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return GeoOperations.Combine(this, other, PolygonCombineMode.Union, nameof(other));
    }

    /// <summary>Gets the area that this geometry and another one have in common.</summary>
    /// <param name="other">The other geometry: a polygon, a multi-polygon or a collection of polygons.</param>
    /// <returns>The intersection, or <see langword="null"/> when it is empty.</returns>
    /// <exception cref="InvalidOperationException">This geometry is not a polygon, a multi-polygon or a collection of polygons.</exception>
    /// <exception cref="ArgumentException"><paramref name="other"/> is not a polygon, a multi-polygon or a collection of polygons.</exception>
    /// <remarks>See <see cref="Union"/> for how the rings are read and what the result is made of.</remarks>
    public GeoGeometry? Intersection(GeoGeometry other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return GeoOperations.Combine(this, other, PolygonCombineMode.Intersect, nameof(other));
    }

    /// <summary>Gets the area of this geometry that is outside another one.</summary>
    /// <param name="other">The other geometry: a polygon, a multi-polygon or a collection of polygons.</param>
    /// <returns>The difference, or <see langword="null"/> when it is empty.</returns>
    /// <exception cref="InvalidOperationException">This geometry is not a polygon, a multi-polygon or a collection of polygons.</exception>
    /// <exception cref="ArgumentException"><paramref name="other"/> is not a polygon, a multi-polygon or a collection of polygons.</exception>
    /// <remarks>See <see cref="Union"/> for how the rings are read and what the result is made of.</remarks>
    public GeoGeometry? Difference(GeoGeometry other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return GeoOperations.Combine(this, other, PolygonCombineMode.Exclude, nameof(other));
    }

    /// <summary>Gets a geometry of the same type whose positions are transformed, such as positions in another coordinate system converted to longitudes and latitudes.</summary>
    /// <param name="transform">The function that transforms each position. Return <c>position with { Longitude = ..., Latitude = ... }</c> to keep its altitude, measure and time.</param>
    /// <returns>The transformed geometry.</returns>
    public GeoGeometry Transform(Func<GeoPosition, GeoPosition> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        return GeoOperations.Transform(this, transform);
    }

    /// <summary>Gets the rings of the polygons of the geometry as closed lines, such as the coastlines of land polygons.</summary>
    /// <returns>A line for each ring of each polygon, exterior rings and holes, in order. Nothing for points and lines.</returns>
    public IEnumerable<GeoLineString> GetBoundaryLines() => GeoOperations.GetBoundaryLines(this);

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
