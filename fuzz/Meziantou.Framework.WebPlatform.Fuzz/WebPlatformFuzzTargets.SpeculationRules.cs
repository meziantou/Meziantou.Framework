using System.Text;
using System.Text.Json;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// The parser of the speculation rules (https://html.spec.whatwg.org/multipage/speculative-loading.html#speculation-rules-parsing)
public static partial class WebPlatformFuzzTargets
{
    private static readonly Lazy<Document> SpeculationRulesDocument = new(() => HtmlDocumentParser.ParseDocument("<a href=/a>a</a>"));

    /// <summary>The tokens of the speculation rules target: the JSON syntax, the keys of the rules and the escapes of the surrogates.</summary>
    private static string[] SpeculationRulesTokens =>
    [
        "{", "}", "[", "]", ":", ",", "\"", "\\\"", "\\\\", "\\u", "\\uD800", "\\uDC00", "\\uDEAD", "\\uFFFD", "true", "null",
        "\"prefetch\"", "\"prerender\"", "\"source\"", "\"list\"", "\"document\"", "\"urls\"", "\"where\"", "\"href_matches\"", "\"selector_matches\"",
        "\"and\"", "\"or\"", "\"not\"", "\"relative_to\"", "\"eagerness\"", "\"tag\"", "\"requires\"", "\"referrer_policy\"", "\"target_hint\"",
        "\"expects_no_vary_search\"", "\"pathname\"",
    ];

    /// <summary>
    /// Parses a speculation rule set, and checks the properties of the result: the lone surrogates of the JSON text (characters
    /// or escapes) are replaced, so that every string of the parsed JSON can be read and the replacement is idempotent, and
    /// the URLs of the list rules are HTTP(S) URLs. A text that is not JSON, or whose rules are not valid, is rejected with the
    /// documented exceptions.
    /// </summary>
    public static void SpeculationRules(string input)
    {
        var replaced = SpeculationRuleSet.ReplaceLoneSurrogates(input);
        FuzzAssert.Equal(replaced, SpeculationRuleSet.ReplaceLoneSurrogates(replaced), $"Replacing the lone surrogates of {FuzzAssert.Format(input)} again");
        FuzzAssert.Equal(replaced, Infra.InfraString.ToScalarValueString(replaced), $"The replacement of the lone surrogates of {FuzzAssert.Format(input)} has a lone surrogate");
        try
        {
            using var json = JsonDocument.Parse(replaced, new JsonDocumentOptions { MaxDepth = 512 });
            ReadStrings(json.RootElement);
        }
        catch (JsonException)
        {
        }

        SpeculationRuleSet ruleSet;
        try
        {
            ruleSet = SpeculationRuleSet.Parse(input, SpeculationRulesDocument.Value, UrlParser.Parse("https://example.com/dir/page", baseUrl: null)!);
        }
        catch (JsonException)
        {
            return;
        }
        catch (TypeErrorException)
        {
            return;
        }

        foreach (var rule in ruleSet.PrefetchRules.Concat(ruleSet.PrerenderRules))
        {
            foreach (var url in rule.Urls)
            {
                FuzzAssert.True(url.Scheme is "http" or "https", $"The rule set {FuzzAssert.Format(input)} has the URL {FuzzAssert.Format(url.Serialize())}");
            }
        }

        static void ReadStrings(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    _ = element.GetString();
                    break;

                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        ReadStrings(item);
                    }

                    break;

                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        _ = property.Name;
                        ReadStrings(property.Value);
                    }

                    break;
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetSpeculationRulesSeeds()
    {
        string[] inputs =
        [
            """{"prefetch": [{"source": "list", "urls": ["/a", "https://other.example/b"]}]}""",
            """{"prefetch": [{"source": "document", "where": {"href_matches": "/a\uDEAD"}}]}""",
            """{"prefetch": [{"source": "document", "where": {"href_matches": {"pathname": "/a\uDEAD"}}}]}""",
            """{"prefetch": [{"source": "document", "where": {"and": [{"selector_matches": ":scope a"}, {"not": {"href_matches": "/b"}}]}}]}""",
            """{"prefetch": [{"source": "document", "where": {"href_matches": "/a𐀀", "relative_to": "document"}}], "prerender": [{"source": "list", "urls": ["/c"], "eagerness": "eager", "target_hint": "_blank"}]}""",
            """{"tag": "a\uDEAD", "prefetch": [{"source": "document", "where": {"invalid": "\uDEAD"}}, {"source": "list", "urls": ["/d"]}]}""",
            """{"prefetch": [{"source": "list", "urls": ["\\uDEAD", "\\\uDEAD"]}]}""",
            "[]",
            "{",
        ];
        return inputs.Select((input, index) => new FuzzSeed($"speculation-rules-{index}", Encoding.UTF8.GetBytes(input)));
    }
}
