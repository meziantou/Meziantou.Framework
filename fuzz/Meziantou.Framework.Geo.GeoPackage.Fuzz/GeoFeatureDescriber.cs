using System.Globalization;
using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Geo;

namespace Meziantou.Framework.GeoPackageFuzz;

/// <summary>Describes features as text, to compare the results of two readers, and checks the operations of the model on them.</summary>
internal static class GeoFeatureDescriber
{
    public static string Describe(IEnumerable<GeoFeature> features)
    {
        var builder = new StringBuilder();
        foreach (var feature in features)
        {
            builder.Append(feature.Id).Append(" | ");
            Describe(builder, feature.Geometry);
            foreach (var (name, value) in feature.Properties.OrderBy(property => property.Key, StringComparer.Ordinal))
            {
                builder.Append(" | ").Append(name).Append('=').Append(FuzzAssert.Format(value)).Append(':').Append(value?.GetType().Name);
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Calls the operations of the model on the features: none of them throws, and finite bounds contain the finite positions.</summary>
    public static void Exercise(IEnumerable<GeoFeature> features)
    {
        foreach (var feature in features)
        {
            if (feature.Geometry is not { } geometry)
                continue;

            // A position of NaN coordinates, which WKB can hold, gives bounds of NaN
            var bounds = geometry.GetBounds();
            if (bounds is { } finite && !(double.IsFinite(finite.West) && double.IsFinite(finite.South) && double.IsFinite(finite.East) && double.IsFinite(finite.North)))
                continue;

            foreach (var position in geometry.GetPositions())
            {
                if (bounds is { } box && double.IsFinite(position.Longitude) && double.IsFinite(position.Latitude))
                {
                    FuzzAssert.True(position.Longitude >= box.West && position.Longitude <= box.East && position.Latitude >= box.South && position.Latitude <= box.North, "The bounds of a geometry do not contain its positions");
                }
            }
        }
    }

    public static GeoBounds ReadBox(ref ReadOnlySpan<byte> data)
    {
        var a = (sbyte)FuzzInput.ReadByte(ref data);
        var b = (sbyte)FuzzInput.ReadByte(ref data);
        var c = (sbyte)FuzzInput.ReadByte(ref data);
        var d = (sbyte)FuzzInput.ReadByte(ref data);
        return new GeoBounds(Math.Min(a, c), Math.Min(b, d), Math.Max(a, c), Math.Max(b, d));
    }

    public static bool Intersects(GeoBounds a, GeoBounds b) => b.West <= a.East && b.East >= a.West && b.South <= a.North && b.North >= a.South;

    /// <summary>Enumerates the features of a streaming reader that completes synchronously: the features, and the <see cref="FormatException"/> that stopped it.</summary>
    public static (List<GeoFeature> Features, FormatException? Error) ReadAll(Func<IAsyncEnumerable<GeoFeature>> read)
    {
        var features = new List<GeoFeature>();
        try
        {
            var enumerator = read().GetAsyncEnumerator();
            try
            {
                while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                {
                    features.Add(enumerator.Current);
                }
            }
            finally
            {
                enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            return (features, null);
        }
        catch (FormatException ex)
        {
            return (features, ex);
        }
    }

    private static void Describe(StringBuilder builder, GeoGeometry? geometry)
    {
        switch (geometry)
        {
            case null:
                builder.Append("null");
                break;
            case GeoGeometryCollection collection:
                builder.Append("Collection(");
                foreach (var item in collection.Geometries)
                {
                    Describe(builder, item);
                    builder.Append(';');
                }

                builder.Append(')');
                break;
            case GeoMultiPolygon multiPolygon:
                builder.Append("MultiPolygon(");
                foreach (var polygon in multiPolygon.Polygons)
                {
                    Describe(builder, polygon);
                    builder.Append(';');
                }

                builder.Append(')');
                break;
            case GeoMultiLineString multiLineString:
                builder.Append("MultiLineString(");
                foreach (var line in multiLineString.LineStrings)
                {
                    Describe(builder, line);
                    builder.Append(';');
                }

                builder.Append(')');
                break;
            case GeoPolygon polygon:
                builder.Append("Polygon(");
                foreach (var ring in polygon.Rings)
                {
                    builder.Append('[').AppendJoin(", ", ring.Select(Describe)).Append(']');
                }

                builder.Append(')');
                break;
            default:
                builder.Append(geometry.Type).Append('(').AppendJoin(", ", geometry.GetPositions().Select(Describe)).Append(')');
                break;
        }
    }

    private static string Describe(GeoPosition position) => string.Create(CultureInfo.InvariantCulture, $"{position.Longitude:R} {position.Latitude:R} {position.Altitude:R} {position.Measure:R} {position.Time:O}");
}
