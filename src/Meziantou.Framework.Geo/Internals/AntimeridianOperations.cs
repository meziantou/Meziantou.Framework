using System.Runtime.InteropServices;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>The geometries that cross the 180th meridian: detection, split, clip to a box that crosses it, and labels.</summary>
internal static class AntimeridianOperations
{
    /// <summary>
    /// Gets a value that indicates whether an edge is meant to cross the 180th meridian: its ends are more than 180° of
    /// longitude apart, and they are not both on the 180th meridian, as the edges of a box around the whole world are.
    /// </summary>
    public static bool IsCrossingEdge(GeoPosition a, GeoPosition b) => Math.Abs(b.Longitude - a.Longitude) > 180 && !(Math.Abs(a.Longitude) == 180 && Math.Abs(b.Longitude) == 180);

    public static bool CrossesAntimeridian(GeoGeometry geometry) => geometry.GetSimpleGeometries().Any(simple => simple switch
    {
        GeoLineString lineString => HasCrossingEdge(lineString.Positions, closed: false),
        GeoPolygon polygon => polygon.Rings.Any(ring => HasCrossingEdge(ring, closed: true)),
        _ => false,
    });

    public static GeoGeometry Split(GeoGeometry geometry)
    {
        switch (geometry)
        {
            case GeoPoint point:
                return IsInRange(point.Position) ? point : new GeoPoint(Normalize(point.Position));

            case GeoMultiPoint multiPoint:
                return multiPoint.Positions.All(IsInRange) ? multiPoint : new GeoMultiPoint(multiPoint.Positions.Select(Normalize));

            case GeoLineString lineString:
                if (!NeedsSplit(lineString.Positions, closed: false))
                    return lineString;

                var pieces = SplitLine(lineString.Positions);
                return pieces.Count == 1 ? new GeoLineString(pieces[0]) : new GeoMultiLineString(pieces.Select(piece => new GeoLineString(piece)));

            case GeoMultiLineString multiLineString:
                if (!multiLineString.LineStrings.Any(lineString => NeedsSplit(lineString.Positions, closed: false)))
                    return multiLineString;

                return new GeoMultiLineString(multiLineString.LineStrings.SelectMany(lineString => SplitLine(lineString.Positions)).Select(piece => new GeoLineString(piece)));

            case GeoPolygon polygon:
                if (!NeedsSplit(polygon))
                    return polygon;

                var polygons = SplitPolygon(polygon);
                return polygons.Count == 1 ? polygons[0] : new GeoMultiPolygon(polygons);

            case GeoMultiPolygon multiPolygon:
                if (!multiPolygon.Polygons.Any(NeedsSplit))
                    return multiPolygon;

                return new GeoMultiPolygon(multiPolygon.Polygons.SelectMany(polygon => NeedsSplit(polygon) ? SplitPolygon(polygon) : (IEnumerable<GeoPolygon>)[polygon]));

            case GeoGeometryCollection collection:
                var parts = collection.Geometries.Select(Split).ToArray();
                return parts.SequenceEqual(collection.Geometries) ? collection : new GeoGeometryCollection(parts);

            default:
                return geometry;
        }
    }

    /// <summary>Cuts a geometry to a box that crosses the 180th meridian: the part east of its west side, and the part west of its east side.</summary>
    public static GeoGeometry? ClipWrapped(GeoGeometry geometry, GeoBounds bounds)
    {
        var east = GeoOperations.Clip(geometry, bounds with { East = 180 });
        var west = GeoOperations.Clip(geometry, bounds with { West = -180 });
        if (east is null)
            return west;

        if (west is null)
            return east;

        GeoGeometry[] simples = [.. east.GetSimpleGeometries(), .. west.GetSimpleGeometries()];
        if (simples.All(simple => simple is GeoPoint))
            return new GeoMultiPoint(simples.Select(simple => ((GeoPoint)simple).Position));

        if (simples.All(simple => simple is GeoLineString))
            return new GeoMultiLineString(simples.Cast<GeoLineString>());

        if (simples.All(simple => simple is GeoPolygon))
            return new GeoMultiPolygon(simples.Cast<GeoPolygon>());

        return new GeoGeometryCollection([east, west]);
    }

    /// <summary>Gets the ring with longitudes that do not jump at the edges that cross the 180th meridian, for its area and centroid.</summary>
    public static IReadOnlyList<GeoPosition> UnwrapForCentroid(IReadOnlyList<GeoPosition> ring) => HasCrossingEdge(ring, closed: true) ? Unwrap(ring) : ring;

    /// <summary>Gets the mean of positions, each one taken on the shortest way from the previous one, so that positions on both sides of the 180th meridian do not average to the other side of the world.</summary>
    public static GeoPosition GetMean(List<GeoPosition> positions)
    {
        var offset = 0.0;
        var longitudes = 0.0;
        var latitudes = 0.0;
        for (var i = 0; i < positions.Count; i++)
        {
            var delta = i > 0 ? positions[i].Longitude - positions[i - 1].Longitude : 0;
            if (Math.Abs(delta) > 180)
            {
                offset -= 360 * Math.Round(delta / 360, MidpointRounding.AwayFromZero);
            }

            longitudes += positions[i].Longitude + offset;
            latitudes += positions[i].Latitude;
        }

        return new GeoPosition(NormalizeLongitude(longitudes / positions.Count), latitudes / positions.Count);
    }

    public static double NormalizeLongitude(double longitude)
    {
        if (longitude is >= -180 and <= 180)
            return longitude;

        return longitude - (360 * Math.Floor((longitude + 180) / 360));
    }

    private static bool HasCrossingEdge(IReadOnlyList<GeoPosition> positions, bool closed)
    {
        for (var i = 1; i < positions.Count; i++)
        {
            if (IsCrossingEdge(positions[i - 1], positions[i]))
                return true;
        }

        return closed && positions.Count > 1 && IsCrossingEdge(positions[^1], positions[0]);
    }

    private static bool IsInRange(GeoPosition position) => position.Longitude is >= -180 and <= 180;

    private static GeoPosition Normalize(GeoPosition position) => IsInRange(position) ? position : position with { Longitude = NormalizeLongitude(position.Longitude) };

    private static bool NeedsSplit(IReadOnlyList<GeoPosition> positions, bool closed) => HasCrossingEdge(positions, closed) || !positions.All(IsInRange);

    private static bool NeedsSplit(GeoPolygon polygon) => polygon.Rings.Any(ring => NeedsSplit(ring, closed: true));

    /// <summary>Gets the positions with longitudes that take the shortest way at each edge that crosses the 180th meridian, so they go past ±180 instead of jumping.</summary>
    private static GeoPosition[] Unwrap(IReadOnlyList<GeoPosition> positions) => [.. GetUnwrappedPositions(positions).Select(position => position.Shift(0))];

    private static UnwrappedPosition[] GetUnwrappedPositions(IReadOnlyList<GeoPosition> positions)
    {
        var result = new UnwrappedPosition[positions.Count];
        var offset = 0.0;
        for (var i = 0; i < positions.Count; i++)
        {
            if (i > 0 && IsCrossingEdge(positions[i - 1], positions[i]))
            {
                offset -= 360 * Math.Round((positions[i].Longitude - positions[i - 1].Longitude) / 360, MidpointRounding.AwayFromZero);
            }

            result[i] = new UnwrappedPosition(positions[i], offset);
        }

        return result;
    }

    /// <summary>The window of longitudes [360k - 180, 360k + 180] that contains a longitude: 0 for [-180, 180).</summary>
    private static int GetWindow(double longitude) => (int)Math.Floor((longitude + 180) / 360);

    private static List<GeoPosition[]> SplitLine(IReadOnlyList<GeoPosition> positions)
    {
        if (positions.Count < 2)
            return [[.. positions.Select(Normalize)]];

        var unwrapped = GetUnwrappedPositions(positions);
        var pieces = new List<GeoPosition[]>();
        List<GeoPosition>? current = null;
        var currentWindow = 0;
        for (var i = 1; i < unwrapped.Length; i++)
        {
            var a = unwrapped[i - 1];
            var b = unwrapped[i];

            // The meridian ±180 strictly between the ends, if any: an unwrapped edge spans at most 180°, so it crosses at most one
            var low = Math.Min(a.Longitude, b.Longitude);
            var high = Math.Max(a.Longitude, b.Longitude);
            var meridian = 180 + (360 * Math.Ceiling((low - 180) / 360));
            if (meridian > low && meridian < high)
            {
                var t = (meridian - a.Longitude) / (b.Longitude - a.Longitude);
                var (originalA, originalB) = (a.Original, b.Original);
                var crossing = new UnwrappedPosition(
                    originalA with
                    {
                        Longitude = meridian,
                        Latitude = originalA.Latitude + (t * (originalB.Latitude - originalA.Latitude)),
                        Altitude = originalA.Altitude is { } altitudeA && originalB.Altitude is { } altitudeB ? altitudeA + (t * (altitudeB - altitudeA)) : null,
                        Measure = originalA.Measure is { } measureA && originalB.Measure is { } measureB ? measureA + (t * (measureB - measureA)) : null,
                        Time = originalA.Time is { } timeA && originalB.Time is { } timeB ? timeA + ((timeB - timeA) * t) : null,
                    },
                    Offset: 0);

                AddEdge(a, crossing);
                AddEdge(crossing, b);
            }
            else
            {
                AddEdge(a, b);
            }
        }

        pieces.Add([.. current!]);
        return pieces;

        void AddEdge(UnwrappedPosition a, UnwrappedPosition b)
        {
            var middle = (a.Longitude + b.Longitude) / 2;
            var window = GetWindow(middle);

            // An edge on a meridian ±180 stays in the window of the previous edge; the first one keeps its longitude
            if (IsOnWindowSide(middle))
            {
                if (current is not null && Math.Abs(middle - (360.0 * currentWindow)) == 180)
                {
                    window = currentWindow;
                }
                else if (middle is not -180)
                {
                    window--;
                }
            }

            if (current is null)
            {
                current = [a.Shift(window)];
            }
            else if (window != currentWindow)
            {
                pieces.Add([.. current]);
                current = [a.Shift(window)];
            }

            current.Add(b.Shift(window));
            currentWindow = window;
        }

        static bool IsOnWindowSide(double longitude) => Math.IEEERemainder(longitude - 180, 360) == 0;
    }

    private static List<GeoPolygon> SplitPolygon(GeoPolygon polygon)
    {
        if (polygon.Rings.Count == 0 || polygon.Rings[0].Count == 0)
            return [polygon];

        // Unwrap the rings: their longitudes may go past ±180. Each hole moves by turns of 360° next to the exterior ring.
        var exterior = UnwrapRing(polygon.Rings[0]);
        var exteriorWest = exterior.Min(position => position.Longitude);
        var rings = new List<IReadOnlyList<GeoPosition>> { exterior };
        foreach (var hole in polygon.Rings.Skip(1))
        {
            if (hole.Count == 0)
                continue;

            var unwrapped = UnwrapRing(hole);
            var turns = Math.Floor((unwrapped[0].Longitude - exteriorWest) / 360);
            rings.Add(turns == 0 ? unwrapped : [.. unwrapped.Select(position => position with { Longitude = position.Longitude - (360 * turns) })]);
        }

        var unwrappedPolygon = new GeoPolygon(rings);
        var bounds = unwrappedPolygon.GetBounds()!.Value;
        var south = Math.Min(-90, bounds.South);
        var north = Math.Max(90, bounds.North);

        // Cut the polygon to each window of 360° it covers, and move each part back to [-180, 180]
        var result = new List<GeoPolygon>();
        for (var window = GetWindow(bounds.West); window <= (int)Math.Ceiling((bounds.East - 180) / 360); window++)
        {
            var part = GeoOperations.Clip(unwrappedPolygon, new GeoBounds((360.0 * window) - 180, south, (360.0 * window) + 180, north));
            if (part is null)
                continue;

            if (window != 0)
            {
                // The parts are on the grid of the polygon operations: move them on the grid, so they stay exactly on it
                var shift = (long)(360 * window * PolygonCombiner.GridScale);
                part = part.Transform(position => position with { Longitude = (PolygonCombiner.ToGrid(position.Longitude) - shift) / PolygonCombiner.GridScale });
            }

            result.AddRange(part.GetSimpleGeometries().Cast<GeoPolygon>());
        }

        if (result.Count == 0)
            return [new GeoPolygon(polygon.Rings.Select(ring => ring.Select(Normalize)))];

        return result;
    }

    /// <summary>
    /// Unwraps a ring. A ring that goes around a pole, such as the coast of Antarctica, does not come back to its first
    /// longitude: it is closed through the pole it is nearest to.
    /// </summary>
    private static GeoPosition[] UnwrapRing(IReadOnlyList<GeoPosition> ring)
    {
        var unwrapped = Unwrap(ring);
        var first = ring[0];
        var last = unwrapped[^1];
        var turns = IsCrossingEdge(ring[^1], first) ? Math.Round((first.Longitude - ring[^1].Longitude) / 360, MidpointRounding.AwayFromZero) : 0;
        var end = first.Longitude + (last.Longitude - ring[^1].Longitude) - (360 * turns);
        if (end == first.Longitude)
            return unwrapped;

        var pole = ring.Average(position => position.Latitude) >= 0 ? 90.0 : -90.0;
        var result = new List<GeoPosition>(unwrapped);
        if (last.Longitude != end || last.Latitude != first.Latitude)
        {
            result.Add(first with { Longitude = end });
        }

        result.Add(new GeoPosition(end, pole));
        result.Add(new GeoPosition(first.Longitude, pole));
        result.Add(first);
        return [.. result];
    }

    /// <summary>A position whose longitude goes past ±180 by a number of turns: the original position is kept, so it comes back exactly when the turns are undone.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct UnwrappedPosition(GeoPosition Original, double Offset)
    {
        public double Longitude => Original.Longitude + Offset;

        public double Latitude => Original.Latitude;

        /// <summary>Gets the position moved to a window of longitudes.</summary>
        public GeoPosition Shift(int window)
        {
            var offset = Offset - (360.0 * window);
            return offset == 0 ? Original : Original with { Longitude = Original.Longitude + offset };
        }
    }
}
