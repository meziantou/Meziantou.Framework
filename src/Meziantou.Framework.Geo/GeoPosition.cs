using System.Runtime.InteropServices;

namespace Meziantou.Framework.Geo;

/// <summary>A position on the Earth, in degrees, with an optional altitude, measure and time.</summary>
/// <param name="Longitude">The longitude, from -180 (west) to 180 (east).</param>
/// <param name="Latitude">The latitude, from -90 (south) to 90 (north).</param>
/// <param name="Altitude">The altitude, in meters, or <see langword="null"/> when the position has none.</param>
/// <remarks>
/// Set <see cref="Measure"/> and <see cref="Time"/> with an object initializer, such as
/// <c>new GeoPosition(2.35, 48.86) { Time = DateTimeOffset.UtcNow }</c>. Two positions are equal when their coordinates,
/// altitudes and measures are equal and their times are the same instant, as for <see cref="DateTimeOffset"/>.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct GeoPosition(double Longitude, double Latitude, double? Altitude = null)
{
    /// <summary>The mean radius of the Earth, in meters, as defined by the IUGG.</summary>
    public const double EarthRadius = 6_371_008.8;

    // The optional values are stored without Nullable<T>, so a position stays small: a flag tells which ones are set.
    // The value of a field whose flag is not set is always 0, so the equality can compare the fields.
    private readonly double _altitude = Altitude.GetValueOrDefault();
    private readonly double _measure;
    private readonly long _utcTicks;
    private readonly short _offsetMinutes;
    private readonly ValueFlags _flags = Altitude.HasValue ? ValueFlags.Altitude : ValueFlags.None;

    /// <summary>Gets the altitude, in meters, or <see langword="null"/> when the position has none.</summary>
    public double? Altitude
    {
        get => _flags.HasFlag(ValueFlags.Altitude) ? _altitude : null;
        init
        {
            _altitude = value.GetValueOrDefault();
            _flags = value.HasValue ? _flags | ValueFlags.Altitude : _flags & ~ValueFlags.Altitude;
        }
    }

    /// <summary>
    /// Gets the measure (the M value of WKT, WKB and shapefiles), or <see langword="null"/> when the position has none. Its
    /// meaning depends on the data, such as the distance from the start of a road, in its own unit.
    /// </summary>
    public double? Measure
    {
        get => _flags.HasFlag(ValueFlags.Measure) ? _measure : null;
        init
        {
            _measure = value.GetValueOrDefault();
            _flags = value.HasValue ? _flags | ValueFlags.Measure : _flags & ~ValueFlags.Measure;
        }
    }

    /// <summary>Gets the time at which the position was recorded, such as the time of a point of a GPS track, or <see langword="null"/> when the position has none.</summary>
    public DateTimeOffset? Time
    {
        get => _flags.HasFlag(ValueFlags.Time) ? new DateTimeOffset(_utcTicks, TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(_offsetMinutes)) : null;
        init
        {
            var time = value.GetValueOrDefault();
            _utcTicks = time.UtcTicks;
            _offsetMinutes = (short)time.Offset.TotalMinutes;
            _flags = value.HasValue ? _flags | ValueFlags.Time : _flags & ~ValueFlags.Time;
        }
    }

    /// <summary>Gets the great-circle distance to another position, in meters, on a sphere of radius <see cref="EarthRadius"/>. The altitudes are ignored.</summary>
    /// <param name="other">The other position.</param>
    /// <returns>The distance, in meters.</returns>
    /// <remarks>The haversine formula: the Earth is not a sphere, so the result can differ from the distance on the ellipsoid by up to 0.5%.</remarks>
    public double GetDistance(GeoPosition other)
    {
        var phi1 = Latitude * Math.PI / 180;
        var phi2 = other.Latitude * Math.PI / 180;
        var sinDeltaPhi = Math.Sin((phi2 - phi1) / 2);
        var sinDeltaLambda = Math.Sin((other.Longitude - Longitude) * Math.PI / 180 / 2);
        var a = (sinDeltaPhi * sinDeltaPhi) + (Math.Cos(phi1) * Math.Cos(phi2) * sinDeltaLambda * sinDeltaLambda);
        return 2 * EarthRadius * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>Gets a value that indicates whether this position is equal to another one.</summary>
    /// <param name="other">The other position.</param>
    /// <returns><see langword="true"/> when the coordinates, altitudes and measures are equal and the times are the same instant; otherwise, <see langword="false"/>.</returns>
    public bool Equals(GeoPosition other) =>
        Longitude.Equals(other.Longitude)
        && Latitude.Equals(other.Latitude)
        && _flags == other._flags
        && _altitude.Equals(other._altitude)
        && _measure.Equals(other._measure)
        && _utcTicks == other._utcTicks;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Longitude, Latitude, _altitude, _measure, _utcTicks, _flags);

    [Flags]
    private enum ValueFlags : byte
    {
        None = 0,
        Altitude = 1,
        Measure = 2,
        Time = 4,
    }
}
