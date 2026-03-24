namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>
/// Represents a DNS record type that is not explicitly supported with a strongly-typed class.
/// The raw record data is preserved for consumers to parse.
/// </summary>
public sealed class DnsUnknownRecord : DnsRecord
{
    /// <summary>Gets the raw resource data.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Data { get; internal set; } = [];
}
