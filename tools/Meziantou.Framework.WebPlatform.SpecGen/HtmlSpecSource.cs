using System.Net;
using System.Text.RegularExpressions;

namespace SpecGen;

/// <summary>An enumerated attribute extracted from the HTML specification source.</summary>
internal sealed class EnumeratedAttributeDefinition
{
    /// <summary>The state identifiers (data-x of the state dfn), in order of appearance.</summary>
    public List<string> States { get; } = [];

    /// <summary>The keywords (lowercase) and their state identifier.</summary>
    public Dictionary<string, string> Keywords { get; } = new(StringComparer.Ordinal);

    /// <summary>The explicit canonical keyword of states with several keywords.</summary>
    public Dictionary<string, string> CanonicalKeywords { get; } = new(StringComparer.Ordinal);

    public string? MissingValueDefault { get; set; }

    public string? InvalidValueDefault { get; set; }

    public string? EmptyValueDefault { get; set; }

    public string Source { get; set; } = "";

    public void AddState(string state)
    {
        if (!States.Contains(state, StringComparer.Ordinal))
        {
            States.Add(state);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#canonical-keyword</summary>
    public string? GetCanonicalKeyword(string state)
    {
        if (CanonicalKeywords.TryGetValue(state, out var explicitKeyword))
            return explicitKeyword;

        var keywords = Keywords.Where(k => k.Value == state).Select(k => k.Key).ToList();
        if (keywords.Count == 0)
            return null;

        if (keywords.Count == 1)
            return keywords[0];

        var nonEmpty = keywords.Where(k => k.Length > 0).ToList();
        if (nonEmpty.Count == 1)
            return nonEmpty[0];

        // Otherwise the canonical keyword is given explicitly by the specification; the first keyword of the table is used
        return keywords[0];
    }
}

/// <summary>Extracts machine-readable data from the HTML specification source (tests/Meziantou.Framework.WebPlatform.SpecData/html-source/source).</summary>
internal sealed partial class HtmlSpecSource
{
    private readonly string _source;

    // The potential destinations of Fetch (https://fetch.spec.whatwg.org/#concept-potential-destination), the keywords of the link "as" attribute
    private static readonly string[] PotentialDestinations =
    [
        "fetch", "audio", "audioworklet", "document", "embed", "font", "frame", "iframe", "image", "json", "manifest", "object",
        "paintworklet", "report", "script", "serviceworker", "sharedworker", "style", "text", "track", "video", "webidentity", "worker", "xslt",
    ];

    /// <param name="source">The content of the HTML specification source file.</param>
    /// <param name="referrerPolicies">The values of the ReferrerPolicy IDL enum, the keywords of referrer policy attributes.</param>
    public HtmlSpecSource(string source, IReadOnlyList<string> referrerPolicies)
    {
        _source = source;
        ParseEnumeratedAttributes();
        ParseAttributeDefaults();
        AddReferrerPolicyAttributes(referrerPolicies);
        AddLinkAsAttribute();
        ParseLimitedReflections();
        ParseNumericReflections();
    }

    /// <summary>
    /// The numeric reflections defined in prose, e.g. "The size IDL attribute is limited to only positive numbers and has a default value of 20":
    /// "Interface.idlAttribute" → (extended attribute, default value).
    /// </summary>
    public Dictionary<string, (string Kind, string? DefaultValue)> NumericReflections { get; } = new(StringComparer.Ordinal);

    private void ParseNumericReflections()
    {
        // The IDL attributes by their definition id: <dfn attribute for="HTMLInputElement" data-x="dom-input-size">
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match dfn in IdlDfnWithIdRegex().Matches(_source))
        {
            foreach (var interfaceName in dfn.Groups["for"].Value.Split(','))
            {
                ids.TryAdd(dfn.Groups["id"].Value, interfaceName.Trim() + "." + StripTags(dfn.Groups["name"].Value));
            }
        }

        foreach (Match paragraph in ParagraphRegex().Matches(_source))
        {
            var html = Normalize(paragraph.Groups["content"].Value);
            var match = NumericReflectionRegex().Match(html);
            if (!match.Success || !ids.TryGetValue(match.Groups["id"].Value, out var key))
                continue;

            var kind = match.Groups["kind"].Value switch
            {
                "positive numbers with fallback" => "ReflectPositiveWithFallback",
                "positive numbers" => "ReflectPositive",
                _ => "ReflectNonNegative",
            };

            NumericReflections[key] = (kind, match.Groups["default"].Success ? match.Groups["default"].Value : null);
        }
    }

    /// <summary>The enumerated attributes, keyed by "element/attribute" ("html-global/dir" for global attributes).</summary>
    public Dictionary<string, EnumeratedAttributeDefinition> EnumeratedAttributes { get; } = new(StringComparer.Ordinal);

    /// <summary>The IDL attributes limited to only known values: "Interface.idlAttribute" → content attribute name.</summary>
    public Dictionary<string, string> LimitedReflections { get; } = new(StringComparer.Ordinal);

    public static string StripTags(string html) => WebUtility.HtmlDecode(TagRegex().Replace(html, "")).Trim();

    private static string Normalize(string html) => WhitespaceRegex().Replace(html, " ");

    private void ParseEnumeratedAttributes()
    {
        // An enumerated attribute is introduced by a paragraph containing "enumerated attribute(s)", followed by the
        // keywords (in a table with a State column, or listed), then by the default states.
        foreach (Match paragraph in ParagraphRegex().Matches(_source))
        {
            var intro = paragraph.Groups["content"].Value;
            if (!EnumeratedIntroRegex().IsMatch(intro))
                continue;

            var start = paragraph.Index + paragraph.Length;
            var end = FindEndOfDefinition(start);
            var section = _source[start..end];
            var definition = new EnumeratedAttributeDefinition();
            var owners = new HashSet<string>(StringComparer.Ordinal);

            // The attributes defined by the introduction ("The <dfn element-attr for=form,button><code>method</code></dfn> ...")
            foreach (Match attribute in ElementAttrDfnRegex().Matches(intro))
            {
                var name = StripTags(attribute.Groups["name"].Value);
                foreach (var element in attribute.Groups["for"].Value.Split(','))
                {
                    owners.Add(element.Trim() + "/" + name);
                }
            }

            var table = TableRegex().Match(section);
            var hasStateColumn = table.Success && table.Groups["head"].Value.Contains("State", StringComparison.Ordinal);
            if (hasStateColumn)
            {
                // A state cell can span several rows (keywords): rowspan="n"
                string? spanState = null;
                var spanRemaining = 0;
                foreach (var row in table.Groups["body"].Value.Split("<tr>", StringSplitOptions.RemoveEmptyEntries))
                {
                    var cells = row.Split("<td", StringSplitOptions.None).Skip(1).Select(c => "<td" + c).ToList();
                    string state;
                    if (cells.Count < 2)
                    {
                        if (cells.Count == 0 || spanRemaining <= 0 || spanState is null)
                            continue;

                        spanRemaining--;
                        state = spanState;
                    }
                    else
                    {
                        var stateMatch = StateRegex().Match(cells[1]);
                        state = stateMatch.Success ? stateMatch.Groups["id"].Value : StripTags(cells[1]);
                        var rowspan = RowspanRegex().Match(cells[1]);
                        spanState = state;
                        spanRemaining = rowspan.Success ? int.Parse(rowspan.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) - 1 : 0;
                    }

                    if (state.Length == 0)
                        continue;

                    definition.AddState(state);
                    var keywordCell = cells[0];
                    foreach (Match code in CodeRegex().Matches(keywordCell))
                    {
                        definition.Keywords[StripTags(code.Groups["text"].Value).ToLowerInvariant()] = state;
                    }

                    if (StripTags(keywordCell).Contains("the empty string", StringComparison.Ordinal))
                    {
                        definition.Keywords[""] = state;
                    }

                    AddOwners(owners, keywordCell);
                }
            }
            else
            {
                // Each keyword maps to a state of its own
                foreach (Match keyword in AttrValueDfnRegex().Matches(section))
                {
                    var code = CodeWithIdRegex().Match(keyword.Value);
                    if (!code.Success)
                        continue;

                    var state = code.Groups["id"].Value;
                    definition.AddState(state);
                    definition.Keywords[StripTags(code.Groups["text"].Value).ToLowerInvariant()] = state;
                    AddOwners(owners, keyword.Value);
                }
            }

            if (definition.Keywords.Count == 0 || owners.Count == 0)
                continue;

            // Attributes sharing keywords can have different defaults (e.g. method and formmethod): one definition per attribute name
            foreach (var group in owners.GroupBy(o => o.Split('/')[1]))
            {
                var attributeDefinition = definition;
                if (owners.Select(o => o.Split('/')[1]).Distinct().Count() > 1)
                {
                    attributeDefinition = new EnumeratedAttributeDefinition();
                    definition.States.ForEach(attributeDefinition.AddState);
                    foreach (var (keyword, state) in definition.Keywords)
                    {
                        attributeDefinition.Keywords[keyword] = state;
                    }
                }

                ParseDefaults(attributeDefinition, section, group.Key);
                attributeDefinition.Source = string.Join(",", group.Order(StringComparer.Ordinal));
                foreach (var owner in group)
                {
                    EnumeratedAttributes.TryAdd(owner, attributeDefinition);
                }
            }
        }
    }

    /// <summary>Parses the defaults given in the definition of the attribute when they are not with the keywords (e.g. img decoding).</summary>
    private void ParseAttributeDefaults()
    {
        foreach (Match paragraph in ParagraphRegex().Matches(_source))
        {
            var html = paragraph.Groups["content"].Value;
            if (!html.Contains("value default", StringComparison.Ordinal))
                continue;

            foreach (Match attribute in ElementAttrDfnRegex().Matches(html))
            {
                var name = StripTags(attribute.Groups["name"].Value);
                foreach (var element in attribute.Groups["for"].Value.Split(','))
                {
                    if (EnumeratedAttributes.TryGetValue(element.Trim() + "/" + name, out var definition) && definition.MissingValueDefault is null && definition.InvalidValueDefault is null)
                    {
                        ParseDefaults(definition, paragraph.Value);
                    }
                }
            }
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/urls-and-fetching.html#referrer-policy-attribute</summary>
    private void AddReferrerPolicyAttributes(IReadOnlyList<string> referrerPolicies)
    {
        var definition = new EnumeratedAttributeDefinition { Source = "referrer policy attribute" };
        foreach (var policy in referrerPolicies)
        {
            var state = "referrer-policy-" + (policy.Length == 0 ? "empty" : policy);
            definition.AddState(state);
            definition.Keywords[policy] = state;
        }

        definition.MissingValueDefault = definition.InvalidValueDefault = "referrer-policy-empty";
        foreach (Match attribute in ElementAttrDfnRegex().Matches(_source))
        {
            if (StripTags(attribute.Groups["name"].Value) != "referrerpolicy")
                continue;

            foreach (var element in attribute.Groups["for"].Value.Split(','))
            {
                EnumeratedAttributes.TryAdd(element.Trim() + "/referrerpolicy", definition);
            }
        }
    }

    /// <summary>The link as attribute: its keywords are the potential destinations of Fetch, and it has no default state.</summary>
    private void AddLinkAsAttribute()
    {
        var definition = new EnumeratedAttributeDefinition { Source = "link/as" };
        foreach (var destination in PotentialDestinations)
        {
            definition.AddState("destination-" + destination);
            definition.Keywords[destination] = "destination-" + destination;
        }

        EnumeratedAttributes.TryAdd("link/as", definition);
    }

    private static void AddOwners(HashSet<string> owners, string html)
    {
        foreach (Match forMatch in AttrValueForRegex().Matches(html))
        {
            foreach (var owner in forMatch.Groups["for"].Value.Split(','))
            {
                owners.Add(owner.Trim());
            }
        }
    }

    /// <summary>Returns the end of the text that describes the enumerated attribute (the next heading, table or attribute definition).</summary>
    private int FindEndOfDefinition(int start)
    {
        var candidates = new[] { "<h2", "<h3", "<h4", "<h5", "<h6", "<dfn element-attr", "<hr>" };
        var end = Math.Min(_source.Length, start + 8000);
        foreach (var candidate in candidates)
        {
            var index = _source.IndexOf(candidate, start, end - start, StringComparison.Ordinal);
            if (index >= 0 && index < end)
            {
                end = index;
            }
        }

        return end;
    }

    private static void ParseDefaults(EnumeratedAttributeDefinition definition, string text, string? attributeName = null)
    {
        // The paragraphs about this attribute ("The <code>formmethod</code> attribute has no missing value default...") take
        // precedence over the generic ones ("The attribute's missing value default...")
        var generic = new List<string>();
        var specific = new List<string>();
        foreach (Match paragraph in ParagraphRegex().Matches(text))
        {
            var html = Normalize(paragraph.Groups["content"].Value);
            if (!html.Contains("value default", StringComparison.Ordinal))
                continue;

            // The data-x of <i data-x="missing value default"> would be seen as a second occurrence of the default kind
            html = ItalicRegex().Replace(html, "<i>");
            var subject = DefaultsSubjectRegex().Match(html);
            if (!subject.Success || attributeName is null)
            {
                generic.Add(html);
            }
            else if (StripTags(subject.Groups["name"].Value) == attributeName)
            {
                specific.Add(html);
            }
        }

        var excluded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var html in specific.Concat(generic))
        {
            // Tokens, in order: default kinds ("missing value default"...), "no" markers, and state references
            var pending = new List<string>();
            var negated = false;
            foreach (Match token in DefaultTokenRegex().Matches(html))
            {
                if (token.Groups["kind"].Success)
                {
                    var kind = token.Groups["kind"].Value;
                    if (negated)
                    {
                        // "has no missing value default"
                        excluded.Add(kind);
                        negated = false;
                        continue;
                    }

                    pending.Add(kind);
                }
                else if (token.Groups["no"].Success)
                {
                    negated = true;
                }
                else if (token.Groups["state"].Success)
                {
                    var state = token.Groups["state"].Value;
                    if (pending.Count == 0)
                        continue;

                    definition.AddState(state);
                    foreach (var kind in pending)
                    {
                        if (excluded.Contains(kind))
                            continue;

                        switch (kind)
                        {
                            case "missing":
                                definition.MissingValueDefault ??= state;
                                break;
                            case "invalid":
                                definition.InvalidValueDefault ??= state;
                                break;
                            case "empty":
                                definition.EmptyValueDefault ??= state;
                                break;
                        }
                    }

                    pending.Clear();
                }
            }
        }
    }

    private void ParseLimitedReflections()
    {
        foreach (Match paragraph in ParagraphRegex().Matches(_source))
        {
            var html = Normalize(paragraph.Groups["content"].Value);
            if (!html.Contains("limited to only known values</span>", StringComparison.Ordinal) || !html.Contains("<span>reflect</span>", StringComparison.Ordinal))
                continue;

            // Each sentence: "The <dfn attribute for=Interface><code>idl</code></dfn> [and ...] IDL attribute(s) must reflect the <code>content</code> content attribute(s), limited to only known values."
            foreach (var sentence in html.Split(". ", StringSplitOptions.None))
            {
                if (!sentence.Contains("limited to only known values", StringComparison.Ordinal))
                    continue;

                var idlAttributes = IdlDfnRegex().Matches(sentence).Select(m => (Interface: m.Groups["for"].Value, Name: StripTags(m.Groups["name"].Value))).ToList();
                if (idlAttributes.Count == 0)
                    continue;

                var reflectIndex = sentence.IndexOf("<span>reflect</span>", StringComparison.Ordinal);
                var afterReflect = sentence[reflectIndex..];
                var contentNames = ContentAttributeRegex().Matches(afterReflect).Select(m => StripTags(m.Groups["name"].Value)).ToList();
                var sameName = afterReflect.Contains("of the same name", StringComparison.Ordinal);
                for (var i = 0; i < idlAttributes.Count; i++)
                {
                    var (interfaceNames, name) = idlAttributes[i];
                    var contentName = sameName ? name.ToLowerInvariant() : contentNames.Count > i ? contentNames[i] : contentNames.FirstOrDefault() ?? name.ToLowerInvariant();
                    foreach (var interfaceName in interfaceNames.Split(','))
                    {
                        LimitedReflections.TryAdd(interfaceName.Trim() + "." + name, contentName);
                    }
                }
            }
        }
    }

    [GeneratedRegex(@"^<td[^>]*\browspan=""?(?<n>[0-9]+)")]
    private static partial Regex RowspanRegex();

    [GeneratedRegex(@"<dfn attribute for=""(?<for>[^""]+)""[^>]*\bdata-x=""(?<id>[^""]+)""[^>]*>(?<name>.*?)</dfn>", RegexOptions.Singleline)]
    private static partial Regex IdlDfnWithIdRegex();

    [GeneratedRegex(@"^The <code data-x=""(?<id>[^""]+)"">[^<]*</code> IDL attribute is <span>limited to only (?<kind>positive numbers with fallback|positive numbers|non-negative numbers)</span>(?: and has a <span>default value</span> of (?<default>[0-9.]+))?\.$")]
    private static partial Regex NumericReflectionRegex();

    [GeneratedRegex(@"^\s*The <code[^>]*>(?<name>[^<]+)</code> (?:attribute|content attribute)")]
    private static partial Regex DefaultsSubjectRegex();

    [GeneratedRegex(@"<i\b[^>]*>")]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"<table[^>]*>\s*<thead>(?<head>.*?)<tbody>(?<body>.*?)</table>", RegexOptions.Singleline)]
    private static partial Regex TableRegex();

    [GeneratedRegex(@"<(?:dfn|span)[^>]*\bdata-x=""(?<id>[^""]+)""[^>]*>", RegexOptions.Singleline)]
    private static partial Regex StateRegex();

    [GeneratedRegex(@"<code[^>]*>(?<text>.*?)</code>", RegexOptions.Singleline)]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"attr-value\s+for=""(?<for>[^""]+)""", RegexOptions.Singleline)]
    private static partial Regex AttrValueForRegex();

    [GeneratedRegex(@"<p[^>]*>(?<content>.*?)</p>", RegexOptions.Singleline)]
    private static partial Regex ParagraphRegex();

    [GeneratedRegex(@"(?<kind>missing|invalid|empty)(?=\s+value\s+default)|\b(?<no>no)\s+<i|<(?:dfn|span|code)[^>]*\bdata-x=""(?<state>[^""]+)""")]
    private static partial Regex DefaultTokenRegex();

    [GeneratedRegex(@"enumerated\s+attribute|decoding\s+hint</dfn>\s+keywords")]
    private static partial Regex EnumeratedIntroRegex();

    [GeneratedRegex(@"<dfn element-attr for=""(?<for>[^""]+)""[^>]*>(?<name>.*?)</dfn>", RegexOptions.Singleline)]
    private static partial Regex ElementAttrDfnRegex();

    [GeneratedRegex(@"<dfn attr-value for=""[^""]+""[^>]*>.*?</dfn>", RegexOptions.Singleline)]
    private static partial Regex AttrValueDfnRegex();

    [GeneratedRegex(@"<code[^>]*\bdata-x=""(?<id>[^""]+)""[^>]*>(?<text>.*?)</code>", RegexOptions.Singleline)]
    private static partial Regex CodeWithIdRegex();

    [GeneratedRegex(@"<dfn attribute for=""(?<for>[^""]+)""[^>]*>(?<name>.*?)</dfn>", RegexOptions.Singleline)]
    private static partial Regex IdlDfnRegex();

    [GeneratedRegex(@"<code data-x=""attr-[^""]*"">(?<name>.*?)</code>", RegexOptions.Singleline)]
    private static partial Regex ContentAttributeRegex();
}
