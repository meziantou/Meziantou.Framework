using System.Text.Json;

namespace Meziantou.Framework.Geo.Maps.Internals;

/// <summary>
/// Gives the properties that <see cref="MapLayer"/> documents their type, whatever the format read: GeoJSON reads every
/// number as a <see cref="double"/>, and SQLite stores the Booleans as integers.
/// </summary>
internal static class MapLayerProperties
{
    private enum PropertyType
    {
        Int32,
        Int64,
        Boolean,
    }

    public static GeoFeature Normalize(MapLayer layer, GeoFeature feature)
    {
        Dictionary<string, object?>? properties = null;
        foreach (var (name, value) in feature.Properties)
        {
            if (value is null || GetType(layer, name) is not { } type)
                continue;

            var converted = Convert(value, type);
            if (ReferenceEquals(converted, value))
                continue;

            properties ??= new Dictionary<string, object?>(feature.Properties, StringComparer.Ordinal);
            properties[name] = converted;
        }

        return properties is null ? feature : new GeoFeature(feature.Geometry, properties, feature.Id);
    }

    private static PropertyType? GetType(MapLayer layer, string name) => (layer, name) switch
    {
        (MapLayer.StatesProvinces, "admin_level") => PropertyType.Int32,
        (MapLayer.Cities, "rank") => PropertyType.Int32,
        (MapLayer.Cities, "population") => PropertyType.Int64,
        (MapLayer.Cities, "capital") => PropertyType.Boolean,
        (MapLayer.MarineAreas or MapLayer.PhysicalRegions, "scale_rank") => PropertyType.Int32,
        _ => null,
    };

    // A value that has the type, or that cannot be converted without loss, is returned as it is
    private static object Convert(object value, PropertyType type)
    {
        switch (type)
        {
            case PropertyType.Int32:
                if (value is int)
                    return value;

                return GetInteger(value) is { } int64 && int64 is >= int.MinValue and <= int.MaxValue ? (int)int64 : value;

            case PropertyType.Int64:
                if (value is long)
                    return value;

                return GetInteger(value) is { } integer ? integer : value;

            default:
                return value switch
                {
                    bool => value,
                    JsonElement { ValueKind: JsonValueKind.True } => true,
                    JsonElement { ValueKind: JsonValueKind.False } => false,
                    _ => GetInteger(value) switch
                    {
                        0 => false,
                        1 => true,
                        _ => value,
                    },
                };
        }
    }

    private static long? GetInteger(object value) => value switch
    {
        sbyte number => number,
        byte number => number,
        short number => number,
        ushort number => number,
        int number => number,
        uint number => number,
        long number => number,
        ulong number when number <= long.MaxValue => (long)number,
        double number when double.IsInteger(number) && number is >= long.MinValue and < long.MaxValue => (long)number,
        float number when float.IsInteger(number) && number is >= long.MinValue and < long.MaxValue => (long)number,
        JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt64(out var number) => number,
        _ => null,
    };
}
