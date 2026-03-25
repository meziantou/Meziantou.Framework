namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS SOA (start of authority) record (RFC 1035).</summary>
public sealed class DnsSoaRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the primary name server.</summary>
    public string PrimaryNameServer { get; set; } = "";

    /// <summary>Gets or sets the responsible mailbox (encoded as a domain name).</summary>
    public string ResponsibleMailbox { get; set; } = "";

    /// <summary>Gets or sets the zone serial number.</summary>
    public uint Serial { get; set; }

    /// <summary>Gets or sets the refresh interval in seconds.</summary>
    public int Refresh { get; set; }

    /// <summary>Gets or sets the retry interval in seconds.</summary>
    public int Retry { get; set; }

    /// <summary>Gets or sets the expire interval in seconds.</summary>
    public int Expire { get; set; }

    /// <summary>Gets or sets the minimum TTL in seconds.</summary>
    public uint Minimum { get; set; }
}
