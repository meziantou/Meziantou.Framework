namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS HINFO (host information) record (RFC 1035).</summary>
public sealed class DnsHinfoRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the CPU type.</summary>
    public string Cpu { get; set; } = "";

    /// <summary>Gets or sets the operating system.</summary>
    public string Os { get; set; } = "";
}
