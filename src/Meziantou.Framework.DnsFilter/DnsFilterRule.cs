using System.Text.RegularExpressions;

namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// Represents a single parsed DNS filter rule.
/// </summary>
public sealed class DnsFilterRule
{
    internal DnsFilterRule()
    {
    }

    /// <summary>
    /// Gets the original text of the rule as it appeared in the filter list.
    /// </summary>
    public required string OriginalText { get; init; }

    /// <summary>
    /// Gets the action to perform when this rule matches.
    /// </summary>
    public required DnsFilterAction Action { get; init; }

    /// <summary>
    /// Gets a value indicating whether this rule has the <c>$important</c> modifier,
    /// which elevates its priority above normal rules and exceptions.
    /// </summary>
    public bool IsImportant { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is a <c>$badfilter</c> rule that
    /// disables another rule matching its text.
    /// </summary>
    public bool IsBadFilter { get; init; }

    /// <summary>
    /// Gets the exact domain to match, if the rule is an exact domain match.
    /// </summary>
    internal string? ExactDomain { get; init; }

    /// <summary>
    /// Gets the domain suffix to match (for <c>||domain^</c> style rules that match the domain and all subdomains).
    /// </summary>
    internal string? DomainSuffix { get; init; }

    /// <summary>
    /// Gets the compiled regular expression for regex-based rules.
    /// </summary>
    internal Regex? Pattern { get; init; }

    /// <summary>
    /// Gets the set of DNS query types this rule applies to (from <c>$dnstype</c> modifier).
    /// <see langword="null"/> means the rule applies to all query types.
    /// </summary>
    public IReadOnlyCollection<DnsFilterQueryType>? AllowedDnsTypes { get; init; }

    /// <summary>
    /// Gets the set of DNS query types this rule does not apply to (from <c>$dnstype=~</c> modifier).
    /// <see langword="null"/> means no types are excluded.
    /// </summary>
    public IReadOnlyCollection<DnsFilterQueryType>? ExcludedDnsTypes { get; init; }

    /// <summary>
    /// Gets the set of domains excluded from this rule (from <c>$denyallow</c> modifier).
    /// </summary>
    public IReadOnlyCollection<string>? DenyAllowDomains { get; init; }

    /// <summary>
    /// Gets the rewrite directive for this rule (from <c>$dnsrewrite</c> modifier).
    /// </summary>
    public DnsFilterRewriteRule? Rewrite { get; init; }

    /// <summary>
    /// Gets the client specifications this rule applies to (from <c>$client</c> modifier).
    /// Entries can be IP addresses, CIDR ranges, or client names. Prefixed with <c>~</c> for exclusion.
    /// </summary>
    internal IReadOnlyList<DnsFilterClientSpec>? ClientSpecs { get; init; }

    /// <summary>
    /// Gets the client tags this rule applies to (from <c>$ctag</c> modifier).
    /// </summary>
    internal DnsFilterTagSpec? TagSpec { get; init; }
}
