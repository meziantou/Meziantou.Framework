using Meziantou.Framework.DnsClient.Query;

namespace Meziantou.Framework.DnsClient.Response;

/// <summary>Represents the header of a DNS response message.</summary>
public sealed class DnsResponseHeader
{
    /// <summary>Gets the query identifier.</summary>
    public ushort Id { get; internal set; }

    /// <summary>Gets a value indicating whether this is a response (true) or query (false).</summary>
    public bool IsResponse { get; internal set; }

    /// <summary>Gets the operation code.</summary>
    public DnsOpCode OpCode { get; internal set; }

    /// <summary>Gets a value indicating whether the responding server is authoritative for the queried domain.</summary>
    public bool IsAuthoritative { get; internal set; }

    /// <summary>Gets a value indicating whether the message was truncated.</summary>
    public bool IsTruncated { get; internal set; }

    /// <summary>Gets a value indicating whether recursion was desired in the query.</summary>
    public bool RecursionDesired { get; internal set; }

    /// <summary>Gets a value indicating whether recursion is available on the server.</summary>
    public bool RecursionAvailable { get; internal set; }

    /// <summary>Gets a value indicating whether the response data has been authenticated by the server (DNSSEC).</summary>
    public bool AuthenticatedData { get; internal set; }

    /// <summary>Gets a value indicating whether checking was disabled (DNSSEC).</summary>
    public bool CheckingDisabled { get; internal set; }

    /// <summary>Gets the response code.</summary>
    public DnsResponseCode ResponseCode { get; internal set; }

    /// <summary>Gets the number of questions.</summary>
    public ushort QuestionCount { get; internal set; }

    /// <summary>Gets the number of answer records.</summary>
    public ushort AnswerCount { get; internal set; }

    /// <summary>Gets the number of authority records.</summary>
    public ushort AuthorityCount { get; internal set; }

    /// <summary>Gets the number of additional records.</summary>
    public ushort AdditionalCount { get; internal set; }
}
