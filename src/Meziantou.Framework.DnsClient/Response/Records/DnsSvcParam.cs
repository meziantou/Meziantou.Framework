namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>
/// Represents a service parameter in a SVCB/HTTPS record.
/// </summary>
public sealed class DnsSvcParam
{
    /// <summary>Gets the parameter key.</summary>
    public ushort Key { get; internal set; }

    /// <summary>Gets the raw parameter value.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Value { get; internal set; } = [];
}
