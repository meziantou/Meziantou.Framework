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
    /// is computed on the longitudes and latitudes, as on an equirectangular map. A ring with an edge that crosses the 180th
    /// meridian (see <see cref="CrossesAntimeridian"/>) and the positions on both sides of it are taken as they are on the
    /// globe, so the label does not move to the other side of the world. It is <see langword="null"/> for an empty geometry.
    /// </summary>
    public GeoPosition? LabelPosition => (_labelPosition ??= new StrongBox<GeoPosition?>(ComputeLabelPosition())).Value;

    /// <summary>Gets the positions of the geometry: every position of every point, line and ring, in order.</summary>
    /// <returns>The positions.</returns>
    public abstract IEnumerable<GeoPosition> GetPositions();

    /// <summary>
    /// Gets a value that indicates whether a line or a ring of the geometry has an edge that is meant to cross the 180th
    /// meridian: an edge whose ends are more than 180° of longitude apart, such as an edge from the longitude 179 to the
    /// longitude -179. The edges whose ends are both on the 180th meridian, such as the edges of a box around the whole
    /// world from -180 to 180, do not count.
    /// </summary>
    /// <remarks>
    /// RFC 7946 wants such geometries split along the 180th meridian: the operations of this class draw the edge from
    /// 179 to -179 the long way, through the Greenwich meridian. Split them with <see cref="SplitAtAntimeridian"/>, or
    /// use <see cref="GeoEdgeType.Geodesic"/> edges.
    /// </remarks>
    public bool CrossesAntimeridian => AntimeridianOperations.CrossesAntimeridian(this);

    /// <summary>Gets the smallest box that contains the geometry.</summary>
    /// <returns>The box, or <see langword="null"/> for an empty geometry.</returns>
    /// <remarks>The box contains the positions, as the edges are straight in degrees (<see cref="GeoEdgeType.Planar"/>): it does not cross the 180th meridian.</remarks>
    public GeoBounds? GetBounds() => GeoBounds.FromPositions(GetPositions());

    /// <summary>Gets the smallest box that contains the geometry, whose edges are drawn as an edge type says.</summary>
    /// <param name="edgeType">How the edges are drawn.</param>
    /// <returns>
    /// The box, or <see langword="null"/> for an empty geometry. With <see cref="GeoEdgeType.Planar"/>, the box of
    /// <see cref="GetBounds()"/>. With <see cref="GeoEdgeType.Geodesic"/>, the smallest box that contains the shortest
    /// great-circle arcs between the consecutive positions: it can cross the 180th meridian (its west side is east of its
    /// east side, as RFC 7946 allows), such as the box of a flight from Tokyo to San Francisco or of the parts of Fiji
    /// on both sides of the 180th meridian, and it goes past the latitudes of the positions where the arcs bend toward a
    /// pole. A ring that goes around a pole, such as the coast of Antarctica, gives a box that contains the pole and every longitude.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeType"/> is not a value of <see cref="GeoEdgeType"/>.</exception>
    public GeoBounds? GetBounds(GeoEdgeType edgeType) => edgeType switch
    {
        GeoEdgeType.Planar => GetBounds(),
        GeoEdgeType.Geodesic => SphericalGeometry.GetBounds([this]),
        _ => throw new ArgumentOutOfRangeException(nameof(edgeType), edgeType, message: null),
    };

    /// <summary>
    /// Splits the lines and the polygons of the geometry along the 180th meridian, as RFC 7946 wants it, and moves every
    /// longitude to [-180, 180]. The edges that cross the 180th meridian (see <see cref="CrossesAntimeridian"/>) take the
    /// shortest way, and the longitudes greater than 180 or smaller than -180 go around the world.
    /// </summary>
    /// <returns>
    /// <para>
    /// The geometry itself when no edge crosses the 180th meridian and every longitude is in [-180, 180]. Otherwise, a line
    /// that crosses it becomes a <see cref="GeoMultiLineString"/> whose parts end and start on it, at the longitudes 180
    /// and -180, and a polygon a <see cref="GeoMultiPolygon"/> with a polygon on each side. The points are moved, not split.
    /// </para>
    /// <para>
    /// The positions where a line crosses the meridian are interpolated as on an equirectangular map. A polygon is cut
    /// with <see cref="Clip(GeoBounds)"/>: the positions of a polygon that is cut move to the nearest ten-millionth of a
    /// degree and lose their altitude. A ring that goes around a pole, such as the coast of Antarctica, is closed through
    /// the pole it is nearest to.
    /// </para>
    /// </returns>
    /// <exception cref="InvalidOperationException">A coordinate of a polygon to split is not a number between -100,000 and 100,000.</exception>
    public GeoGeometry SplitAtAntimeridian() => AntimeridianOperations.Split(this);

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
    /// <exception cref="ArgumentException">The box is empty: its south side is north of its north side.</exception>
    /// <remarks>
    /// A box whose west side is east of its east side crosses the 180th meridian (see <see cref="GeoBounds.CrossesAntimeridian()"/>):
    /// the result is the part of the geometry east of its west side and the part west of its east side, as a multi-geometry.
    /// Split the geometries that cross the 180th meridian with <see cref="SplitAtAntimeridian"/> first.
    /// </remarks>
    public GeoGeometry? Clip(GeoBounds bounds) => bounds.CrossesAntimeridian() && bounds.South <= bounds.North ? AntimeridianOperations.ClipWrapped(this, bounds) : GeoOperations.Clip(this, bounds);

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
                    var (area, centroid) = GeoPolygon.GetAreaAndCentroid(AntimeridianOperations.UnwrapForCentroid(polygon.Rings[0]));
                    if (Math.Abs(area) > bestArea)
                    {
                        bestArea = Math.Abs(area);
                        best = centroid with { Longitude = AntimeridianOperations.NormalizeLongitude(centroid.Longitude) };
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
            return AntimeridianOperations.GetMean(linePositions);

        if (points.Count > 0)
            return AntimeridianOperations.GetMean(points);

        return null;
    }

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
