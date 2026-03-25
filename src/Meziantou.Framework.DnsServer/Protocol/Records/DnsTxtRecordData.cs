namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS TXT record (RFC 1035).</summary>
public sealed class DnsTxtRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the text strings.</summary>
    public IReadOnlyList<string> Text { get; set; } = [];
}
