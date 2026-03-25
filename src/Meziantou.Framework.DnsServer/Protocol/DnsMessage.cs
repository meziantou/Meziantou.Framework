namespace Meziantou.Framework.DnsServer.Protocol;

/// <summary>
/// Represents a unified DNS message used for both incoming queries and outgoing responses.
/// </summary>
public sealed class DnsMessage
{
    /// <summary>Gets or sets the message identifier.</summary>
    public ushort Id { get; set; }

    /// <summary>Gets or sets a value indicating whether this message is a response.</summary>
    public bool IsResponse { get; set; }

    /// <summary>Gets or sets the operation code.</summary>
    public DnsOpCode OpCode { get; set; }

    /// <summary>Gets or sets a value indicating whether this is an authoritative answer.</summary>
    public bool IsAuthoritative { get; set; }

    /// <summary>Gets or sets a value indicating whether the message was truncated.</summary>
    public bool IsTruncated { get; set; }

    /// <summary>Gets or sets a value indicating whether recursion is desired.</summary>
    public bool RecursionDesired { get; set; }

    /// <summary>Gets or sets a value indicating whether recursion is available.</summary>
    public bool RecursionAvailable { get; set; }

    /// <summary>Gets or sets a value indicating whether the data has been authenticated (DNSSEC).</summary>
    public bool AuthenticatedData { get; set; }

    /// <summary>Gets or sets a value indicating whether checking is disabled (DNSSEC).</summary>
    public bool CheckingDisabled { get; set; }

    /// <summary>Gets or sets the response code.</summary>
    public DnsResponseCode ResponseCode { get; set; }

    /// <summary>Gets the question section.</summary>
    public IList<DnsQuestion> Questions { get; } = new List<DnsQuestion>();

    /// <summary>Gets the answer section.</summary>
    public IList<DnsResourceRecord> Answers { get; } = new List<DnsResourceRecord>();

    /// <summary>Gets the authority section.</summary>
    public IList<DnsResourceRecord> Authorities { get; } = new List<DnsResourceRecord>();

    /// <summary>Gets the additional records section.</summary>
    public IList<DnsResourceRecord> AdditionalRecords { get; } = new List<DnsResourceRecord>();

    /// <summary>Gets or sets the EDNS options. Null if EDNS is not used.</summary>
    public DnsEdnsOptions? EdnsOptions { get; set; }
}
