namespace Meziantou.Framework.Geo.Internals;

/// <summary>Checks the arguments of the writers of the formats.</summary>
internal static class GeoValidation
{
    /// <summary>Throws when a position of the features is not finite, as no format can write it.</summary>
    public static void EnsureFinite(GeoFeatureCollection features, string paramName)
    {
        foreach (var feature in features)
        {
            if (feature.Geometry is not null)
            {
                EnsureFinite(feature.Geometry, paramName);
            }
        }
    }

    /// <summary>Throws when a position of a geometry is not finite, as no format can write it.</summary>
    public static void EnsureFinite(GeoGeometry geometry, string paramName)
    {
        foreach (var position in geometry.GetPositions())
        {
            if (!double.IsFinite(position.Longitude) || !double.IsFinite(position.Latitude) || position.Altitude is { } altitude && !double.IsFinite(altitude))
                throw new ArgumentException($"The position {position} is not finite.", paramName);
        }
    }
}
