using System.Text.Json;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>https://html.spec.whatwg.org/multipage/speculative-loading.html#speculation-rule-eagerness, from the most eager to the least eager.</summary>
internal enum SpeculationEagerness
{
    Immediate,
    Eager,
    Moderate,
    Conservative,
}

/// <summary>
/// A speculation rule set (https://html.spec.whatwg.org/multipage/speculative-loading.html#speculation-rule-set): the
/// prefetch rules, and the prerender rules of the Prerendering specification
/// (https://wicg.github.io/nav-speculation/prerendering.html#speculation-rule-set-prerender-rules).
/// </summary>
internal sealed class SpeculationRuleSet
{
    public List<SpeculationRule> PrefetchRules { get; } = [];

    public List<SpeculationRule> PrerenderRules { get; } = [];

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/speculative-loading.html#parse-a-speculation-rule-set-string
    /// </summary>
    /// <exception cref="JsonException">The string is not valid JSON.</exception>
    /// <exception cref="TypeErrorException">The top-level value is not an object, or the tag of the rule set is not valid.</exception>
    public static SpeculationRuleSet Parse(string input, Document document, UrlRecord baseUrl)
    {
        using var json = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 512 });
        var parsed = json.RootElement;
        if (parsed.ValueKind != JsonValueKind.Object)
            throw new TypeErrorException("The speculation rules must be a JSON object");

        var result = new SpeculationRuleSet();
        string? tag = null;
        if (TryGet(parsed, "tag", out var rawTag))
        {
            if (!IsSpeculationRuleTag(rawTag))
                throw new TypeErrorException("The tag of the speculation rules is not valid");

            tag = rawTag.GetString();
        }

        ParseRules(parsed, "prefetch", result.PrefetchRules, tag, document, baseUrl);
        ParseRules(parsed, "prerender", result.PrerenderRules, tag, document, baseUrl);
        return result;
    }

    private static void ParseRules(JsonElement parsed, string type, List<SpeculationRule> rules, string? tag, Document document, UrlRecord baseUrl)
    {
        if (!TryGet(parsed, type, out var list) || list.ValueKind != JsonValueKind.Array)
            return;

        foreach (var rawRule in list.EnumerateArray())
        {
            if (ParseRule(rawRule, tag, document, baseUrl, isPrerender: type == "prerender") is { } rule)
            {
                rules.Add(rule);
            }
        }
    }

    /// <summary>The value of a key of a JSON object: the last one, as <c>JSON.parse()</c> does for the duplicate keys.</summary>
    internal static bool TryGet(JsonElement map, string key, out JsonElement value)
    {
        value = default;
        var found = false;
        foreach (var property in map.EnumerateObject())
        {
            if (property.NameEquals(key))
            {
                value = property.Value;
                found = true;
            }
        }

        return found;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/speculative-loading.html#speculation-rule-tag: a string of printable ASCII characters.</summary>
    private static bool IsSpeculationRuleTag(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            return false;

        foreach (var c in value.GetString()!)
        {
            if (c is < ' ' or > '~')
                return false;
        }

        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/speculative-loading.html#parse-a-speculation-rule</summary>
    private static SpeculationRule? ParseRule(JsonElement input, string? rulesetLevelTag, Document document, UrlRecord baseUrl, bool isPrerender)
    {
        if (input.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in input.EnumerateObject())
        {
            if (property.Name is not ("source" or "urls" or "where" or "relative_to" or "eagerness" or "referrer_policy" or "tag" or "requires" or "expects_no_vary_search" or "target_hint"))
                return null;
        }

        var hasUrls = TryGet(input, "urls", out var rawUrls);
        var hasWhere = TryGet(input, "where", out var rawWhere);
        string? source = null;
        if (TryGet(input, "source", out var rawSource))
        {
            source = rawSource.ValueKind == JsonValueKind.String ? rawSource.GetString() : "";
        }
        else if (hasUrls && !hasWhere)
        {
            source = "list";
        }
        else if (hasWhere && !hasUrls)
        {
            source = "document";
        }

        if (source is not ("list" or "document"))
            return null;

        var urls = new List<UrlRecord>();
        DocumentRulePredicate? predicate = null;
        var hasRelativeTo = TryGet(input, "relative_to", out var relativeTo);
        if (source == "list")
        {
            if (hasWhere)
                return null;

            if (hasRelativeTo)
            {
                if (relativeTo.ValueKind != JsonValueKind.String || relativeTo.GetString() is not ("ruleset" or "document"))
                    return null;

                if (relativeTo.GetString() == "document")
                {
                    baseUrl = document.DocumentBaseUrl;
                }
            }

            if (!hasUrls || rawUrls.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var urlString in rawUrls.EnumerateArray())
            {
                if (urlString.ValueKind != JsonValueKind.String)
                    return null;

                if (UrlParser.Parse(urlString.GetString()!, baseUrl) is { Scheme: "http" or "https" } parsedUrl)
                {
                    urls.Add(parsedUrl);
                }
            }
        }
        else
        {
            if (hasUrls || hasRelativeTo)
                return null;

            predicate = hasWhere ? DocumentRulePredicate.Parse(rawWhere, document, baseUrl) : new DocumentRuleConjunction([]);
            if (predicate is null)
                return null;
        }

        var eagerness = source == "list" ? SpeculationEagerness.Immediate : SpeculationEagerness.Conservative;
        if (TryGet(input, "eagerness", out var rawEagerness))
        {
            switch (rawEagerness.ValueKind == JsonValueKind.String ? rawEagerness.GetString() : null)
            {
                case "immediate":
                    eagerness = SpeculationEagerness.Immediate;
                    break;
                case "eager":
                    eagerness = SpeculationEagerness.Eager;
                    break;
                case "moderate":
                    eagerness = SpeculationEagerness.Moderate;
                    break;
                case "conservative":
                    eagerness = SpeculationEagerness.Conservative;
                    break;
                default:
                    return null;
            }
        }

        var referrerPolicy = "";
        if (TryGet(input, "referrer_policy", out var rawReferrerPolicy))
        {
            if (rawReferrerPolicy.ValueKind != JsonValueKind.String || (rawReferrerPolicy.GetString() is { Length: > 0 } policy && !ReferrerPolicies.IsValid(policy)))
                return null;

            referrerPolicy = rawReferrerPolicy.GetString()!;
        }

        var tags = new List<string?>();
        if (rulesetLevelTag is not null)
        {
            tags.Add(rulesetLevelTag);
        }

        if (TryGet(input, "tag", out var rawTag))
        {
            if (!IsSpeculationRuleTag(rawTag))
                return null;

            if (!tags.Contains(rawTag.GetString()))
            {
                tags.Add(rawTag.GetString());
            }
        }

        if (tags.Count == 0)
        {
            tags.Add(null);
        }

        var anonymousClientIp = false;
        if (TryGet(input, "requires", out var requires))
        {
            if (requires.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var requirement in requires.EnumerateArray())
            {
                if (requirement.ValueKind != JsonValueKind.String || requirement.GetString() != "anonymous-client-ip-when-cross-origin")
                    return null;

                anonymousClientIp = true;
            }
        }

        var noVarySearchHint = UrlSearchVariance.Default;
        if (TryGet(input, "expects_no_vary_search", out var rawNoVarySearch))
        {
            if (rawNoVarySearch.ValueKind != JsonValueKind.String)
                return null;

            noVarySearchHint = UrlSearchVariance.Parse(rawNoVarySearch.GetString()!);
        }

        // https://wicg.github.io/nav-speculation/prerendering.html#parse-a-speculation-rule: the target navigable name hint of
        // a prerender rule is a valid navigable target name or keyword
        string? targetHint = null;
        if (TryGet(input, "target_hint", out var rawTargetHint))
        {
            if (rawTargetHint.ValueKind != JsonValueKind.String || !IsValidNavigableTargetNameOrKeyword(rawTargetHint.GetString()!))
                return null;

            targetHint = isPrerender ? rawTargetHint.GetString() : null;
        }

        return new SpeculationRule
        {
            Urls = urls,
            Predicate = predicate,
            Eagerness = eagerness,
            ReferrerPolicy = referrerPolicy,
            Tags = tags,
            RequiresAnonymousClientIp = anonymousClientIp,
            NoVarySearchHint = noVarySearchHint,
            TargetHint = targetHint,
        };
    }

    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#valid-navigable-target-name-or-keyword</summary>
    private static bool IsValidNavigableTargetNameOrKeyword(string value)
    {
        if (value.Length == 0)
            return false;

        if (value[0] != '_')
            return true;

        return value.Equals("_blank", StringComparison.OrdinalIgnoreCase) || value.Equals("_self", StringComparison.OrdinalIgnoreCase)
            || value.Equals("_parent", StringComparison.OrdinalIgnoreCase) || value.Equals("_top", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>https://html.spec.whatwg.org/multipage/speculative-loading.html#speculation-rule</summary>
internal sealed class SpeculationRule
{
    public required List<UrlRecord> Urls { get; init; }

    public required DocumentRulePredicate? Predicate { get; init; }

    public required SpeculationEagerness Eagerness { get; init; }

    public required string ReferrerPolicy { get; init; }

    /// <summary>The tags of the rule: the tag of its rule set and its own tag, or a single null tag.</summary>
    public required List<string?> Tags { get; init; }

    /// <summary>Whether the requirements of the rule contain "anonymous-client-ip-when-cross-origin".</summary>
    public required bool RequiresAnonymousClientIp { get; init; }

    public required UrlSearchVariance NoVarySearchHint { get; init; }

    /// <summary>https://wicg.github.io/nav-speculation/prerendering.html#speculation-rule-target-navigable-name-hint</summary>
    public string? TargetHint { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/speculative-loading.html#document-rule-predicate</summary>
internal abstract class DocumentRulePredicate
{
    /// <summary>https://html.spec.whatwg.org/multipage/speculative-loading.html#document-rule-predicate-matches</summary>
    public abstract bool Matches(Element element, UrlRecord url);

    /// <summary>Whether the predicate (or one of its clauses) matches selectors: its result depends on the styles of the document.</summary>
    public abstract bool HasSelectors { get; }

    /// <summary>https://html.spec.whatwg.org/multipage/speculative-loading.html#parse-a-document-rule-predicate</summary>
    public static DocumentRulePredicate? Parse(JsonElement input, Document document, UrlRecord baseUrl)
    {
        if (input.ValueKind != JsonValueKind.Object)
            return null;

        string? predicateType = null;
        foreach (var property in input.EnumerateObject())
        {
            if (property.Name is "and" or "or" or "not" or "href_matches" or "selector_matches" && property.Name != predicateType)
            {
                if (predicateType is not null)
                    return null;

                predicateType = property.Name;
            }
        }

        if (predicateType is null)
            return null;

        SpeculationRuleSet.TryGet(input, predicateType, out var value);
        foreach (var property in input.EnumerateObject())
        {
            if (property.Name != predicateType && !(predicateType == "href_matches" && property.Name == "relative_to"))
                return null;
        }

        switch (predicateType)
        {
            case "and" or "or":
                {
                    if (value.ValueKind != JsonValueKind.Array)
                        return null;

                    var clauses = new List<DocumentRulePredicate>();
                    foreach (var rawClause in value.EnumerateArray())
                    {
                        if (Parse(rawClause, document, baseUrl) is not { } clause)
                            return null;

                        clauses.Add(clause);
                    }

                    return predicateType == "and" ? new DocumentRuleConjunction(clauses) : new DocumentRuleDisjunction(clauses);
                }

            case "not":
                return Parse(value, document, baseUrl) is { } negated ? new DocumentRuleNegation(negated) : null;

            case "href_matches":
                {
                    if (SpeculationRuleSet.TryGet(input, "relative_to", out var relativeTo))
                    {
                        if (relativeTo.ValueKind != JsonValueKind.String || relativeTo.GetString() is not ("ruleset" or "document"))
                            return null;

                        if (relativeTo.GetString() == "document")
                        {
                            baseUrl = document.DocumentBaseUrl;
                        }
                    }

                    var patterns = new List<UrlPattern>();
                    foreach (var rawPattern in value.ValueKind == JsonValueKind.Array ? [.. value.EnumerateArray()] : new[] { value })
                    {
                        if (BuildUrlPattern(rawPattern, baseUrl) is not { } pattern)
                            return null;

                        patterns.Add(pattern);
                    }

                    return new DocumentRuleUrlPatternPredicate(patterns);
                }

            default:
                {
                    var selectors = new List<SelectorList>();
                    foreach (var rawSelector in value.ValueKind == JsonValueKind.Array ? [.. value.EnumerateArray()] : new[] { value })
                    {
                        if (rawSelector.ValueKind != JsonValueKind.String || SelectorCache.TryParse(rawSelector.GetString()!) is not { } selectorList)
                            return null;

                        selectors.Add(selectorList);
                    }

                    return new DocumentRuleSelectorPredicate(selectors);
                }
        }
    }

    /// <summary>https://urlpattern.spec.whatwg.org/#build-a-url-pattern-from-an-infra-value (null when it throws).</summary>
    private static UrlPattern? BuildUrlPattern(JsonElement rawPattern, UrlRecord baseUrl)
    {
        var serializedBaseUrl = baseUrl.Serialize();
        try
        {
            if (rawPattern.ValueKind == JsonValueKind.String)
                return new UrlPattern(rawPattern.GetString()!, serializedBaseUrl);

            if (rawPattern.ValueKind != JsonValueKind.Object)
                return null;

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in rawPattern.EnumerateObject())
            {
                if (property.Name is not ("protocol" or "username" or "password" or "hostname" or "port" or "pathname" or "search" or "hash" or "baseURL") || property.Value.ValueKind != JsonValueKind.String)
                    return null;

                values[property.Name] = property.Value.GetString()!;
            }

            return new UrlPattern(new UrlPatternInit
            {
                Protocol = values.GetValueOrDefault("protocol"),
                Username = values.GetValueOrDefault("username"),
                Password = values.GetValueOrDefault("password"),
                Hostname = values.GetValueOrDefault("hostname"),
                Port = values.GetValueOrDefault("port"),
                Pathname = values.GetValueOrDefault("pathname"),
                Search = values.GetValueOrDefault("search"),
                Hash = values.GetValueOrDefault("hash"),
                BaseUrl = values.GetValueOrDefault("baseURL") ?? serializedBaseUrl,
            });
        }
        catch (TypeErrorException)
        {
            return null;
        }
    }
}

internal sealed class DocumentRuleConjunction(List<DocumentRulePredicate> clauses) : DocumentRulePredicate
{
    public override bool HasSelectors => clauses.Exists(static clause => clause.HasSelectors);

    public override bool Matches(Element element, UrlRecord url) => clauses.TrueForAll(clause => clause.Matches(element, url));
}

internal sealed class DocumentRuleDisjunction(List<DocumentRulePredicate> clauses) : DocumentRulePredicate
{
    public override bool HasSelectors => clauses.Exists(static clause => clause.HasSelectors);

    public override bool Matches(Element element, UrlRecord url) => clauses.Exists(clause => clause.Matches(element, url));
}

internal sealed class DocumentRuleNegation(DocumentRulePredicate clause) : DocumentRulePredicate
{
    public override bool HasSelectors => clause.HasSelectors;

    public override bool Matches(Element element, UrlRecord url) => !clause.Matches(element, url);
}

internal sealed class DocumentRuleUrlPatternPredicate(List<UrlPattern> patterns) : DocumentRulePredicate
{
    public override bool HasSelectors => false;

    public override bool Matches(Element element, UrlRecord url)
    {
        var wrapper = new Url(url);
        return patterns.Exists(pattern => pattern.Test(wrapper));
    }
}

internal sealed class DocumentRuleSelectorPredicate(List<SelectorList> selectors) : DocumentRulePredicate
{
    public override bool HasSelectors => true;

    public override bool Matches(Element element, UrlRecord url) => selectors.Exists(selector => element.Matches(selector));
}

/// <summary>
/// A URL search variance (https://httpwg.org/http-extensions/draft-ietf-httpbis-no-vary-search.html#name-data-model): the
/// query parameters that do not change the response of a URL, as declared by the <c>No-Vary-Search</c> response header (or
/// expected by the <c>expects_no_vary_search</c> hint of a speculation rule).
/// </summary>
internal sealed class UrlSearchVariance : IEquatable<UrlSearchVariance>
{
    public static UrlSearchVariance Default { get; } = new(noVaryParams: [], varyParams: null, varyOnKeyOrder: true);

    private UrlSearchVariance(List<string>? noVaryParams, List<string>? varyParams, bool varyOnKeyOrder)
    {
        NoVaryParams = noVaryParams;
        VaryParams = varyParams;
        VaryOnKeyOrder = varyOnKeyOrder;
    }

    /// <summary>The no-vary params, or null for the wildcard.</summary>
    public List<string>? NoVaryParams { get; }

    /// <summary>The vary params, or null for the wildcard.</summary>
    public List<string>? VaryParams { get; }

    public bool VaryOnKeyOrder { get; }

    public bool IsDefault => NoVaryParams is { Count: 0 } && VaryParams is null && VaryOnKeyOrder;

    /// <summary>https://httpwg.org/http-extensions/draft-ietf-httpbis-no-vary-search.html#name-obtain-a-url-search-varianc</summary>
    public static UrlSearchVariance Obtain(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        var value = HeaderList.Get(headers, "No-Vary-Search");
        return value is null ? Default : Parse(value);
    }

    /// <summary>https://httpwg.org/http-extensions/draft-ietf-httpbis-no-vary-search.html#name-parse-a-url-search-variance</summary>
    public static UrlSearchVariance Parse(string value)
    {
        if (Infra.StructuredFields.ParseDictionary(value) is not { } dictionary)
            return Default;

        List<string>? noVaryParams = [];
        List<string>? varyParams = null;
        var varyOnKeyOrder = true;
        var paramsIsTrue = false;
        foreach (var (key, member) in dictionary)
        {
            switch (key)
            {
                case "key-order":
                    if (member.Item?.Value is not bool keyOrder)
                        return Default;

                    varyOnKeyOrder = !keyOrder;
                    break;

                case "params":
                    if (member.Item?.Value is bool allParams)
                    {
                        paramsIsTrue = allParams;
                        if (allParams)
                        {
                            noVaryParams = null;
                            varyParams = [];
                        }
                    }
                    else if (GetStrings(member) is { } names)
                    {
                        noVaryParams = names;
                    }
                    else
                    {
                        return Default;
                    }

                    break;
            }
        }

        foreach (var (key, member) in dictionary)
        {
            if (key != "except")
                continue;

            if (!paramsIsTrue || GetStrings(member) is not { } names)
                return Default;

            varyParams = names;
        }

        return new UrlSearchVariance(noVaryParams, varyParams, varyOnKeyOrder);
    }

    private static List<string>? GetStrings(Infra.StructuredFieldMember member)
    {
        if (member.InnerList is not { } innerList)
            return null;

        var result = new List<string>();
        foreach (var item in innerList)
        {
            if (item.Value is not string name)
                return null;

            // The names are parsed as the names of an application/x-www-form-urlencoded string
            result.Add(FormUrlEncoded.Parse(System.Text.Encoding.UTF8.GetBytes("=" + name)) is [var pair, ..] ? pair.Value : name);
        }

        return result;
    }

    /// <summary>https://httpwg.org/http-extensions/draft-ietf-httpbis-no-vary-search.html#name-comparing: the fragments are ignored.</summary>
    public bool AreEquivalent(UrlRecord urlA, UrlRecord urlB)
    {
        if (WithoutQuery(urlA) != WithoutQuery(urlB))
            return false;

        if (IsDefault)
            return (urlA.Query ?? "") == (urlB.Query ?? "");

        var paramsA = GetSearchParams(urlA);
        var paramsB = GetSearchParams(urlB);
        return paramsA.Count == paramsB.Count && paramsA.SequenceEqual(paramsB);
    }

    /// <summary>The serialization of the scheme, the credentials, the host, the port and the path of a URL.</summary>
    private static string WithoutQuery(UrlRecord url)
    {
        var serialized = url.Serialize(true);
        var index = serialized.IndexOf('?', StringComparison.Ordinal);
        return index < 0 ? serialized : serialized[..index];
    }

    private List<KeyValuePair<string, string>> GetSearchParams(UrlRecord url)
    {
        var searchParams = FormUrlEncoded.Parse(System.Text.Encoding.UTF8.GetBytes(url.Query ?? ""));
        if (NoVaryParams is { } noVaryParams)
        {
            searchParams.RemoveAll(pair => noVaryParams.Contains(pair.Key));
        }
        else
        {
            searchParams.RemoveAll(pair => !VaryParams!.Contains(pair.Key));
        }

        if (!VaryOnKeyOrder)
        {
            // A stable sort by key, in code unit order
            searchParams = [.. searchParams.Select((pair, index) => (pair, index)).OrderBy(static entry => entry.pair.Key, StringComparer.Ordinal).ThenBy(static entry => entry.index).Select(static entry => entry.pair)];
        }

        return searchParams;
    }

    public bool Equals([NotNullWhen(true)] UrlSearchVariance? other)
    {
        if (other is null)
            return false;

        return VaryOnKeyOrder == other.VaryOnKeyOrder && ListEquals(NoVaryParams, other.NoVaryParams) && ListEquals(VaryParams, other.VaryParams);

        static bool ListEquals(List<string>? a, List<string>? b) => a is null ? b is null : b is not null && a.SequenceEqual(b);
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as UrlSearchVariance);

    public override int GetHashCode() => HashCode.Combine(VaryOnKeyOrder, NoVaryParams?.Count, VaryParams?.Count);
}
