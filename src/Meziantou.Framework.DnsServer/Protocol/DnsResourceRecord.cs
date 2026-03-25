namespace Meziantou.Framework.DnsServer.Protocol;

/// <summary>Represents a DNS resource record.</summary>
public sealed class DnsResourceRecord
{
    /// <summary>Gets or sets the domain name to which this record pertains.</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the record type.</summary>
    public DnsQueryType Type { get; set; }

    /// <summary>Gets or sets the record class.</summary>
    public DnsQueryClass Class { get; set; } = DnsQueryClass.IN;

    /// <summary>Gets or sets the time to live in seconds.</summary>
    public uint TimeToLive { get; set; }

    /// <summary>Gets or sets the record-specific data.</summary>
    public DnsResourceRecordData? Data { get; set; }
}
