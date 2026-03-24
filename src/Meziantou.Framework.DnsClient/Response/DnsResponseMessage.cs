using Meziantou.Framework.DnsClient.Query;

namespace Meziantou.Framework.DnsClient.Response;

/// <summary>
/// Represents a DNS response message received from a DNS server.
/// </summary>
public sealed class DnsResponseMessage
{
    internal DnsResponseMessage(DnsResponseHeader header)
    {
        Header = header;
    }

    /// <summary>Gets the response header containing flags and counts.</summary>
    public DnsResponseHeader Header { get; }

    /// <summary>Gets the list of questions echoed back from the server.</summary>
    public IReadOnlyList<DnsQuestion> Questions { get; internal set; } = [];

    /// <summary>Gets the answer resource records.</summary>
    public IReadOnlyList<DnsRecord> Answers { get; internal set; } = [];

    /// <summary>Gets the authority resource records.</summary>
    public IReadOnlyList<DnsRecord> Authorities { get; internal set; } = [];

    /// <summary>Gets the additional resource records.</summary>
    public IReadOnlyList<DnsRecord> AdditionalRecords { get; internal set; } = [];
}
