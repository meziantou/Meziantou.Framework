using System.Text.RegularExpressions;

namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// A DNS filter matching engine that evaluates DNS queries against a set of filter rules.
/// Supports efficient exact domain matching, subdomain matching, wildcard, and regex patterns.
/// </summary>
/// <remarks>
/// <see cref="Evaluate"/> is safe to call concurrently, and <see cref="Reload"/> swaps the rule set
/// atomically. <see cref="DnsFilterRuleSet"/> itself is not thread-safe for concurrent mutation;
/// the engine takes a snapshot when it builds, so mutating a rule set afterwards never affects an
/// engine already built from it.
/// </remarks>
public sealed class DnsFilterEngine
{
    private volatile FilterData _data;

    /// <summary>
    /// Initializes a new instance of the <see cref="DnsFilterEngine"/> class with the specified rule set.
    /// </summary>
    /// <param name="ruleSet">The rule set to use for matching.</param>
    public DnsFilterEngine(DnsFilterRuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        _data = BuildFilterData(ruleSet);
    }

    /// <summary>
    /// Atomically replaces the current rule set with a new one. Thread-safe with respect to
    /// concurrent <see cref="Evaluate"/> calls.
    /// </summary>
    /// <param name="ruleSet">The new rule set.</param>
    public void Reload(DnsFilterRuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        _data = BuildFilterData(ruleSet);
    }

    /// <summary>
    /// Evaluates a DNS query against the filter rules.
    /// </summary>
    /// <param name="domain">The queried domain name. Internationalized names are converted to their
    /// punycode form before matching.</param>
    /// <param name="queryType">The DNS query type. Pass the raw QTYPE cast to
    /// <see cref="DnsFilterQueryType"/> even when it is not a named member.</param>
    /// <param name="client">Optional client information for <c>$client</c> and <c>$ctag</c> matching.
    /// When a dimension is not supplied, rules scoped on it do not match.</param>
    /// <returns>A <see cref="DnsFilterResult"/> indicating whether the query is blocked, allowed,
    /// rewritten, or unmatched. This method does not throw for malformed or hostile input.</returns>
    public DnsFilterResult Evaluate(string domain, DnsFilterQueryType queryType = DnsFilterQueryType.A, DnsClientInfo client = default)
    {
        ArgumentNullException.ThrowIfNull(domain);

        if (!DnsDomainName.TryNormalize(domain, out var name))
            return DnsFilterResult.NotMatched;

        var data = _data;
        var state = default(MatchState);

        // 1. Exact domain match.
        if (data.ExactDomainRules.Count > 0 && data.ExactDomainRules.TryGetValue(name, out var exact))
        {
            ConsiderBucket(exact, ref state, name, queryType, client, requirePatternMatch: false);
        }

        // 2. Suffix (subdomain) match: the name itself and each of its parents.
        if (data.SuffixDomainRules.Count > 0 || data.SuffixPatternRules.Count > 0)
        {
            var suffixLookup = data.SuffixDomainRules.GetAlternateLookup<ReadOnlySpan<char>>();
            var patternLookup = data.SuffixPatternRules.GetAlternateLookup<ReadOnlySpan<char>>();

            var current = name.AsSpan();
            while (true)
            {
                if (suffixLookup.TryGetValue(current, out var suffixRules))
                {
                    ConsiderBucket(suffixRules, ref state, name, queryType, client, requirePatternMatch: false);
                }

                if (patternLookup.TryGetValue(current, out var patternRules))
                {
                    ConsiderBucket(patternRules, ref state, name, queryType, client, requirePatternMatch: true);
                }

                var dotIndex = current.IndexOf('.');
                if (dotIndex < 0)
                    break;

                current = current[(dotIndex + 1)..];
            }
        }

        // 3. Pattern rules with no indexable suffix, gated by a literal prefilter.
        foreach (var rule in data.PatternRules)
        {
            Consider(rule, ref state, name, queryType, client, requirePatternMatch: true);
        }

        if (state.Best is null)
            return DnsFilterResult.NotMatched;

        return state.Best.Action is DnsFilterAction.Allow
            ? DnsFilterResult.Allowed(state.Best)
            : DnsFilterResult.Blocked(state.Best);
    }

    private static void ConsiderBucket(object bucket, ref MatchState state, string name, DnsFilterQueryType queryType, in DnsClientInfo client, bool requirePatternMatch)
    {
        if (bucket is DnsFilterRule single)
        {
            Consider(single, ref state, name, queryType, client, requirePatternMatch);
            return;
        }

        foreach (var rule in (List<DnsFilterRule>)bucket)
        {
            Consider(rule, ref state, name, queryType, client, requirePatternMatch);
        }
    }

    private static void Consider(DnsFilterRule rule, ref MatchState state, string name, DnsFilterQueryType queryType, in DnsClientInfo client, bool requirePatternMatch)
    {
        // Cheap ordering checks first: a rule that cannot beat the incumbent needs no matching work.
        var rank = GetRank(rule);
        if (rank < state.Rank)
            return;

        var specificity = GetSpecificity(rule);
        if (rank == state.Rank && specificity <= state.Specificity)
            return;

        if (requirePatternMatch && !MatchesPattern(rule, name))
            return;

        if (!MatchesDnsType(rule, queryType))
            return;

        if (IsExcludedByDenyAllow(rule, name))
            return;

        if (!MatchesClient(rule, client))
            return;

        if (!MatchesCtag(rule, client))
            return;

        state.Best = rule;
        state.Rank = rank;
        state.Specificity = specificity;
    }

    private struct MatchState
    {
        public DnsFilterRule? Best;
        public int Rank;
        public int Specificity;
    }

    /// <summary>
    /// Priority of a rule, highest wins. An <c>$important</c> exception outranks an
    /// <c>$important</c> block, which is what makes <c>@@…$important</c> usable as an override
    /// against a blocklist the operator does not control.
    /// </summary>
    private static int GetRank(DnsFilterRule rule) => (rule.IsImportant, rule.Action) switch
    {
        (true, DnsFilterAction.Allow) => 4,
        (true, _) => 3,
        (false, DnsFilterAction.Allow) => 2,
        _ => 1,
    };

    /// <summary>
    /// Tie-break within a priority level: a rewrite beats a plain block, then the more specific
    /// rule wins. Without this the winner would be whichever rule the index happened to yield first.
    /// </summary>
    private static int GetSpecificity(DnsFilterRule rule)
    {
        var score = rule.ExactDomain is not null ? 1_000_000
            : rule.DomainSuffix is not null ? 1_000 + rule.DomainSuffix.Length
            : 1;

        if (rule.Rewrite is not null)
        {
            score += 10_000_000;
        }

        return score;
    }

    private static bool MatchesPattern(DnsFilterRule rule, string domain)
    {
        if (rule.RequiredLiterals is { } literals)
        {
            foreach (var literal in literals)
            {
                if (!domain.Contains(literal, StringComparison.Ordinal))
                    return false;
            }
        }

        try
        {
            return rule.Pattern!.IsMatch(domain);
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological pattern in a third-party list must not take the resolver down. Treat it
            // as a non-match; the rule is effectively inert for this query.
            return false;
        }
    }

    private static bool MatchesDnsType(DnsFilterRule rule, DnsFilterQueryType queryType)
    {
        if (rule.AllowedDnsTypes is not null && !rule.AllowedDnsTypes.Contains(queryType))
            return false;

        if (rule.ExcludedDnsTypes is not null && rule.ExcludedDnsTypes.Contains(queryType))
            return false;

        return true;
    }

    private static bool IsExcludedByDenyAllow(DnsFilterRule rule, string domain)
    {
        if (rule.DenyAllowDomains is null)
            return false;

        foreach (var allowed in rule.DenyAllowDomains)
        {
            if (domain.Equals(allowed, StringComparison.Ordinal) ||
                (domain.Length > allowed.Length &&
                 domain[domain.Length - allowed.Length - 1] is '.' &&
                 domain.EndsWith(allowed, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesClient(DnsFilterRule rule, DnsClientInfo client)
    {
        if (rule.ClientSpecs is null)
            return true;

        var hasInclusions = false;
        var matchedInclusion = false;

        foreach (var spec in rule.ClientSpecs)
        {
            if (spec.IsExclusion)
            {
                // The caller may not have supplied the dimension this spec is written against. An
                // exclusion that cannot be evaluated must not be assumed satisfied, or a
                // "block for everyone except X" rule would end up applying to X itself.
                if (!CanEvaluate(spec, client) || MatchesClientSpec(spec, client))
                    return false;
            }
            else
            {
                hasInclusions = true;
                if (CanEvaluate(spec, client) && MatchesClientSpec(spec, client))
                {
                    matchedInclusion = true;
                }
            }
        }

        return !hasInclusions || matchedInclusion;
    }

    private static bool CanEvaluate(DnsFilterClientSpec spec, DnsClientInfo client)
    {
        if (spec.Address is not null || spec.Network is not null)
            return client.Address is not null;

        return client.Name is not null;
    }

    private static bool MatchesClientSpec(DnsFilterClientSpec spec, DnsClientInfo client)
    {
        if (spec.Address is not null)
            return spec.Address.Equals(client.Address);

        if (spec.Network is not null)
            return spec.Network.Value.Contains(client.Address!);

        return spec.Name is not null && spec.Name.Equals(client.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesCtag(DnsFilterRule rule, DnsClientInfo client)
    {
        if (rule.TagSpec is null)
            return true;

        if (client.Tags is null || client.Tags.Count is 0)
            return false;

        // Exclusions and inclusions are both requirements, not alternatives.
        if (rule.TagSpec.ExcludedTags is not null)
        {
            foreach (var tag in rule.TagSpec.ExcludedTags)
            {
                if (ContainsTag(client.Tags, tag))
                    return false;
            }
        }

        if (rule.TagSpec.IncludedTags is not null)
        {
            foreach (var tag in rule.TagSpec.IncludedTags)
            {
                if (ContainsTag(client.Tags, tag))
                    return true;
            }

            return false;
        }

        return true;
    }

    private static bool ContainsTag(IReadOnlyList<string> tags, string tag)
    {
        foreach (var candidate in tags)
        {
            if (candidate.Equals(tag, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static FilterData BuildFilterData(DnsFilterRuleSet ruleSet)
    {
        var rules = ruleSet.ToArray();

        // Pass 1: collect the identities disabled by $badfilter, so disabled rules are never
        // indexed at all and the query path does not have to check for them.
        HashSet<string>? badFilterKeys = null;
        foreach (var rule in rules)
        {
            if (rule.IsBadFilter && rule.BadFilterKey is not null)
            {
                badFilterKeys ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                badFilterKeys.Add(rule.BadFilterKey);
            }
        }

        var exactDomainRules = new Dictionary<string, object>(rules.Length, StringComparer.OrdinalIgnoreCase);
        var suffixDomainRules = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var suffixPatternRules = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var patternRules = new List<DnsFilterRule>();

        foreach (var rule in rules)
        {
            if (rule.IsBadFilter)
                continue;

            if (badFilterKeys is not null && rule.BadFilterKey is not null && badFilterKeys.Contains(rule.BadFilterKey))
                continue;

            if (rule.ExactDomain is not null)
            {
                AddToIndex(exactDomainRules, rule.ExactDomain, rule);
            }
            else if (rule.DomainSuffix is not null)
            {
                AddToIndex(suffixDomainRules, rule.DomainSuffix, rule);
            }
            else if (rule.Pattern is not null)
            {
                // A wildcard rule anchored on a concrete multi-label suffix can be reached through
                // the parent-domain walk instead of being tested against every single query.
                if (rule.PatternSuffix is not null)
                {
                    AddToIndex(suffixPatternRules, rule.PatternSuffix, rule);
                }
                else
                {
                    patternRules.Add(rule);
                }
            }
        }

        exactDomainRules.TrimExcess();

        return new FilterData
        {
            ExactDomainRules = exactDomainRules,
            SuffixDomainRules = suffixDomainRules,
            SuffixPatternRules = suffixPatternRules,
            PatternRules = patternRules,
        };
    }

    /// <summary>
    /// Stores the rule directly for the ~76% of keys that hold exactly one, promoting to a list
    /// only on the second insert.
    /// </summary>
    private static void AddToIndex(Dictionary<string, object> index, string key, DnsFilterRule rule)
    {
        if (!index.TryGetValue(key, out var existing))
        {
            index[key] = rule;
            return;
        }

        if (existing is List<DnsFilterRule> list)
        {
            list.Add(rule);
            return;
        }

        index[key] = new List<DnsFilterRule> { (DnsFilterRule)existing, rule };
    }

    private sealed class FilterData
    {
        public required Dictionary<string, object> ExactDomainRules { get; init; }
        public required Dictionary<string, object> SuffixDomainRules { get; init; }
        public required Dictionary<string, object> SuffixPatternRules { get; init; }
        public required List<DnsFilterRule> PatternRules { get; init; }
    }
}
