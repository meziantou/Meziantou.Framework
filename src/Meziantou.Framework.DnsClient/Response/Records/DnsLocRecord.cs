namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>
/// Represents a DNS LOC record for location information (RFC 1876).
/// </summary>
public sealed class DnsLocRecord : DnsRecord
{
    /// <summary>Gets the version (must be 0).</summary>
    public byte Version { get; internal set; }

    /// <summary>Gets the size of the sphere enclosing the entity in centimeters.</summary>
    public byte Size { get; internal set; }

    /// <summary>Gets the horizontal precision in centimeters.</summary>
    public byte HorizontalPrecision { get; internal set; }

    /// <summary>Gets the vertical precision in centimeters.</summary>
    public byte VerticalPrecision { get; internal set; }

    /// <summary>Gets the latitude in thousandths of a second of arc, with 2^31 representing the equator.</summary>
    public uint Latitude { get; internal set; }

    /// <summary>Gets the longitude in thousandths of a second of arc, with 2^31 representing the prime meridian.</summary>
    public uint Longitude { get; internal set; }

    /// <summary>Gets the altitude in centimeters above a base of 100,000m below the GPS reference spheroid.</summary>
    public uint Altitude { get; internal set; }
}
