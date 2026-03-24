namespace Meziantou.Framework.DnsServer.Protocol;

/// <summary>Specifies DNS response codes as defined in various RFCs.</summary>
[SuppressMessage("Design", "CA1027:Mark enums with FlagsAttribute")]
public enum DnsResponseCode : ushort
{
    /// <summary>No error condition (RFC 1035).</summary>
    NoError = 0,

    /// <summary>Format error: the name server was unable to interpret the query (RFC 1035).</summary>
    FormError = 1,

    /// <summary>Server failure: the name server was unable to process the query (RFC 1035).</summary>
    ServerFailure = 2,

    /// <summary>Name error: the domain name referenced in the query does not exist (RFC 1035).</summary>
    NameError = 3,

    /// <summary>Not implemented: the name server does not support the requested operation (RFC 1035).</summary>
    NotImplemented = 4,

    /// <summary>Refused: the name server refuses to perform the operation (RFC 1035).</summary>
    Refused = 5,

    /// <summary>Name exists when it should not (RFC 2136).</summary>
    YxDomain = 6,

    /// <summary>RR set exists when it should not (RFC 2136).</summary>
    YxRRSet = 7,

    /// <summary>RR set that should exist does not (RFC 2136).</summary>
    NxRRSet = 8,

    /// <summary>Server not authoritative for zone / not authorized (RFC 2136).</summary>
    NotAuthoritative = 9,

    /// <summary>Name not contained in zone (RFC 2136).</summary>
    NotZone = 10,

    /// <summary>Bad OPT version (RFC 6891).</summary>
    BadVersion = 16,

    /// <summary>Key not recognized (RFC 2845).</summary>
    BadKey = 17,

    /// <summary>Signature out of time window (RFC 2845).</summary>
    BadTime = 18,

    /// <summary>Bad TKEY mode (RFC 2930).</summary>
    BadMode = 19,

    /// <summary>Duplicate key name (RFC 2930).</summary>
    BadName = 20,

    /// <summary>Algorithm not supported (RFC 2930).</summary>
    BadAlgorithm = 21,

    /// <summary>Bad truncation (RFC 4635).</summary>
    BadTruncation = 22,

    /// <summary>Bad/missing server cookie (RFC 7873).</summary>
    BadCookie = 23,
}
