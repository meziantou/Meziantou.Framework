namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// Specifies the response code for a <c>$dnsrewrite</c> rule.
/// </summary>
public enum DnsFilterRewriteResponseCode
{
    /// <summary>
    /// No error (NOERROR). The query succeeded and may contain answer records.
    /// </summary>
    NoError,

    /// <summary>
    /// The domain name does not exist (NXDOMAIN).
    /// </summary>
    NameError,

    /// <summary>
    /// The server refuses to answer (REFUSED).
    /// </summary>
    Refused,
}
