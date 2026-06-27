namespace Meziantou.Framework.DnsClient.Response;

/// <summary>Specifies the result of local DNSSEC validation.</summary>
public enum DnssecValidationStatus
{
    /// <summary>The response was not locally validated.</summary>
    NotValidated,

    /// <summary>The response was validated successfully.</summary>
    Secure,

    /// <summary>The response belongs to an authenticated unsigned delegation.</summary>
    Insecure,

    /// <summary>The response failed DNSSEC validation.</summary>
    Bogus,

    /// <summary>The validator could not determine the DNSSEC status.</summary>
    Indeterminate,
}
