namespace Meziantou.Framework.DnsClient.Response;

/// <summary>Represents the result of local DNSSEC validation.</summary>
public sealed class DnssecValidationResult
{
    internal static DnssecValidationResult NotValidatedResult { get; } = new(DnssecValidationStatus.NotValidated, []);

    internal DnssecValidationResult(DnssecValidationStatus status, IReadOnlyList<DnssecValidationIssue> issues)
    {
        Status = status;
        Issues = issues;
    }

    /// <summary>Gets the validation status.</summary>
    public DnssecValidationStatus Status { get; }

    /// <summary>Gets the validation issues.</summary>
    public IReadOnlyList<DnssecValidationIssue> Issues { get; }
}
