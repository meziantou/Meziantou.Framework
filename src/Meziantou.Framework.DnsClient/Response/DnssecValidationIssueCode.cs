namespace Meziantou.Framework.DnsClient.Response;

/// <summary>Specifies DNSSEC validation issue categories.</summary>
public enum DnssecValidationIssueCode
{
    /// <summary>No issue was reported.</summary>
    None,

    /// <summary>The response did not contain a question to validate.</summary>
    MissingQuestion,

    /// <summary>The response was truncated.</summary>
    TruncatedResponse,

    /// <summary>The response does not contain the records required for validation.</summary>
    MissingRecord,

    /// <summary>No matching RRSIG record was found.</summary>
    MissingRrsig,

    /// <summary>No matching DNSKEY record was found.</summary>
    MissingDnskey,

    /// <summary>No matching DS record or trust anchor was found.</summary>
    MissingDs,

    /// <summary>A DS digest did not match its DNSKEY.</summary>
    DigestMismatch,

    /// <summary>The DNSSEC algorithm is not supported by this implementation.</summary>
    UnsupportedAlgorithm,

    /// <summary>The DS digest algorithm is not supported by this implementation.</summary>
    UnsupportedDigest,

    /// <summary>A signature is not valid yet.</summary>
    SignatureNotYetValid,

    /// <summary>A signature has expired.</summary>
    SignatureExpired,

    /// <summary>A cryptographic signature verification failed.</summary>
    SignatureVerificationFailed,

    /// <summary>An authenticated denial proof was missing or invalid.</summary>
    InvalidDenialProof,

    /// <summary>The DNSSEC trust chain could not be completed.</summary>
    TrustChainIncomplete,

    /// <summary>The response is not valid DNSSEC data.</summary>
    InvalidData,
}
