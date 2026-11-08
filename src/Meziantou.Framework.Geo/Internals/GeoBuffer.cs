namespace Meziantou.Framework.Geo.Internals;

/// <summary>
/// The buffer of a geometry: the union of a circle around each point, of a band along each edge of the lines and the rings,
/// and of the joins and the ends of the lines, combined with the polygons (added for a positive distance, removed for a
/// negative one) by <see cref="PolygonCombiner"/>.
/// </summary>
/// <remarks>
/// The positions of the pieces are computed on the WGS 84 ellipsoid. The edges are cut so that each piece spans less than
/// <see cref="MaxPieceDegrees"/> of latitude, where the edges straight in degrees are close to the edges at a constant distance.
/// </remarks>
internal static class GeoBuffer
{
    /// <summary>The largest difference of latitude of a piece of an edge.</summary>
    private const double MaxPieceDegrees = 0.5;

    private const double DegreesToRadians = Math.PI / 180;

    public static GeoGeometry? Buffer(GeoGeometry geometry, double distance, GeoBufferOptions options)
    {
        if (double.IsNaN(distance) || double.IsInfinity(distance))
            throw new ArgumentOutOfRangeException(nameof(distance), distance, "The distance must be a finite number.");

        GeoOperations.ValidateCoordinates(geometry, paramName: null);

        var radius = Math.Abs(distance);
        var polygons = GeoOperations.GetOrientedRings(geometry).ToList();
        var pieces = new List<IReadOnlyList<GeoPosition>>();
        if (radius > 0)
        {
            var builder = new Builder(radius, options, pieces);
            foreach (var simple in geometry.GetSimpleGeometries())
            {
                switch (simple)
                {
                    // A point has no negative buffer
                    case GeoPoint point when distance > 0:
                        builder.AddCircle(point.Position);
                        break;

                    // The negative buffer of a line is empty
                    case GeoLineString lineString when distance > 0:
                        builder.AddLine(lineString.Positions, closed: false);
                        break;

                    case GeoPolygon polygon:
                        foreach (var ring in polygon.Rings)
                        {
                            builder.AddLine(ring, closed: true);
                        }

                        break;
                }
            }
        }

        if (distance <= 0 && polygons.Count == 0)
            return null;

        List<(long X, long Y)[]> contours = distance > 0
            ? PolygonCombiner.Combine(polygons.Concat(pieces), second: null, PolygonCombineMode.Union, PolygonFillRule.CounterClockwise, PolygonFillRule.CounterClockwise)
            : PolygonCombiner.Combine(polygons, pieces, PolygonCombineMode.Exclude, PolygonFillRule.CounterClockwise, PolygonFillRule.CounterClockwise);

        return contours.Count == 0 ? null : GeoOperations.ToGeometry(contours, asMultiPolygon: false);
    }

    private sealed class Builder(double radius, GeoBufferOptions options, List<IReadOnlyList<GeoPosition>> pieces)
    {
        private readonly double _step = 90.0 / options.SegmentsPerQuarterCircle;

        public void AddCircle(GeoPosition center)
        {
            var count = 4 * options.SegmentsPerQuarterCircle;
            var ring = new GeoPosition[count];
            for (var i = 0; i < count; i++)
            {
                // The azimuths turn clockwise: the ring is reversed to turn counter-clockwise
                ring[count - 1 - i] = Geodesic.GetDestination(center, i * _step, radius);
            }

            pieces.Add(ring);
        }

        public void AddLine(IReadOnlyList<GeoPosition> positions, bool closed)
        {
            var vertices = new List<(GeoPosition Position, bool IsVertex)>(positions.Count);
            foreach (var position in positions)
            {
                if (vertices.Count != 0 && SameLocation(vertices[^1].Position, position))
                    continue;

                if (vertices.Count != 0)
                {
                    Densify(vertices, vertices[^1].Position, position);
                }

                vertices.Add((position, IsVertex: true));
            }

            if (closed && vertices.Count > 1)
            {
                if (SameLocation(vertices[0].Position, vertices[^1].Position))
                {
                    vertices.RemoveAt(vertices.Count - 1);
                }

                Densify(vertices, vertices[^1].Position, vertices[0].Position);
            }

            switch (vertices.Count)
            {
                case 0:
                    return;

                case 1:
                    // A line of one position is a point, without a direction to cut it flat
                    if (closed || options.EndCapStyle is not GeoBufferEndCapStyle.Flat)
                    {
                        AddCircle(vertices[0].Position);
                    }

                    return;
            }

            var segmentCount = closed ? vertices.Count : vertices.Count - 1;
            var azimuths = new double[segmentCount];
            for (var i = 0; i < segmentCount; i++)
            {
                var start = vertices[i].Position;
                var end = vertices[(i + 1) % vertices.Count].Position;
                azimuths[i] = GetAzimuth(start, end);
                var leftStart = Offset(start, azimuths[i], -1);
                var leftEnd = Offset(end, azimuths[i], -1);
                var rightStart = Offset(start, azimuths[i], 1);
                var rightEnd = Offset(end, azimuths[i], 1);

                // The ends of the edge are vertices of the band, so that it shares its edges with the joins and the caps exactly
                AddPiece([rightStart, rightEnd, end, leftEnd, leftStart, start]);
            }

            var joinEnd = closed ? vertices.Count : vertices.Count - 1;
            for (var i = closed ? 0 : 1; i < joinEnd; i++)
            {
                var incoming = azimuths[(i - 1 + segmentCount) % segmentCount];
                AddJoin(vertices[i].Position, incoming, azimuths[i], vertices[i].IsVertex ? options.JoinStyle : GeoBufferJoinStyle.Bevel);
            }

            if (!closed)
            {
                AddCap(vertices[0].Position, azimuths[0] + 180);
                AddCap(vertices[^1].Position, azimuths[^1]);
            }
        }

        /// <summary>Adds the piece on the outer side of a turn, between the band of the incoming edge and the band of the outgoing one.</summary>
        private void AddJoin(GeoPosition vertex, double incoming, double outgoing, GeoBufferJoinStyle style)
        {
            var turn = Math.IEEERemainder(outgoing - incoming, 360);
            if (Math.Abs(turn) < 1e-9)
                return;

            // A turn to the right (clockwise) opens the left side
            var side = turn > 0 ? -1 : 1;
            var from = incoming + (side * 90);
            var to = from + turn;
            var first = Offset(vertex, incoming, side);
            var last = Offset(vertex, outgoing, side);
            switch (style)
            {
                case GeoBufferJoinStyle.Round:
                    AddPiece([vertex, first, .. GetArc(vertex, from, to), last]);
                    break;

                case GeoBufferJoinStyle.Miter when 1 / Math.Cos(turn / 2 * DegreesToRadians) <= options.MiterLimit:
                    var corner = Geodesic.GetDestination(vertex, from + (turn / 2), radius / Math.Cos(turn / 2 * DegreesToRadians));
                    AddPiece([vertex, first, corner, last]);
                    break;

                default:
                    AddPiece([vertex, first, last]);
                    break;
            }
        }

        private void AddCap(GeoPosition end, double azimuth)
        {
            switch (options.EndCapStyle)
            {
                case GeoBufferEndCapStyle.Round:
                    AddPiece([end, Offset(end, azimuth, -1), .. GetArc(end, azimuth - 90, azimuth + 90), Offset(end, azimuth, 1)]);
                    break;

                case GeoBufferEndCapStyle.Square:
                    var diagonal = radius * Math.Sqrt(2);
                    AddPiece([end, Offset(end, azimuth, -1), Geodesic.GetDestination(end, azimuth - 45, diagonal), Geodesic.GetDestination(end, azimuth + 45, diagonal), Offset(end, azimuth, 1)]);
                    break;
            }
        }

        /// <summary>Gets the positions of an arc of the circle around a position, between two azimuths, without its ends.</summary>
        private IEnumerable<GeoPosition> GetArc(GeoPosition center, double from, double to)
        {
            var count = (int)Math.Ceiling(Math.Abs(to - from) / _step);
            for (var i = 1; i < count; i++)
            {
                yield return Geodesic.GetDestination(center, from + ((to - from) * i / count), radius);
            }
        }

        /// <summary>Gets the position at the distance on one side of an edge: -1 for the left, 1 for the right.</summary>
        private GeoPosition Offset(GeoPosition position, double azimuth, int side) => Geodesic.GetDestination(position, azimuth + (side * 90), radius);

        private void AddPiece(GeoPosition[] ring)
        {
            if (GetDoubledArea(ring) < 0)
            {
                Array.Reverse(ring);
            }

            pieces.Add(ring);
        }

        /// <summary>Adds the positions that cut an edge in pieces that span less than <see cref="MaxPieceDegrees"/> of latitude: the scale of the longitudes only depends on the latitude.</summary>
        private static void Densify(List<(GeoPosition Position, bool IsVertex)> vertices, GeoPosition start, GeoPosition end)
        {
            var count = (int)Math.Ceiling(Math.Abs(end.Latitude - start.Latitude) / MaxPieceDegrees);
            for (var i = 1; i < count; i++)
            {
                var t = (double)i / count;
                vertices.Add((new GeoPosition(start.Longitude + ((end.Longitude - start.Longitude) * t), start.Latitude + ((end.Latitude - start.Latitude) * t)), IsVertex: false));
            }
        }

        /// <summary>Gets the direction of an edge straight in degrees, in degrees clockwise from the north, with the scale of the longitudes at its middle.</summary>
        private static double GetAzimuth(GeoPosition start, GeoPosition end)
        {
            var scale = Math.Cos((start.Latitude + end.Latitude) / 2 * DegreesToRadians);
            return Math.Atan2((end.Longitude - start.Longitude) * scale, end.Latitude - start.Latitude) / DegreesToRadians;
        }

        /// <summary>Gets a value indicating whether two positions are the same place: an edge along a pole has no length.</summary>
        private static bool SameLocation(GeoPosition a, GeoPosition b) => a.Latitude == b.Latitude && (a.Longitude == b.Longitude || Math.Abs(a.Latitude) == 90);

        private static double GetDoubledArea(GeoPosition[] ring)
        {
            var area = 0.0;
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                area += (a.Longitude * b.Latitude) - (b.Longitude * a.Latitude);
            }

            return area;
        }
    }
}
