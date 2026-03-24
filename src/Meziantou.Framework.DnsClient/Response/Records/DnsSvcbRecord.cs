namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>
/// Represents a DNS SVCB or HTTPS record for service binding (RFC 9460).
/// Used for both SVCB (type 64) and HTTPS (type 65) records.
/// </summary>
public sealed class DnsSvcbRecord : DnsRecord
{
    /// <summary>Gets the priority. 0 indicates an alias form.</summary>
    public ushort Priority { get; internal set; }

    /// <summary>Gets the target name. "." means the owner name should be used.</summary>
    public string TargetName { get; internal set; } = "";

    /// <summary>Gets the service parameters as key-value pairs.</summary>
    public IReadOnlyList<DnsSvcParam> Parameters { get; internal set; } = [];
}
