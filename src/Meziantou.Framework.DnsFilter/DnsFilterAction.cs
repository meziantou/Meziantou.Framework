namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// Specifies the action a DNS filter rule performs when matched.
/// </summary>
public enum DnsFilterAction
{
    /// <summary>
    /// The rule blocks the matching DNS query.
    /// </summary>
    Block,

    /// <summary>
    /// The rule explicitly allows the matching DNS query (exception/allowlist rule).
    /// </summary>
    Allow,
}
