using Meziantou.Framework.DnsClient.Query;

namespace Meziantou.Framework.DnsClient.Response;

/// <summary>Represents one DNSSEC validation issue.</summary>
public sealed class DnssecValidationIssue
{
    internal DnssecValidationIssue(DnssecValidationIssueCode code, string message, string? name = null, DnsQueryType? recordType = null)
    {
        Code = code;
        Message = message;
        Name = name;
        RecordType = recordType;
    }

    /// <summary>Gets the issue code.</summary>
    public DnssecValidationIssueCode Code { get; }

    /// <summary>Gets the human-readable issue message.</summary>
    public string Message { get; }

    /// <summary>Gets the DNS name associated with the issue, when known.</summary>
    public string? Name { get; }

    /// <summary>Gets the DNS record type associated with the issue, when known.</summary>
    public DnsQueryType? RecordType { get; }
}
