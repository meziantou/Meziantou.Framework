namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS DNSKEY record for DNSSEC (RFC 4034).</summary>
public sealed class DnsDnskeyRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the flags.</summary>
    public ushort Flags { get; set; }

    /// <summary>Gets or sets the protocol (must be 3).</summary>
    public byte Protocol { get; set; }

    /// <summary>Gets or sets the algorithm number.</summary>
    public byte Algorithm { get; set; }

    /// <summary>Gets or sets the public key data.</summary>
    public byte[] PublicKey { get; set; } = [];
}
