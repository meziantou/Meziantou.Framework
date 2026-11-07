namespace Meziantou.Framework.Geo.Maps.Tool;

internal static class GeoBoxes
{
    // The parts of a country further than this from its main part, in degrees, are left out of its extent
    private const double MaxGap = 5;

    public static bool Intersects(GeoBounds a, GeoBounds b) => b.West <= a.East && b.East >= a.West && b.South <= a.North && b.North >= a.South;

    /// <summary>
    /// Gets the extent of countries: for each one, the box of its largest polygon and of the polygons near it, so that a
    /// country is not drawn with its overseas territories (French Guiana, the Caribbean Netherlands) or with the part of
    /// it on the other side of the 180th meridian (the east of Chukotka).
    /// </summary>
    public static GeoBounds? GetMainExtent(IEnumerable<GeoFeature> countries)
    {
        GeoBounds? result = null;
        foreach (var country in countries)
        {
            var parts = country.Geometry?.GetSimpleGeometries().OfType<GeoPolygon>()
                .Where(polygon => polygon.Rings.Count > 0 && polygon.Rings[0].Count > 0)
                .Select(polygon => (Bounds: polygon.GetBounds()!.Value, Area: Math.Abs(GetArea(polygon.Rings[0]))))
                .ToList();
            if (parts is null || parts.Count == 0)
                continue;

            var largest = parts.MaxBy(part => part.Area);
            var extent = largest.Bounds;
            var remaining = parts.Where(part => part != largest).ToList();
            bool added;
            do
            {
                added = false;
                for (var i = remaining.Count - 1; i >= 0; i--)
                {
                    if (Intersects(Expand(extent, MaxGap), remaining[i].Bounds))
                    {
                        extent = extent.Union(remaining[i].Bounds);
                        remaining.RemoveAt(i);
                        added = true;
                    }
                }
            }
            while (added);

            result = result is { } current ? current.Union(extent) : extent;
        }

        return result;
    }

    private static GeoBounds Expand(GeoBounds bounds, double margin) => new(bounds.West - margin, bounds.South - margin, bounds.East + margin, bounds.North + margin);

    private static double GetArea(IReadOnlyList<GeoPosition> ring)
    {
        var area = 0.0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            area += (a.Longitude * b.Latitude) - (b.Longitude * a.Latitude);
        }

        return area / 2;
    }
}
