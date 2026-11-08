using System.Collections.Frozen;

namespace Meziantou.Framework.WebPlatform.Urls;

/// <summary>
/// The Public Suffix List algorithm (https://github.com/publicsuffix/list/wiki/Format#formal-algorithm), used to obtain the
/// public suffix (https://url.spec.whatwg.org/#host-public-suffix) and the registrable domain
/// (https://url.spec.whatwg.org/#host-registrable-domain) of a host. The rules are generated from the list (tests/Meziantou.Framework.WebPlatform.SpecData/publicsuffix).
/// </summary>
internal static partial class PublicSuffixList
{
    private static readonly Lazy<Rules> AllRules = new(Rules.Create);

    /// <summary>https://url.spec.whatwg.org/#host-public-suffix: null when the host is not a domain.</summary>
    public static string? GetPublicSuffix(UrlHost host)
    {
        if (host.Kind != UrlHostKind.Domain)
            return null;

        // The public suffix keeps the trailing dot of the host
        var serialized = host.Serialized;
        return serialized[GetPublicSuffixStart(GetDomain(serialized))..];
    }

    /// <summary>https://url.spec.whatwg.org/#host-registrable-domain: null when the host is not a domain or is a public suffix.</summary>
    public static string? GetRegistrableDomain(UrlHost host)
    {
        if (host.Kind != UrlHostKind.Domain)
            return null;

        var serialized = host.Serialized;
        var publicSuffixStart = GetPublicSuffixStart(GetDomain(serialized));
        if (publicSuffixStart == 0)
            return null;

        // The registrable domain is the public suffix and the label that precedes it
        var labelStart = serialized.AsSpan(0, publicSuffixStart - 1).LastIndexOf('.') + 1;
        return serialized[labelStart..];
    }

    /// <summary>The host without its trailing dot.</summary>
    private static ReadOnlySpan<char> GetDomain(string host)
        => host.Length > 1 && host[^1] == '.' ? host.AsSpan(0, host.Length - 1) : host;

    /// <summary>The index of the label that follows the one starting at <paramref name="start"/>, or -1 for the last label.</summary>
    private static int GetNextLabelStart(ReadOnlySpan<char> domain, int start)
    {
        var dot = domain[start..].IndexOf('.');
        return dot < 0 ? -1 : start + dot + 1;
    }

    /// <summary>
    /// The start of the public suffix of a domain (without a trailing dot): the labels matched by the prevailing rule, which is
    /// an exception rule (without its leftmost label), else the matching rule with the most labels, else <c>*</c>.
    /// </summary>
    private static int GetPublicSuffixStart(ReadOnlySpan<char> domain)
    {
        var rules = AllRules.Value;

        // An exception rule prevails: the public suffix is the rule without its leftmost label
        for (var start = 0; start >= 0; start = GetNextLabelStart(domain, start))
        {
            if (rules.Exceptions.Contains(domain[start..]))
                return GetNextLabelStart(domain, start);
        }

        // The suffixes are tested from the longest one: the first matching rule has the most labels. A wildcard rule
        // "*.suffix" matches the suffix preceded by any label.
        var suffixStart = 0;
        while (true)
        {
            if (rules.Normal.Contains(domain[suffixStart..]))
                return suffixStart;

            var nextStart = GetNextLabelStart(domain, suffixStart);
            if (nextStart < 0)
                return suffixStart; // The default rule "*": the last label

            if (rules.Wildcards.Contains(domain[nextStart..]))
                return suffixStart;

            suffixStart = nextStart;
        }
    }

    private sealed class Rules
    {
        public required FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> Normal { get; init; }

        /// <summary>The suffixes of the wildcard rules (without the leading "*.").</summary>
        public required FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> Wildcards { get; init; }

        /// <summary>The exception rules (without the leading "!").</summary>
        public required FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> Exceptions { get; init; }

        public static Rules Create()
        {
            return new Rules
            {
                Normal = Parse(NormalRules),
                Wildcards = Parse(WildcardRules),
                Exceptions = Parse(ExceptionRules),
            };
        }

        private static FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> Parse(string data)
        {
            return data.Split([' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .ToFrozenSet(StringComparer.Ordinal)
                .GetAlternateLookup<ReadOnlySpan<char>>();
        }
    }
}
