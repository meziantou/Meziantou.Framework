namespace Meziantou.Framework.DnsClient.Query;

/// <summary>
/// Specifies DNS query classes as defined in RFC 1035.
/// </summary>
public enum DnsQueryClass : ushort
{
    /// <summary>Internet (RFC 1035).</summary>
    IN = 1,

    /// <summary>CSNET, obsolete (RFC 1035).</summary>
    CS = 2,

    /// <summary>CHAOS (RFC 1035).</summary>
    CH = 3,

    /// <summary>Hesiod (RFC 1035).</summary>
    HS = 4,

    /// <summary>None, used in DNS UPDATE (RFC 2136).</summary>
    NONE = 254,

    /// <summary>Any class (RFC 1035).</summary>
    ANY = 255,
}
