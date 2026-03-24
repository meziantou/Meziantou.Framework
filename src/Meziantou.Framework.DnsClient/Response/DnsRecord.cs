using Meziantou.Framework.DnsClient.Query;

namespace Meziantou.Framework.DnsClient.Response;

/// <summary>Base class for all DNS resource records.</summary>
public abstract class DnsRecord
{
    /// <summary>Gets the domain name this record belongs to.</summary>
    public string Name { get; internal set; } = "";

    /// <summary>Gets the record type.</summary>
    public DnsQueryType RecordType { get; internal set; }

    /// <summary>Gets the record class.</summary>
    public DnsQueryClass RecordClass { get; internal set; }

    /// <summary>Gets the time to live in seconds.</summary>
    public uint TimeToLive { get; internal set; }

    /// <summary>Gets the length of the resource data in bytes.</summary>
    public ushort DataLength { get; internal set; }
}
