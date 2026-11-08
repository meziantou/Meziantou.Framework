namespace Meziantou.Framework.Geo.Internals;

/// <summary>The operations whose edges are great-circle arcs, on a sphere.</summary>
internal static class SphericalGeometry
{
    private const double DegreesToRadians = Math.PI / 180;

    /// <summary>Normalizes a difference of longitudes to (-180, 180]: the shortest way from a meridian to another, positive to the east.</summary>
    public static double NormalizeDelta(double delta)
    {
        delta = Math.IEEERemainder(delta, 360);
        return delta <= -180 ? delta + 360 : delta;
    }

    /// <summary>Gets a value that indicates whether a position is inside the rings (even-odd rule), whose edges are great-circle arcs.</summary>
    public static bool Contains(IReadOnlyList<IReadOnlyList<GeoPosition>> rings, GeoPosition position)
    {
        // A ring separates the position from the north pole when the meridian from the position to the pole crosses it an
        // odd number of times. A ring that turns around a pole contains the pole it is nearest to, and no other ring does.
        var inside = false;
        foreach (var ring in rings)
        {
            if (ring.Count == 0)
                continue;

            var winding = 0.0;
            var latitudes = 0.0;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = ring[j];
                var b = ring[i];
                latitudes += b.Latitude;
                if (IsPole(a) || IsPole(b))
                    continue;

                winding += NormalizeDelta(b.Longitude - a.Longitude);
                if (CrossesMeridianNorthOf(a, b, position))
                {
                    inside = !inside;
                }
            }

            if (Math.Abs(winding) > 180 && latitudes >= 0)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>Gets the smallest box that contains the geometry, whose edges are great-circle arcs.</summary>
    public static GeoBounds? GetBounds(IEnumerable<GeoGeometry> geometries)
    {
        var longitudes = new LongitudeRange();
        var south = double.PositiveInfinity;
        var north = double.NegativeInfinity;
        foreach (var geometry in geometries.SelectMany(geometry => geometry.GetSimpleGeometries()))
        {
            switch (geometry)
            {
                case GeoPoint point:
                    AddPosition(point.Position);
                    break;

                case GeoLineString lineString:
                    AddLine(lineString.Positions, closed: false);
                    break;

                case GeoPolygon polygon:
                    foreach (var ring in polygon.Rings)
                    {
                        AddLine(ring, closed: true);
                    }

                    break;
            }
        }

        if (longitudes.IsEmpty)
            return null;

        var (west, east) = longitudes.GetRange();
        return new GeoBounds(west, south, east, north);

        void AddPosition(GeoPosition position)
        {
            south = Math.Min(south, position.Latitude);
            north = Math.Max(north, position.Latitude);
            if (!IsPole(position))
            {
                longitudes.Add(position.Longitude, 0);
            }
        }

        void AddLine(IReadOnlyList<GeoPosition> positions, bool closed)
        {
            if (positions.Count == 0)
                return;

            var winding = 0.0;
            var latitudes = 0.0;
            AddPosition(positions[0]);
            var count = closed ? positions.Count : positions.Count - 1;
            for (var i = 0; i < count; i++)
            {
                var a = positions[i];
                var b = positions[(i + 1) % positions.Count];
                latitudes += a.Latitude;
                AddPosition(b);
                if (IsPole(a) || IsPole(b))
                    continue;

                var delta = NormalizeDelta(b.Longitude - a.Longitude);
                winding += delta;
                longitudes.Add(delta >= 0 ? a.Longitude : b.Longitude, Math.Abs(delta));
                var (min, max) = GetArcLatitudes(a, b);
                south = Math.Min(south, min);
                north = Math.Max(north, max);
            }

            // A ring that turns around a pole contains it, and every longitude
            if (closed && Math.Abs(winding) > 180)
            {
                longitudes.AddFull();
                if (latitudes >= 0)
                {
                    north = 90;
                }
                else
                {
                    south = -90;
                }
            }
        }
    }

    /// <summary>Gets the smallest and the largest latitudes of the shortest great-circle arc between two positions.</summary>
    private static (double Min, double Max) GetArcLatitudes(GeoPosition a, GeoPosition b)
    {
        var min = Math.Min(a.Latitude, b.Latitude);
        var max = Math.Max(a.Latitude, b.Latitude);
        var va = ToVector(a);
        var vb = ToVector(b);
        var normal = Cross(va, vb);
        var length = Math.Sqrt(Dot(normal, normal));
        if (length < 1e-15)
            return (min, max);

        // The northernmost point of the great circle is the projection of the north pole on its plane
        var (nx, ny, nz) = (normal.X / length, normal.Y / length, normal.Z / length);
        var vertex = (X: -nz * nx, Y: -nz * ny, Z: 1 - (nz * nz));
        var vertexLength = Math.Sqrt(Dot(vertex, vertex));
        if (vertexLength < 1e-15)
            return (min, max);

        vertex = (vertex.X / vertexLength, vertex.Y / vertexLength, vertex.Z / vertexLength);
        var vertexLatitude = Math.Asin(Math.Clamp(vertex.Z, -1, 1)) / DegreesToRadians;
        if (IsOnArc(vertex))
        {
            max = Math.Max(max, vertexLatitude);
        }

        if (IsOnArc((-vertex.X, -vertex.Y, -vertex.Z)))
        {
            min = Math.Min(min, -vertexLatitude);
        }

        return (min, max);

        bool IsOnArc((double X, double Y, double Z) point) => Dot(Cross(va, point), normal) > 0 && Dot(Cross(point, vb), normal) > 0;
    }

    /// <summary>Gets a value that indicates whether the shortest great-circle arc between two positions crosses the meridian of a position north of it.</summary>
    private static bool CrossesMeridianNorthOf(GeoPosition a, GeoPosition b, GeoPosition position)
    {
        // The side of each end: east of the meridian, or west of it (on it included), so that a vertex on the meridian is
        // counted once, as by the planar test
        var da = NormalizeDelta(a.Longitude - position.Longitude);
        var db = NormalizeDelta(b.Longitude - position.Longitude);
        if ((da > 0) == (db > 0))
            return false;

        // The arc crosses the opposite meridian instead
        if (Math.Abs(db - da) > 180)
            return false;

        // The intersection of the plane of the arc and the plane of the meridian, on the side of the meridian
        var lambda = position.Longitude * DegreesToRadians;
        var normal = Cross(ToVector(a), ToVector(b));
        var meridianNormal = (X: -Math.Sin(lambda), Y: Math.Cos(lambda), Z: 0.0);
        var direction = Cross(normal, meridianNormal);
        if ((direction.X * Math.Cos(lambda)) + (direction.Y * Math.Sin(lambda)) < 0)
        {
            direction = (-direction.X, -direction.Y, -direction.Z);
        }

        var latitude = Math.Atan2(direction.Z, Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y)));
        return latitude > position.Latitude * DegreesToRadians;
    }

    private static bool IsPole(GeoPosition position) => Math.Abs(position.Latitude) >= 90;

    private static (double X, double Y, double Z) ToVector(GeoPosition position)
    {
        var phi = position.Latitude * DegreesToRadians;
        var lambda = position.Longitude * DegreesToRadians;
        return (Math.Cos(phi) * Math.Cos(lambda), Math.Cos(phi) * Math.Sin(lambda), Math.Sin(phi));
    }

    private static (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b)
        => ((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

    private static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
}
