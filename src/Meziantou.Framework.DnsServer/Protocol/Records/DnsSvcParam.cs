namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a service parameter in SVCB/HTTPS records.</summary>
public sealed class DnsSvcParam
{
    /// <summary>Gets or sets the parameter key.</summary>
    public ushort Key { get; set; }

    /// <summary>Gets or sets the parameter value.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Value { get; set; } = [];
}
