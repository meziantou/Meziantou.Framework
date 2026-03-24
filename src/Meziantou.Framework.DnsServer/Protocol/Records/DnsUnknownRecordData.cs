namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents an unknown or unsupported DNS record type.</summary>
public sealed class DnsUnknownRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the raw record data.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Data { get; set; } = [];
}
