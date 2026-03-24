namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>
/// Represents an EDNS option in an OPT record.
/// </summary>
public sealed class DnsEdnsOption
{
    /// <summary>Gets the option code.</summary>
    public ushort Code { get; internal set; }

    /// <summary>Gets the option data.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Data { get; internal set; } = [];
}
