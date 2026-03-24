namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents an EDNS option within an OPT record.</summary>
public sealed class DnsEdnsOption
{
    /// <summary>Gets or sets the option code.</summary>
    public ushort Code { get; set; }

    /// <summary>Gets or sets the option data.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Data { get; set; } = [];
}
