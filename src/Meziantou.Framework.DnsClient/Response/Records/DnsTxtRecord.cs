namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>Represents a DNS TXT record containing text strings (RFC 1035).</summary>
public sealed class DnsTxtRecord : DnsRecord
{
    /// <summary>Gets the text strings.</summary>
    public IReadOnlyList<string> Text { get; internal set; } = [];
}
