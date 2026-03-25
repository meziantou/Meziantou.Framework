namespace Meziantou.Framework.DnsServer.Protocol;

/// <summary>EDNS(0) options for RFC 6891 support.</summary>
public sealed class DnsEdnsOptions
{
    /// <summary>Gets or sets the maximum UDP payload size the sender can reassemble.</summary>
    public ushort UdpPayloadSize { get; set; } = 4096;

    /// <summary>Gets or sets the EDNS version.</summary>
    public byte Version { get; set; }

    /// <summary>Gets or sets a value indicating whether the DNSSEC OK (DO) flag is set.</summary>
    public bool DnssecOk { get; set; }

    /// <summary>Gets or sets the extended RCODE bits.</summary>
    public byte ExtendedRCode { get; set; }
}
