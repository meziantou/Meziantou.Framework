using System.Collections.Frozen;

namespace Meziantou.Framework.WebPlatform.Urls;

/// <summary>
/// The Public Suffix List algorithm (https://github.com/publicsuffix/list/wiki/Format#formal-algorithm), used to obtain the
/// public suffix (https://url.spec.whatwg.org/#host-public-suffix) and the registrable domain
/// (https://url.spec.whatwg.org/#host-registrable-domain) of a host. The rules are generated from the list (tests/Meziantou.Framework.WebPlatform.SpecData/publicsuffix).
/// </summary>
internal static partial class PublicSuffixList
{
    private static readonly Lazy<Rules> AllRules = new(() => Rules.Parse(RulesData));

    /// <summary>https://url.spec.whatwg.org/#host-public-suffix: null when the host is not a domain.</summary>
    public static string? GetPublicSuffix(UrlHost host)
    {
        if (host.Kind != UrlHostKind.Domain)
            return null;

        var (domain, trailingDot) = SplitTrailingDot(host.Serialized);
        return GetPublicSuffix(domain) + trailingDot;
    }

    /// <summary>https://url.spec.whatwg.org/#host-registrable-domain: null when the host is not a domain or is a public suffix.</summary>
    public static string? GetRegistrableDomain(UrlHost host)
    {
        if (host.Kind != UrlHostKind.Domain)
            return null;

        var (domain, trailingDot) = SplitTrailingDot(host.Serialized);
        var publicSuffix = GetPublicSuffix(domain);
        if (publicSuffix.Length == domain.Length)
            return null;

        // The registrable domain is the public suffix and the label that precedes it
        var labelEnd = domain.Length - publicSuffix.Length - 1;
        var labelStart = domain.LastIndexOf('.', labelEnd - 1, StringComparison.Ordinal) + 1;
        return domain[labelStart..] + trailingDot;
    }

    private static (string Domain, string TrailingDot) SplitTrailingDot(string host)
        => host.Length > 1 && host[^1] == '.' ? (host[..^1], ".") : (host, "");

    /// <summary>
    /// The public suffix of a domain (without a trailing dot): the labels matched by the prevailing rule, which is an
    /// exception rule (without its leftmost label), else the matching rule with the most labels, else <c>*</c>.
    /// </summary>
    private static string GetPublicSuffix(string domain)
    {
        var rules = AllRules.Value;
        var suffixStarts = new List<int> { 0 };
        for (var i = 0; i < domain.Length; i++)
        {
            if (domain[i] == '.')
            {
                suffixStarts.Add(i + 1);
            }
        }

        // An exception rule prevails: the public suffix is the rule without its leftmost label
        foreach (var start in suffixStarts)
        {
            if (rules.Exceptions.Contains(domain[start..]))
                return domain[(domain.IndexOf('.', start, StringComparison.Ordinal) + 1)..];
        }

        // The suffixes are tested from the longest one: the first matching rule has the most labels. A wildcard rule
        // "*.suffix" matches the suffix preceded by any label.
        for (var i = 0; i < suffixStarts.Count; i++)
        {
            var suffix = domain[suffixStarts[i]..];
            if (rules.Normal.Contains(suffix))
                return suffix;

            if (i + 1 < suffixStarts.Count && rules.Wildcards.Contains(domain[suffixStarts[i + 1]..]))
                return suffix;
        }

        // The default rule "*": the last label
        return domain[suffixStarts[^1]..];
    }

    private sealed class Rules
    {
        public required FrozenSet<string> Normal { get; init; }

        /// <summary>The suffixes of the wildcard rules (without the leading "*.").</summary>
        public required FrozenSet<string> Wildcards { get; init; }

        /// <summary>The exception rules (without the leading "!").</summary>
        public required FrozenSet<string> Exceptions { get; init; }

        public static Rules Parse(string data)
        {
            var normal = new List<string>();
            var wildcards = new List<string>();
            var exceptions = new List<string>();
            foreach (var rule in data.Split(' '))
            {
                if (rule.StartsWith('!', StringComparison.Ordinal))
                {
                    exceptions.Add(rule[1..]);
                }
                else if (rule.StartsWith("*.", StringComparison.Ordinal))
                {
                    wildcards.Add(rule[2..]);
                }
                else
                {
                    normal.Add(rule);
                }
            }

            return new Rules
            {
                Normal = normal.ToFrozenSet(StringComparer.Ordinal),
                Wildcards = wildcards.ToFrozenSet(StringComparer.Ordinal),
                Exceptions = exceptions.ToFrozenSet(StringComparer.Ordinal),
            };
        }
    }
}
