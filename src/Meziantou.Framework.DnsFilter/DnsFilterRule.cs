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
    /// <remarks>
    /// This is provided for diagnostics only. It is not the rule's identity: use
    /// <see cref="ExactDomain"/>, <see cref="DomainSuffix"/> or <see cref="PatternText"/> to
    /// report what the rule actually matches.
    /// </remarks>
    public required string OriginalText { get; init; }

    /// <summary>
    /// Gets the action to perform when this rule matches. Always
    /// <see cref="DnsFilterAction.Block"/> or <see cref="DnsFilterAction.Allow"/>.
    /// </summary>
    public required DnsFilterAction Action { get; init; }

    /// <summary>
    /// Gets a value indicating whether this rule has the <c>$important</c> modifier,
    /// which elevates its priority above normal rules and exceptions.
    /// </summary>
    public bool IsImportant { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is a <c>$badfilter</c> rule that
    /// disables another rule matching its pattern and modifiers.
    /// </summary>
    public bool IsBadFilter { get; init; }

    /// <summary>
    /// Gets the exact domain this rule matches, or <see langword="null"/> if it does not match by exact domain.
    /// </summary>
    public string? ExactDomain { get; init; }

    /// <summary>
    /// Gets the domain suffix this rule matches (for <c>||domain^</c> style rules that match the
    /// domain and all its subdomains), or <see langword="null"/>.
    /// </summary>
    public string? DomainSuffix { get; init; }

    /// <summary>
    /// Gets the regular expression source this rule matches with, or <see langword="null"/>.
    /// Set for <c>/regex/</c> rules and for patterns containing <c>*</c>.
    /// </summary>
    public string? PatternText => Pattern?.ToString();

    /// <summary>
    /// Gets the compiled regular expression for pattern-based rules.
    /// </summary>
    internal Regex? Pattern { get; init; }

    /// <summary>
    /// Gets literal substrings that a matching domain must all contain, used to skip the regex
    /// entirely for the overwhelming majority of queries. A substring check costs a few nanoseconds
    /// against roughly 150ns for a non-backtracking regex, so testing several is still far cheaper
    /// than running one. <see langword="null"/> when no useful literal could be extracted.
    /// </summary>
    internal string[]? RequiredLiterals { get; init; }

    /// <summary>
    /// Gets the concrete multi-label domain suffix a wildcard pattern is anchored on
    /// (<c>aliyuncs.com</c> for <c>||ad-host-backup-*.aliyuncs.com^</c>), which lets the engine
    /// reach the rule through the parent-domain walk instead of testing it against every query.
    /// <see langword="null"/> when no such suffix exists.
    /// </summary>
    internal string? PatternSuffix { get; init; }

    /// <summary>
    /// Gets the identity of this rule for <c>$badfilter</c> purposes: its pattern and its modifier
    /// set, normalized and order-independent. Two rules that differ only in casing, whitespace or
    /// modifier order share a key.
    /// </summary>
    internal string? BadFilterKey { get; init; }

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

    /// <summary>
    /// Creates a rule blocking a single domain, and optionally all of its subdomains.
    /// </summary>
    /// <param name="domain">The domain to block.</param>
    /// <param name="includeSubdomains">When <see langword="true"/>, subdomains are blocked too.</param>
    /// <exception cref="ArgumentException"><paramref name="domain"/> is not a valid domain name.</exception>
    public static DnsFilterRule CreateBlock(string domain, bool includeSubdomains = false)
        => Create(domain, includeSubdomains, DnsFilterAction.Block);

    /// <summary>
    /// Creates an exception rule allowing a single domain, and optionally all of its subdomains.
    /// </summary>
    /// <param name="domain">The domain to allow.</param>
    /// <param name="includeSubdomains">When <see langword="true"/>, subdomains are allowed too.</param>
    /// <exception cref="ArgumentException"><paramref name="domain"/> is not a valid domain name.</exception>
    public static DnsFilterRule CreateAllow(string domain, bool includeSubdomains = false)
        => Create(domain, includeSubdomains, DnsFilterAction.Allow);

    private static DnsFilterRule Create(string domain, bool includeSubdomains, DnsFilterAction action)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (!DnsDomainName.TryNormalize(domain, out var normalized))
            throw new ArgumentException($"'{domain}' is not a valid domain name.", nameof(domain));

        var text = (action is DnsFilterAction.Allow ? "@@" : "") + (includeSubdomains ? "||" + normalized + "^" : normalized);

        return new DnsFilterRule
        {
            OriginalText = text,
            Action = action,
            ExactDomain = includeSubdomains ? null : normalized,
            DomainSuffix = includeSubdomains ? normalized : null,
            BadFilterKey = text,
        };
    }
}
