namespace Meziantou.Framework.DnsClient.Query;

/// <summary>
/// Specifies the DNS operation code (RFC 1035).
/// </summary>
[SuppressMessage("Design", "CA1027:Mark enums with FlagsAttribute")]
public enum DnsOpCode : byte
{
    /// <summary>Standard query (RFC 1035).</summary>
    Query = 0,

    /// <summary>Inverse query, obsolete (RFC 3425).</summary>
    IQuery = 1,

    /// <summary>Server status request (RFC 1035).</summary>
    Status = 2,

    /// <summary>Zone change notification (RFC 1996).</summary>
    Notify = 4,

    /// <summary>Dynamic update (RFC 2136).</summary>
    Update = 5,
}
