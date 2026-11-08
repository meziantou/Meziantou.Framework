namespace Meziantou.Framework.Geo.Internals;

/// <summary>The convex hull of the positions of a geometry, on the longitudes and latitudes.</summary>
internal static class GeoConvexHull
{
    /// <summary>Computes the convex hull with the monotone chain algorithm of Andrew.</summary>
    public static GeoGeometry? GetConvexHull(GeoGeometry geometry)
    {
        var positions = geometry.GetPositions()
            .DistinctBy(position => (position.Longitude, position.Latitude))
            .OrderBy(position => position.Longitude)
            .ThenBy(position => position.Latitude)
            .ToArray();

        switch (positions.Length)
        {
            case 0:
                return null;
            case 1:
                return new GeoPoint(positions[0]);
        }

        var hull = new GeoPosition[2 * positions.Length];
        var count = 0;

        // The lower chain, from west to east, then the upper chain, from east to west: each one turns counter-clockwise
        foreach (var position in positions)
        {
            while (count >= 2 && Cross(hull[count - 2], hull[count - 1], position) <= 0)
            {
                count--;
            }

            hull[count++] = position;
        }

        var lowerCount = count + 1;
        for (var i = positions.Length - 2; i >= 0; i--)
        {
            while (count >= lowerCount && Cross(hull[count - 2], hull[count - 1], positions[i]) <= 0)
            {
                count--;
            }

            hull[count++] = positions[i];
        }

        // The last position is the first one: the ring is closed; a flat hull has its two ends only
        if (count <= 3)
            return new GeoLineString([hull[0], hull[1]]);

        return new GeoPolygon([hull[..count]]);
    }

    private static double Cross(GeoPosition o, GeoPosition a, GeoPosition b) => ((a.Longitude - o.Longitude) * (b.Latitude - o.Latitude)) - ((a.Latitude - o.Latitude) * (b.Longitude - o.Longitude));
}
