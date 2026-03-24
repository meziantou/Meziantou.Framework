namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>
/// Represents a DNS SOA record containing start of authority information (RFC 1035).
/// </summary>
public sealed class DnsSoaRecord : DnsRecord
{
    /// <summary>Gets the primary name server domain name.</summary>
    public string PrimaryNameServer { get; internal set; } = "";

    /// <summary>Gets the responsible person's mailbox domain name.</summary>
    public string ResponsibleMailbox { get; internal set; } = "";

    /// <summary>Gets the zone serial number.</summary>
    public uint Serial { get; internal set; }

    /// <summary>Gets the refresh interval in seconds.</summary>
    public int Refresh { get; internal set; }

    /// <summary>Gets the retry interval in seconds.</summary>
    public int Retry { get; internal set; }

    /// <summary>Gets the expiration limit in seconds.</summary>
    public int Expire { get; internal set; }

    /// <summary>Gets the minimum TTL in seconds.</summary>
    public uint Minimum { get; internal set; }
}
