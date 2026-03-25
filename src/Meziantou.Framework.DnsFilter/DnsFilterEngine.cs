using System.Net;

namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// A DNS filter matching engine that evaluates DNS queries against a set of filter rules.
/// Supports efficient exact domain matching, subdomain matching, wildcard, and regex patterns.
/// Thread-safe for concurrent query evaluation; supports atomic rule-set replacement via <see cref="Reload"/>.
/// </summary>
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
    /// Atomically replaces the current rule set with a new one. Thread-safe.
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
    /// <param name="domain">The queried domain name.</param>
    /// <param name="queryType">The DNS query type.</param>
    /// <param name="client">Optional client information for <c>$client</c> and <c>$ctag</c> matching.</param>
    /// <returns>A <see cref="DnsFilterResult"/> indicating whether the query is blocked, allowed, or unmatched.</returns>
    public DnsFilterResult Evaluate(string domain, DnsFilterQueryType queryType = DnsFilterQueryType.A, DnsClientInfo client = default)
    {
        ArgumentNullException.ThrowIfNull(domain);

        domain = domain.Trim().TrimEnd('.').ToLowerInvariant();
        if (domain.Length == 0)
        {
            return DnsFilterResult.NotMatched;
        }

        var data = _data;
        var candidates = FindCandidateRules(data, domain);

        // Apply filtering pipeline and resolve priority
        DnsFilterRule? bestBlock = null;
        DnsFilterRule? bestAllow = null;
        DnsFilterRule? bestImportantBlock = null;
        DnsFilterRule? bestImportantAllow = null;

        foreach (var rule in candidates)
        {
            // Skip $badfilter rules (they only disable other rules)
            if (rule.IsBadFilter)
                continue;

            // Check if this rule is disabled by a $badfilter
            if (IsDisabledByBadFilter(data, rule))
                continue;

            // Filter by $dnstype
            if (!MatchesDnsType(rule, queryType))
                continue;

            // Filter by $denyallow
            if (IsExcludedByDenyAllow(rule, domain))
                continue;

            // Filter by $client
            if (!MatchesClient(rule, client))
                continue;

            // Filter by $ctag
            if (!MatchesCtag(rule, client))
                continue;

            // Categorize by priority
            if (rule.IsImportant)
            {
                if (rule.Action == DnsFilterAction.Block)
                {
                    bestImportantBlock ??= rule;
                }
                else
                {
                    bestImportantAllow ??= rule;
                }
            }
            else
            {
                if (rule.Action == DnsFilterAction.Block)
                {
                    bestBlock ??= rule;
                }
                else
                {
                    bestAllow ??= rule;
                }
            }
        }

        // Priority resolution:
        // 1. $important block rules beat everything
        // 2. $important allow rules beat normal rules
        // 3. Normal allow (@@) rules beat normal block rules
        // 4. Normal block rules
        if (bestImportantBlock is not null)
        {
            return DnsFilterResult.Blocked(bestImportantBlock);
        }

        if (bestImportantAllow is not null)
        {
            return DnsFilterResult.Allowed(bestImportantAllow);
        }

        if (bestAllow is not null)
        {
            return DnsFilterResult.Allowed(bestAllow);
        }

        if (bestBlock is not null)
        {
            return DnsFilterResult.Blocked(bestBlock);
        }

        return DnsFilterResult.NotMatched;
    }

    private static List<DnsFilterRule> FindCandidateRules(FilterData data, string domain)
    {
        var candidates = new List<DnsFilterRule>();

        // 1. Exact domain match
        if (data.ExactDomainRules.TryGetValue(domain, out var exactRules))
        {
            candidates.AddRange(exactRules);
        }

        // 2. Suffix (subdomain) match: check domain itself and all parent domains
        // For "sub.ads.example.com", check: "sub.ads.example.com", "ads.example.com", "example.com", "com"
        var current = domain;
        while (true)
        {
            if (data.SuffixDomainRules.TryGetValue(current, out var suffixRules))
            {
                candidates.AddRange(suffixRules);
            }

            var dotIndex = current.IndexOf('.', StringComparison.Ordinal);
            if (dotIndex < 0)
                break;

            current = current[(dotIndex + 1)..];
        }

        // 3. Regex/wildcard pattern rules
        foreach (var rule in data.PatternRules)
        {
            if (rule.Pattern!.IsMatch(domain))
            {
                candidates.Add(rule);
            }
        }

        return candidates;
    }

    private static bool MatchesDnsType(DnsFilterRule rule, DnsFilterQueryType queryType)
    {
        if (rule.AllowedDnsTypes is not null)
        {
            return rule.AllowedDnsTypes.Contains(queryType);
        }

        if (rule.ExcludedDnsTypes is not null)
        {
            return !rule.ExcludedDnsTypes.Contains(queryType);
        }

        return true;
    }

    private static bool IsExcludedByDenyAllow(DnsFilterRule rule, string domain)
    {
        if (rule.DenyAllowDomains is null)
            return false;

        foreach (var allowed in rule.DenyAllowDomains)
        {
            if (domain.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
                domain.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase))
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

        // If there are only exclusion specs, the rule matches unless the client matches an exclusion
        var hasInclusions = false;
        var matchedInclusion = false;

        foreach (var spec in rule.ClientSpecs)
        {
            var matches = MatchesClientSpec(spec, client);

            if (spec.IsExclusion)
            {
                if (matches)
                {
                    return false; // Excluded client
                }
            }
            else
            {
                hasInclusions = true;
                if (matches)
                {
                    matchedInclusion = true;
                }
            }
        }

        // If there are inclusion specs, at least one must match
        if (hasInclusions)
        {
            return matchedInclusion;
        }

        return true;
    }

    private static bool MatchesClientSpec(DnsFilterClientSpec spec, DnsClientInfo client)
    {
        if (spec.Address is not null && client.Address is not null)
        {
            return spec.Address.Equals(client.Address);
        }

        if (spec.Network is not null && client.Address is not null)
        {
            return spec.Network.Value.Contains(client.Address);
        }

        if (spec.Name is not null && client.Name is not null)
        {
            return spec.Name.Equals(client.Name, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool MatchesCtag(DnsFilterRule rule, DnsClientInfo client)
    {
        if (rule.TagSpec is null)
            return true;

        // If no tags provided by caller, rules with $ctag never match
        if (client.Tags is null || client.Tags.Count == 0)
            return false;

        if (rule.TagSpec.ExcludedTags is not null)
        {
            foreach (var tag in rule.TagSpec.ExcludedTags)
            {
                if (client.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }

            return true;
        }

        if (rule.TagSpec.IncludedTags is not null)
        {
            foreach (var tag in rule.TagSpec.IncludedTags)
            {
                if (client.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        return true;
    }

    private static bool IsDisabledByBadFilter(FilterData data, DnsFilterRule rule)
    {
        if (data.BadFilterTexts.Count == 0)
            return false;

        // A $badfilter rule disables a rule whose text matches (minus the $badfilter modifier)
        return data.BadFilterTexts.Contains(rule.OriginalText);
    }

    private static FilterData BuildFilterData(DnsFilterRuleSet ruleSet)
    {
        var exactDomainRules = new Dictionary<string, List<DnsFilterRule>>(StringComparer.OrdinalIgnoreCase);
        var suffixDomainRules = new Dictionary<string, List<DnsFilterRule>>(StringComparer.OrdinalIgnoreCase);
        var patternRules = new List<DnsFilterRule>();
        var badFilterTexts = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rule in ruleSet.Rules)
        {
            if (rule.IsBadFilter)
            {
                // Compute what the original rule text would be (strip $badfilter from the original)
                var originalText = ComputeBadFilterTarget(rule.OriginalText);
                badFilterTexts.Add(originalText);
                continue;
            }

            if (rule.ExactDomain is not null)
            {
                if (!exactDomainRules.TryGetValue(rule.ExactDomain, out var list))
                {
                    list = [];
                    exactDomainRules[rule.ExactDomain] = list;
                }

                list.Add(rule);
            }
            else if (rule.DomainSuffix is not null)
            {
                if (!suffixDomainRules.TryGetValue(rule.DomainSuffix, out var list))
                {
                    list = [];
                    suffixDomainRules[rule.DomainSuffix] = list;
                }

                list.Add(rule);
            }
            else if (rule.Pattern is not null)
            {
                patternRules.Add(rule);
            }
        }

        return new FilterData
        {
            ExactDomainRules = exactDomainRules,
            SuffixDomainRules = suffixDomainRules,
            PatternRules = patternRules,
            BadFilterTexts = badFilterTexts,
        };
    }

    private static string ComputeBadFilterTarget(string originalText)
    {
        // Remove $badfilter from the original rule text to find what it targets
        // e.g., "||example.com^$badfilter" → "||example.com^"
        // e.g., "||example.com^$important,badfilter" → "||example.com^$important"
        const string badfilterStr = "badfilter";
        var idx = originalText.LastIndexOf(badfilterStr, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return originalText;

        var before = originalText[..idx];
        var after = originalText[(idx + badfilterStr.Length)..];

        // Remove trailing/leading comma
        if (before.EndsWith(','))
        {
            before = before[..^1];
        }
        else if (after.StartsWith(','))
        {
            after = after[1..];
        }

        // If the $ sign is now trailing with nothing after, remove it
        var result = before + after;
        if (result.EndsWith('$'))
        {
            result = result[..^1];
        }

        return result;
    }

    private sealed class FilterData
    {
        public required Dictionary<string, List<DnsFilterRule>> ExactDomainRules { get; init; }
        public required Dictionary<string, List<DnsFilterRule>> SuffixDomainRules { get; init; }
        public required List<DnsFilterRule> PatternRules { get; init; }
        public required HashSet<string> BadFilterTexts { get; init; }
    }
}
