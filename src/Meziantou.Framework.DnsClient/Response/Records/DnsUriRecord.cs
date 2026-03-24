namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>
/// Represents a DNS URI record (RFC 7553).
/// </summary>
public sealed class DnsUriRecord : DnsRecord
{
    /// <summary>Gets the priority. Lower values are preferred.</summary>
    public ushort Priority { get; internal set; }

    /// <summary>Gets the weight for load balancing.</summary>
    public ushort Weight { get; internal set; }

    /// <summary>Gets the URI target.</summary>
    public string Target { get; internal set; } = "";
}
