namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>Represents a DNS HINFO record for host information (RFC 1035).</summary>
public sealed class DnsHinfoRecord : DnsRecord
{
    /// <summary>Gets the CPU type string.</summary>
    public string Cpu { get; internal set; } = "";

    /// <summary>Gets the OS type string.</summary>
    public string Os { get; internal set; } = "";
}
