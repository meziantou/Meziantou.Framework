namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>Represents a DNS SRV record containing service location information (RFC 2782).</summary>
public sealed class DnsSrvRecord : DnsRecord
{
    /// <summary>Gets the priority. Lower values are preferred.</summary>
    public ushort Priority { get; internal set; }

    /// <summary>Gets the weight for load balancing among records with equal priority.</summary>
    public ushort Weight { get; internal set; }

    /// <summary>Gets the TCP or UDP port on which the service is available.</summary>
    public ushort Port { get; internal set; }

    /// <summary>Gets the target host providing the service.</summary>
    public string Target { get; internal set; } = "";
}
