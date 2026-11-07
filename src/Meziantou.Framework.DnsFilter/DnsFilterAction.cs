namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// Specifies the action determined by DNS filter evaluation.
/// </summary>
public enum DnsFilterAction
{
    /// <summary>
    /// No rule matched the query. This is the default value, so an unmatched
    /// result never appears to request blocking.
    /// </summary>
    None = 0,

    /// <summary>
    /// The rule blocks the matching DNS query.
    /// </summary>
    Block,

    /// <summary>
    /// The rule explicitly allows the matching DNS query (exception/allowlist rule).
    /// </summary>
    Allow,

    /// <summary>
    /// The matching rule replaces the DNS response instead of simply refusing it.
    /// The directive is available on <see cref="DnsFilterResult.Rewrite"/>.
    /// </summary>
    /// <remarks>
    /// This value is only ever produced on a <see cref="DnsFilterResult"/>.
    /// <see cref="DnsFilterRule.Action"/> is always <see cref="Block"/> or <see cref="Allow"/>.
    /// </remarks>
    Rewrite,
}
