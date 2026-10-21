using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Meziantou.Framework.DnsFilter;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.DnsFuzz;

/// <summary>
/// Targets of Meziantou.Framework.DnsFilter, which parses filter lists downloaded from third parties and matches the
/// queried names against them: parsing never throws and reports each line it skips, and the engine never throws and
/// returns the rule a naive evaluation of every rule would pick.
/// </summary>
internal static class DnsFilterFuzzTargets
{
    private const int MaxQueries = 16;
    private const int MaxReferenceRules = 64;

    private static readonly IPAddress?[] ClientAddresses = [null, IPAddress.Parse("192.168.1.1"), IPAddress.Parse("192.168.1.5"), IPAddress.Parse("10.0.0.1"), IPAddress.IPv6Loopback, IPAddress.Parse("2001:db8::1")];
    private static readonly string?[] ClientNames = [null, "MyLaptop", "KidsTablet", "Desktop"];
    private static readonly string[]?[] ClientTags = [null, [], ["device_pc"], ["user_child", "device_phone"]];

    /// <summary>
    /// The first byte selects the format (<see cref="DnsFilterListFormat"/>), the rest is the list. Parsing never throws; the
    /// string, reader and diagnostics overloads agree; an auto-detected list parses the same as with the detected format;
    /// each diagnostic points to a line of the list, in order. The rules match one thing (an exact domain, a suffix or a
    /// pattern) with normalized names, and a rule without client scoping matches the domain it names on its own.
    /// </summary>
    public static void List(ReadOnlySpan<byte> data)
    {
        var format = (DnsFilterListFormat)(FuzzInput.ReadByte(ref data) % 4);
        var text = FuzzInput.ReadText(data);

        var result = DnsFilterListReader.ParseWithDiagnostics(text, format);
        var lines = ReadLines(text);
        if (format is DnsFilterListFormat.AutoDetect)
        {
            FuzzAssert.True(result.Format is DnsFilterListFormat.Hosts or DnsFilterListFormat.DomainsOnly or DnsFilterListFormat.AdBlock, $"Unexpected detected format {result.Format}");
            FuzzAssert.Equal(Describe(result), Describe(DnsFilterListReader.ParseWithDiagnostics(text, result.Format)), "An auto-detected list must parse as with the detected format");
        }
        else
        {
            FuzzAssert.Equal(format, result.Format, "The result must report the requested format");
        }

        using (var reader = new StringReader(text))
        {
            FuzzAssert.Equal(Describe(result), Describe(DnsFilterListReader.ParseWithDiagnostics(reader, format)), "The string and TextReader overloads must agree");
        }

        FuzzAssert.Equal(result.Rules.Count, DnsFilterListReader.Parse(text, format).Count, "Parse and ParseWithDiagnostics must agree");

        var previousLine = 0;
        foreach (var diagnostic in result.Diagnostics)
        {
            FuzzAssert.True(diagnostic.LineNumber >= 1 && diagnostic.LineNumber <= lines.Count, $"Diagnostic line {diagnostic.LineNumber} is out of the list ({lines.Count} lines)");
            FuzzAssert.True(diagnostic.LineNumber >= previousLine, "The diagnostics must be in the order of the lines");
            FuzzAssert.Equal(lines[diagnostic.LineNumber - 1], diagnostic.Line, "A diagnostic must give the text of its line");
            FuzzAssert.True(Enum.IsDefined(diagnostic.Error), $"Unexpected error {diagnostic.Error}");
            _ = diagnostic.ToString();
            previousLine = diagnostic.LineNumber;
        }

        var checkedRules = 0;
        foreach (var rule in result.Rules)
        {
            CheckRule(rule, result.Format);
            if (checkedRules < MaxReferenceRules && CheckRuleMatchesItsDomain(rule))
            {
                checkedRules++;
            }
        }
    }

    /// <summary>
    /// The first byte selects the format, then come the list and the queries, separated by NUL characters. A query is
    /// two bytes for the query type, a byte that selects the client (address, name, tags), then the name. The engine never
    /// throws, is deterministic, and its result is consistent with the rule it returns. The rule wins over every other
    /// matching rule: it has the highest priority (<c>$important</c>, then exceptions) and specificity of the rules that
    /// match on their own, whatever the order of the list. The shortcuts of the pattern rules (the literals a name must
    /// contain, the domain suffix the rule is indexed by) never reject a name the regular expression matches.
    /// </summary>
    public static void Engine(ReadOnlySpan<byte> data)
    {
        var format = (DnsFilterListFormat)(FuzzInput.ReadByte(ref data) % 4);
        var text = FuzzInput.ReadText(FuzzInput.ReadUntil(ref data, 0));

        // The backtracking engine (the fallback for the constructs NonBacktracking does not support) is only bounded by a
        // 2-second timeout, which a fuzzer cannot afford for each query
        var rules = DnsFilterListReader.Parse(text, format).Where(rule => rule.Pattern is null || rule.Pattern.Options.HasFlag(RegexOptions.NonBacktracking)).ToArray();

        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddRange(rules);
        var engine = new DnsFilterEngine(ruleSet);

        var reversedRuleSet = new DnsFilterRuleSet();
        reversedRuleSet.AddRange(rules.Reverse());
        var reversedEngine = new DnsFilterEngine(reversedRuleSet);

        // A naive evaluation: each rule on its own, minus the rules a $badfilter rule disables
        DnsFilterEngine[]? singleRuleEngines = null;
        DnsFilterRule[]? activeRules = null;
        if (rules.Length <= MaxReferenceRules)
        {
            var disabledKeys = rules.Where(rule => rule.IsBadFilter && rule.BadFilterKey is not null).Select(rule => rule.BadFilterKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            activeRules = [.. rules.Where(rule => !rule.IsBadFilter && (rule.BadFilterKey is null || !disabledKeys.Contains(rule.BadFilterKey)))];
            singleRuleEngines = [.. activeRules.Select(rule =>
            {
                var single = new DnsFilterRuleSet();
                single.Add(rule);
                return new DnsFilterEngine(single);
            })];
        }

        for (var i = 0; i < MaxQueries && !data.IsEmpty; i++)
        {
            var queryType = (DnsFilterQueryType)((FuzzInput.ReadByte(ref data) << 8) | FuzzInput.ReadByte(ref data));
            var clientSelector = FuzzInput.ReadByte(ref data);
            var client = new DnsClientInfo
            {
                Address = FuzzInput.Pick(ClientAddresses, clientSelector),
                Name = FuzzInput.Pick(ClientNames, (byte)(clientSelector >> 3)),
                Tags = FuzzInput.Pick(ClientTags, (byte)(clientSelector >> 5)),
            };

            var domain = FuzzInput.ReadText(FuzzInput.ReadUntil(ref data, 0));
            var result = engine.Evaluate(domain, queryType, client);
            CheckResult(result, rules);

            var again = engine.Evaluate(domain, queryType, client);
            FuzzAssert.True(ReferenceEquals(result.MatchingRule, again.MatchingRule) && result.Action == again.Action, "Evaluate must be deterministic");

            var reversed = reversedEngine.Evaluate(domain, queryType, client);
            FuzzAssert.Equal(GetPriority(result), GetPriority(reversed), "The result must not depend on the order of the rules");

            if (singleRuleEngines is not null && activeRules is not null)
            {
                var expected = (Rank: 0, Specificity: 0, Action: DnsFilterAction.None);
                for (var j = 0; j < singleRuleEngines.Length; j++)
                {
                    var single = singleRuleEngines[j].Evaluate(domain, queryType, client);
                    if (single.IsMatched)
                    {
                        FuzzAssert.True(ReferenceEquals(single.MatchingRule, activeRules[j]), "A single-rule engine can only return its rule");
                        var priority = GetPriority(single);
                        if (priority.Rank > expected.Rank || (priority.Rank == expected.Rank && priority.Specificity > expected.Specificity))
                        {
                            expected = priority;
                        }
                    }
                }

                FuzzAssert.Equal(expected, GetPriority(result), $"The engine must pick the best of the rules that match {FuzzAssert.Format(domain)} on their own");
            }

            CheckPatternShortcuts(rules, domain);
        }
    }

    private static void CheckResult(DnsFilterResult result, DnsFilterRule[] rules)
    {
        FuzzAssert.Equal(result.MatchingRule is not null, result.IsMatched, "IsMatched must be true exactly when there is a matching rule");
        FuzzAssert.Equal(result.Action is DnsFilterAction.Rewrite, result.Rewrite is not null, "Rewrite must be set exactly for the Rewrite action");
        if (result.MatchingRule is not { } rule)
        {
            FuzzAssert.Equal(DnsFilterAction.None, result.Action, "A query that matches no rule has no action");
            return;
        }

        FuzzAssert.True(rules.Contains(rule), "The matching rule must be one of the rules of the engine");
        FuzzAssert.False(rule.IsBadFilter, "A $badfilter rule never matches");
        var expectedAction = rule.Action is DnsFilterAction.Allow ? DnsFilterAction.Allow : rule.Rewrite is null ? DnsFilterAction.Block : DnsFilterAction.Rewrite;
        FuzzAssert.Equal(expectedAction, result.Action, "The action must be the action of the matching rule");
        // An exception rule allows the query, even with a $dnsrewrite
        FuzzAssert.True(ReferenceEquals(rule.Action is DnsFilterAction.Allow ? null : rule.Rewrite, result.Rewrite), "The rewrite must be the rewrite of the matching blocking rule");
    }

    /// <summary>The ordering of <c>DnsFilterEngine</c>: the priority of the rule, then its specificity.</summary>
    private static (int Rank, int Specificity, DnsFilterAction Action) GetPriority(DnsFilterResult result)
    {
        if (result.MatchingRule is not { } rule)
            return (0, 0, DnsFilterAction.None);

        var rank = (rule.IsImportant, rule.Action) switch
        {
            (true, DnsFilterAction.Allow) => 4,
            (true, _) => 3,
            (false, DnsFilterAction.Allow) => 2,
            _ => 1,
        };

        var specificity = rule.ExactDomain is not null ? 1_000_000 : rule.DomainSuffix is not null ? 1_000 + rule.DomainSuffix.Length : 1;
        if (rule.Rewrite is not null)
        {
            specificity += 10_000_000;
        }

        return (rank, specificity, result.Action);
    }

    private static void CheckPatternShortcuts(DnsFilterRule[] rules, string domain)
    {
        if (!DnsDomainName.TryNormalize(domain, out var name))
            return;

        foreach (var rule in rules)
        {
            if (rule.Pattern is not { } pattern || !pattern.IsMatch(name))
                continue;

            foreach (var literal in rule.RequiredLiterals ?? [])
            {
                FuzzAssert.True(name.Contains(literal, StringComparison.Ordinal), $"The pattern {FuzzAssert.Format(rule.PatternText)} matches {FuzzAssert.Format(name)}, which does not contain its required literal {FuzzAssert.Format(literal)}");
            }

            if (rule.PatternSuffix is { } suffix)
            {
                FuzzAssert.True(name == suffix || name.EndsWith("." + suffix, StringComparison.Ordinal), $"The pattern {FuzzAssert.Format(rule.PatternText)} matches {FuzzAssert.Format(name)}, which is not under the suffix it is indexed by ({FuzzAssert.Format(suffix)})");
            }
        }
    }

    private static void CheckRule(DnsFilterRule rule, DnsFilterListFormat format)
    {
        FuzzAssert.True(rule.Action is DnsFilterAction.Block or DnsFilterAction.Allow, $"Unexpected rule action {rule.Action}");
        var kinds = (rule.ExactDomain is null ? 0 : 1) + (rule.DomainSuffix is null ? 0 : 1) + (rule.PatternText is null ? 0 : 1);
        FuzzAssert.Equal(1, kinds, $"A rule must match an exact domain, a suffix or a pattern: {FuzzAssert.Format(rule.OriginalText)}");
        foreach (var domain in new[] { rule.ExactDomain, rule.DomainSuffix }.Concat(rule.DenyAllowDomains ?? []))
        {
            if (domain is not null)
            {
                FuzzAssert.True(DnsDomainName.TryNormalize(domain, out var normalized) && normalized == domain, $"The rule {FuzzAssert.Format(rule.OriginalText)} holds the name {FuzzAssert.Format(domain)}, which is not normalized");
            }
        }

        if (rule.PatternSuffix is { } suffix)
        {
            FuzzAssert.True(DnsDomainName.TryNormalize(suffix, out var normalized) && normalized == suffix, $"The pattern suffix {FuzzAssert.Format(suffix)} is not normalized");
        }

        FuzzAssert.True(rule.AllowedDnsTypes is null || rule.AllowedDnsTypes.Count > 0, "An empty $dnstype would match nothing");
        if (format is not DnsFilterListFormat.AdBlock)
        {
            FuzzAssert.True(rule.Action is DnsFilterAction.Block && rule.ExactDomain is not null && !rule.IsImportant && !rule.IsBadFilter && rule.Rewrite is null, "A hosts or domains rule blocks an exact domain");
        }
    }

    /// <summary>A rule that is not scoped to some clients matches the domain it names (and a subdomain for a suffix rule) on its own.</summary>
    private static bool CheckRuleMatchesItsDomain(DnsFilterRule rule)
    {
        var domain = rule.ExactDomain ?? rule.DomainSuffix;
        if (domain is null || rule.IsBadFilter || rule.ClientSpecs is not null || rule.TagSpec is not null || rule.DenyAllowDomains is not null)
            return false;

        var queryType = rule.AllowedDnsTypes?.First() ?? DnsFilterQueryType.A;
        if (rule.ExcludedDnsTypes is not null && rule.ExcludedDnsTypes.Contains(queryType))
            return false;

        var ruleSet = new DnsFilterRuleSet();
        ruleSet.Add(rule);
        var engine = new DnsFilterEngine(ruleSet);
        FuzzAssert.True(ReferenceEquals(rule, engine.Evaluate(domain, queryType).MatchingRule), $"The rule {FuzzAssert.Format(rule.OriginalText)} must match {FuzzAssert.Format(domain)}");
        FuzzAssert.True(ReferenceEquals(rule, engine.Evaluate(domain.ToUpperInvariant(), queryType).MatchingRule), $"The rule {FuzzAssert.Format(rule.OriginalText)} must match {FuzzAssert.Format(domain)} ignoring case");
        if (rule.DomainSuffix is not null && domain.Length + 2 <= DnsMessageText.MaxNameTextLength)
        {
            FuzzAssert.True(ReferenceEquals(rule, engine.Evaluate("a." + domain, queryType).MatchingRule), $"The rule {FuzzAssert.Format(rule.OriginalText)} must match the subdomains of {FuzzAssert.Format(domain)}");
        }

        return true;
    }

    private static string Describe(DnsFilterParseResult result)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"Format: {result.Format}\n");
        foreach (var rule in result.Rules)
        {
            builder.Append(CultureInfo.InvariantCulture, $"Rule: {FuzzText.Escape(rule.OriginalText)} {rule.Action} exact={rule.ExactDomain} suffix={rule.DomainSuffix} pattern={rule.PatternText} key={rule.BadFilterKey}\n");
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            builder.Append(CultureInfo.InvariantCulture, $"Diagnostic: {diagnostic.LineNumber} {diagnostic.Error} {(diagnostic.Detail is null ? "null" : FuzzText.Escape(diagnostic.Detail))}\n");
        }

        return builder.ToString();
    }

    private static List<string> ReadLines(string text)
    {
        var lines = new List<string>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }
}
