using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Geo;

/// <summary>Encodes and decodes the positions of a line with the encoded polyline algorithm of Google, used by Google Maps, OSRM, Valhalla or Strava, such as <c>_p~iF~ps|U_ulLnnqC_mqNvxq`@</c>.</summary>
/// <remarks>
/// <para>
/// The format stores the latitude then the longitude of each position, rounded to a number of decimal digits: 5 for
/// Google Maps and OSRM by default, 6 for Valhalla and the <c>polyline6</c> geometries of OSRM. It has no altitude: the
/// decoded positions have none, and the altitudes are ignored when encoding.
/// </para>
/// <para>
/// The coordinates are rounded half away from zero, as the reference implementation, and each position is stored as
/// the difference with the previous rounded position, so the rounding errors do not add up along the line.
/// </para>
/// </remarks>
public static class EncodedPolyline
{
    private const int MaxPrecision = 10;

    // Keep the scaled coordinates and their differences far from the limits of a long
    private const double MaxScaledValue = 1e18;

    /// <summary>Decodes the positions of an encoded polyline.</summary>
    /// <param name="encoded">The encoded polyline.</param>
    /// <param name="precision">The number of decimal digits of the coordinates: 5 for Google Maps and OSRM, 6 for Valhalla.</param>
    /// <returns>The positions, without altitude. Create a <see cref="GeoLineString"/> from them to use them as a geometry.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="precision"/> is not between 0 and 10.</exception>
    /// <exception cref="FormatException">The text is not a valid encoded polyline.</exception>
    public static IReadOnlyList<GeoPosition> Decode(string encoded, int precision = 5)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        ValidatePrecision(precision);
        var factor = Math.Pow(10, precision);
        var positions = new List<GeoPosition>();
        long latitude = 0;
        long longitude = 0;
        var index = 0;
        while (index < encoded.Length)
        {
            var start = index;
            latitude = Add(latitude, ReadValue(encoded, ref index), start);
            if (index >= encoded.Length)
                throw new FormatException($"The encoded polyline ends after the latitude at position {start.ToString(CultureInfo.InvariantCulture)}, without its longitude.");

            start = index;
            longitude = Add(longitude, ReadValue(encoded, ref index), start);
            positions.Add(new GeoPosition(longitude / factor, latitude / factor));
        }

        return positions;
    }

    /// <summary>Encodes positions as an encoded polyline.</summary>
    /// <param name="positions">The positions. Their altitudes are ignored.</param>
    /// <param name="precision">The number of decimal digits of the coordinates: 5 for Google Maps and OSRM, 6 for Valhalla.</param>
    /// <returns>The encoded polyline.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="precision"/> is not between 0 and 10.</exception>
    /// <exception cref="ArgumentException">A coordinate is not finite or is too large to be encoded.</exception>
    public static string Encode(IEnumerable<GeoPosition> positions, int precision = 5)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ValidatePrecision(precision);
        var factor = Math.Pow(10, precision);
        var builder = new StringBuilder();
        long previousLatitude = 0;
        long previousLongitude = 0;
        foreach (var position in positions)
        {
            var latitude = Scale(position.Latitude, factor, position, nameof(positions));
            var longitude = Scale(position.Longitude, factor, position, nameof(positions));
            WriteValue(builder, latitude - previousLatitude);
            WriteValue(builder, longitude - previousLongitude);
            previousLatitude = latitude;
            previousLongitude = longitude;
        }

        return builder.ToString();
    }

    private static void ValidatePrecision(int precision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(precision);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(precision, MaxPrecision);
    }

    private static long Scale(double value, double factor, GeoPosition position, string paramName)
    {
        var scaled = Math.Round(value * factor, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(scaled) || Math.Abs(scaled) > MaxScaledValue)
            throw new ArgumentException($"The position {position} cannot be encoded: its coordinates must be finite and not too large.", paramName);

        return (long)scaled;
    }

    private static void WriteValue(StringBuilder builder, long value)
    {
        // Zigzag encoding: the sign goes to the lowest bit, then 5-bit chunks from the lowest, 0x20 marking that another chunk follows
        var bits = (ulong)(value < 0 ? ~(value << 1) : value << 1);
        while (bits >= 0x20)
        {
            builder.Append((char)((int)(0x20 | (bits & 0x1F)) + 63));
            bits >>= 5;
        }

        builder.Append((char)((int)bits + 63));
    }

    private static long ReadValue(string encoded, ref int index)
    {
        var start = index;
        ulong bits = 0;
        var shift = 0;
        while (true)
        {
            if (index >= encoded.Length)
                throw new FormatException($"The encoded polyline ends in the middle of the value at position {start.ToString(CultureInfo.InvariantCulture)}.");

            var c = encoded[index];
            var chunk = c - 63;
            if (chunk is < 0 or > 63)
                throw new FormatException($"The character '{c}' at position {index.ToString(CultureInfo.InvariantCulture)} is not valid in an encoded polyline.");

            if (shift > 60)
                throw new FormatException($"The value at position {start.ToString(CultureInfo.InvariantCulture)} of the encoded polyline is too large.");

            bits |= (ulong)(chunk & 0x1F) << shift;
            shift += 5;
            index++;
            if (chunk < 0x20)
                break;
        }

        var value = (long)(bits >> 1);
        return (bits & 1) is 0 ? value : ~value;
    }

    private static long Add(long total, long value, int position)
    {
        try
        {
            return checked(total + value);
        }
        catch (OverflowException ex)
        {
            throw new FormatException($"The value at position {position.ToString(CultureInfo.InvariantCulture)} of the encoded polyline is too large.", ex);
        }
    }
}
