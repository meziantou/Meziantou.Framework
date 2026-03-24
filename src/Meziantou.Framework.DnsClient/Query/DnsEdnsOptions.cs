namespace Meziantou.Framework.DnsClient.Query;

/// <summary>
/// Represents EDNS(0) options for a DNS query (RFC 6891).
/// </summary>
public sealed class DnsEdnsOptions
{
    /// <summary>Gets or sets the maximum UDP payload size the client can handle. Default is 4096.</summary>
    public ushort UdpPayloadSize { get; set; } = 4096;

    /// <summary>Gets or sets the EDNS version. Default is 0.</summary>
    public byte Version { get; set; }

    /// <summary>Gets or sets a value indicating whether the DNSSEC OK (DO) flag is set, requesting DNSSEC records.</summary>
    public bool DnssecOk { get; set; }

    /// <summary>Gets or sets the extended RCODE bits (upper 8 bits).</summary>
    public byte ExtendedRCode { get; set; }
}
