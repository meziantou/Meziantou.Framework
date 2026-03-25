namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS SRV (service) record (RFC 2782).</summary>
public sealed class DnsSrvRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the priority.</summary>
    public ushort Priority { get; set; }

    /// <summary>Gets or sets the weight.</summary>
    public ushort Weight { get; set; }

    /// <summary>Gets or sets the port.</summary>
    public ushort Port { get; set; }

    /// <summary>Gets or sets the target host name.</summary>
    public string Target { get; set; } = "";
}
