namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>Represents a DNS DNSKEY record (RFC 4034, DNSSEC).</summary>
public sealed class DnsDnskeyRecord : DnsRecord
{
    /// <summary>Gets the flags. Bit 7 is the Zone Key flag, bit 15 is the Secure Entry Point flag.</summary>
    public ushort Flags { get; internal set; }

    /// <summary>Gets the protocol. Must be 3 for DNSSEC.</summary>
    public byte Protocol { get; internal set; }

    /// <summary>Gets the algorithm number.</summary>
    public byte Algorithm { get; internal set; }

    /// <summary>Gets the public key data.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] PublicKey { get; internal set; } = [];
}
