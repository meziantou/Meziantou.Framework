namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS LOC (location) record (RFC 1876).</summary>
public sealed class DnsLocRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the version (must be 0).</summary>
    public byte Version { get; set; }

    /// <summary>Gets or sets the size of the sphere of accuracy.</summary>
    public byte Size { get; set; }

    /// <summary>Gets or sets the horizontal precision.</summary>
    public byte HorizontalPrecision { get; set; }

    /// <summary>Gets or sets the vertical precision.</summary>
    public byte VerticalPrecision { get; set; }

    /// <summary>Gets or sets the latitude.</summary>
    public uint Latitude { get; set; }

    /// <summary>Gets or sets the longitude.</summary>
    public uint Longitude { get; set; }

    /// <summary>Gets or sets the altitude.</summary>
    public uint Altitude { get; set; }
}
