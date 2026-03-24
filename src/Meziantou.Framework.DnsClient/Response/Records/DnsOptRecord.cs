namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>
/// Represents a DNS OPT pseudo-record for EDNS(0) (RFC 6891).
/// This record appears in the additional section and carries extension information.
/// </summary>
public sealed class DnsOptRecord : DnsRecord
{
    /// <summary>Gets the requestor's UDP payload size (encoded in the CLASS field).</summary>
    public ushort UdpPayloadSize { get; internal set; }

    /// <summary>Gets the extended RCODE (upper 8 bits, combined with the message header RCODE).</summary>
    public byte ExtendedRCode { get; internal set; }

    /// <summary>Gets the EDNS version.</summary>
    public byte EdnsVersion { get; internal set; }

    /// <summary>Gets a value indicating whether the DNSSEC OK (DO) flag is set.</summary>
    public bool DnssecOk { get; internal set; }

    /// <summary>Gets the EDNS options.</summary>
    public IReadOnlyList<DnsEdnsOption> Options { get; internal set; } = [];
}
