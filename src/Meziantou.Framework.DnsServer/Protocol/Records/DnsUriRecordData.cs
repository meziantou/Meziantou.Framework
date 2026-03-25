namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS URI record (RFC 7553).</summary>
public sealed class DnsUriRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the priority.</summary>
    public ushort Priority { get; set; }

    /// <summary>Gets or sets the weight.</summary>
    public ushort Weight { get; set; }

    /// <summary>Gets or sets the target URI.</summary>
    public string Target { get; set; } = "";
}
