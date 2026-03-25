namespace Meziantou.Framework.DnsServer.Protocol.Records;

/// <summary>Represents a DNS SVCB or HTTPS (service binding) record (RFC 9460).</summary>
public sealed class DnsSvcbRecordData : DnsResourceRecordData
{
    /// <summary>Gets or sets the priority (0 for alias mode).</summary>
    public ushort Priority { get; set; }

    /// <summary>Gets or sets the target name.</summary>
    public string TargetName { get; set; } = "";

    /// <summary>Gets or sets the service parameters.</summary>
    public IReadOnlyList<DnsSvcParam> Parameters { get; set; } = [];
}
