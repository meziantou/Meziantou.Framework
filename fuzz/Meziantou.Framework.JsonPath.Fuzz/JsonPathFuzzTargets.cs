using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Json;
using JsonPathQuery = Meziantou.Framework.Json.JsonPath;

namespace Meziantou.Framework.JsonPath.Fuzz;

/// <summary>
/// An invalid JSONPath expression must only throw <see cref="FormatException"/>, and the four parsing overloads must agree.
/// A valid one never throws in <see cref="JsonPathEvaluationMode.Lax"/> mode on a document of System.Text.Json (whose depth
/// is far below the evaluation limit), gives the same matches over <see cref="JsonNode"/> and <see cref="JsonDocument"/>, and
/// the normalized path of every match selects exactly that value (RFC 9535 section 2.7). Strict mode either throws
/// <see cref="JsonPathEvaluationException"/> or gives the matches of Lax mode.
/// </summary>
public static class JsonPathFuzzTargets
{
    // Separates the expression from the document: neither JSON nor JSONPath accepts a raw NUL, so both may hold line breaks
    private const byte Separator = 0;

    // Only the first matches go through the normalized-path check, which evaluates one query per match
    private const int MaxCheckedNormalizedPaths = 16;

    private static readonly string[] Tokens =
    [
        "$", "@", ".", "..", "*", "[", "]", "[*]", "[0]", "[-1]", "[1:3]", "[::-1]", "[::2]", ":", ",", "?", "[?", "(", ")", "'", "\"", "\\", "\\'", "\\u0041", "\\uD83D\\uDE00", "\\b", "\\f", "\\n", "\\r", "\\t", "\\/",
        "==", "!=", "<", "<=", ">", ">=", "&&", "||", "!", "true", "false", "null", "length(", "count(", "match(", "search(", "value(", "1e3", "-0", "0.5", "9007199254740991", "-9007199254740991",
        "\\p{L}", "\\P{Nd}", "[^a]", ".*", "a{2,3}", "|", "+", "?", " ", "\t", "\n", "\r",
        "{", "}", "\":", "{\"a\":1}", "[1,2]", "\"a\"", "1.5", "[]", "{}",
    ];

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        // A JsonObject rejects duplicate names, whereas a JsonDocument keeps them: only compare documents both accept
        AllowDuplicateProperties = false,
    };

    // The document that the parse target evaluates each valid expression against
    private const string SampleDocument = """
        {"a":[1,2.5,"b",true,false,null,{"c":"d","e":[3,4]},[5,[6]]],"o":{"a":{"b":0},"":-1,"k\u00e9":"v","x y":[{"p":1},{"p":"1"},{"q":null}]},"n":-0.0,"s":"ab\ncd","e":[],"m":{}}
        """;

    private static readonly byte[] SampleDocumentUtf8 = Encoding.UTF8.GetBytes(SampleDocument);

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("jsonpath-parse", Parse) { Seeds = GetParseSeeds, Dictionary = Tokens },
        new("jsonpath-evaluate", Evaluate) { Seeds = GetEvaluateSeeds, Dictionary = Tokens },
    ];

    private static void Parse(ReadOnlySpan<byte> data)
    {
        var expression = FuzzText.Decode(data);
        if (ParseExpression(expression) is { } path)
        {
            EvaluateAndCheck(path, SampleDocumentUtf8);
        }
    }

    private static void Evaluate(ReadOnlySpan<byte> data)
    {
        var expression = FuzzText.Decode(FuzzInput.ReadUntil(ref data, Separator));
        if (ParseExpression(expression) is { } path)
        {
            // The document is parsed from its UTF-8 bytes: a string holding an unpaired surrogate makes System.Text.Json
            // throw an ArgumentException before parsing starts
            EvaluateAndCheck(path, data.ToArray());
        }
    }

    private static JsonPathQuery? ParseExpression(string expression)
    {
        JsonPathQuery? path;
        try
        {
            path = JsonPathQuery.Parse(expression);
        }
        catch (FormatException)
        {
            path = null;
        }

        FuzzAssert.Equal(path is not null, JsonPathQuery.TryParse(expression, out var tryParsed), "Parse and TryParse disagree");
        FuzzAssert.Equal(path is not null, JsonPathQuery.TryParse(expression.AsSpan(), out var tryParsedSpan), "Parse and TryParse(ReadOnlySpan<char>) disagree");

        bool spanParsed;
        try
        {
            _ = JsonPathQuery.Parse(expression.AsSpan());
            spanParsed = true;
        }
        catch (FormatException)
        {
            spanParsed = false;
        }

        FuzzAssert.Equal(path is not null, spanParsed, "Parse and Parse(ReadOnlySpan<char>) disagree");
        if (path is not null)
        {
            FuzzAssert.Equal(expression, path.ToString(), "ToString does not return the expression");
            FuzzAssert.Equal(expression, tryParsed!.ToString(), "ToString does not return the expression (TryParse)");
            FuzzAssert.Equal(expression, tryParsedSpan!.ToString(), "ToString does not return the expression (TryParse span)");
        }

        return path;
    }

    private static void EvaluateAndCheck(JsonPathQuery path, byte[] json)
    {
        // System.Text.Json only validates the UTF-8 of a string when it reads it, so a JsonDocument and a JsonNode built
        // from invalid UTF-8 throw an InvalidOperationException later, from their own accessors
        if (!Utf8.IsValid(json))
            return;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            // The duplicate property check reads the property names, and an unpaired surrogate in one throws
            return;
        }

        using (document)
        {
            if (!HasValidStrings(document.RootElement))
                return;

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(json, documentOptions: DocumentOptions);
            }
            catch (JsonException)
            {
                return;
            }

            var nodeMatches = path.Evaluate(node);
            var elementMatches = path.Evaluate(document);
            FuzzAssert.Equal(nodeMatches.Count, elementMatches.Count, "The JsonNode and JsonDocument navigators give a different number of matches");
            for (var i = 0; i < nodeMatches.Count; i++)
            {
                FuzzAssert.Equal(nodeMatches[i].Path, elementMatches[i].Path, $"The JsonNode and JsonDocument navigators give different paths for match {i}");
                FuzzAssert.True(DeepEquals(nodeMatches[i].Value, elementMatches[i].Value), $"The JsonNode and JsonDocument navigators give different values at {nodeMatches[i].Path}");
            }

            var firstValue = path.EvaluateValue(node);
            FuzzAssert.True(ReferenceEquals(firstValue, nodeMatches.Count > 0 ? nodeMatches[0].Value : null), "EvaluateValue does not return the first match");

            var firstElement = path.EvaluateValue(document);
            FuzzAssert.Equal(elementMatches.Count > 0, firstElement.HasValue, "EvaluateValue(JsonDocument) does not return the first match");

            CheckStrictMode(path, node, nodeMatches);
            CheckNormalizedPaths(node, nodeMatches);
        }
    }

    private static void CheckStrictMode(JsonPathQuery path, JsonNode? node, JsonPathResult laxMatches)
    {
        JsonPathResult strictMatches;
        try
        {
            strictMatches = path.Evaluate(node, JsonPathEvaluationMode.Strict);
        }
        catch (JsonPathEvaluationException)
        {
            return;
        }

        FuzzAssert.Equal(Format(laxMatches), Format(strictMatches), "Strict mode gives different matches than Lax mode without reporting an error");
    }

    private static void CheckNormalizedPaths(JsonNode? root, JsonPathResult matches)
    {
        var count = Math.Min(matches.Count, MaxCheckedNormalizedPaths);
        for (var i = 0; i < count; i++)
        {
            var match = matches[i];
            FuzzAssert.True(JsonPathQuery.TryParse(match.Path, out var normalizedPath), $"The normalized path {FuzzAssert.Format(match.Path)} is not a valid query");

            var result = normalizedPath.Evaluate(root);
            FuzzAssert.Equal(1, result.Count, $"The normalized path {FuzzAssert.Format(match.Path)} does not select exactly one node");
            FuzzAssert.Equal(match.Path, result[0].Path, "The normalized path of a normalized path differs");
            FuzzAssert.True(ReferenceEquals(match.Value, result[0].Value), $"The normalized path {FuzzAssert.Format(match.Path)} selects another node");
        }
    }

    // System.Text.Json accepts an escaped unpaired surrogate ("\uD800") when it parses a document, and throws an
    // InvalidOperationException when the string is read, so the navigators cannot read such a document
    private static bool HasValidStrings(JsonElement element)
    {
        try
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    _ = element.GetString();
                    return true;
                case JsonValueKind.Array:
                    return element.EnumerateArray().All(HasValidStrings);
                case JsonValueKind.Object:
                    return element.EnumerateObject().All(property => property.Name is not null && HasValidStrings(property.Value));
                default:
                    return true;
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool DeepEquals(JsonNode? node, JsonElement element)
    {
        if (node is null)
            return element.ValueKind is JsonValueKind.Null;

        return JsonNode.DeepEquals(node, JsonNode.Parse(element.GetRawText(), documentOptions: DocumentOptions));
    }

    private static string Format(JsonPathResult result)
    {
        var builder = new StringBuilder();
        foreach (var match in result)
        {
            builder.Append(match.Path).Append(" = ").Append(match.Value?.ToJsonString() ?? "null").Append('\n');
        }

        return builder.ToString();
    }

    private static IEnumerable<FuzzSeed> GetParseSeeds()
    {
        using var document = FuzzResources.ReadJson(typeof(JsonPathFuzzTargets).Assembly, "cts.json");
        foreach (var test in document.RootElement.GetProperty("tests").EnumerateArray())
        {
            yield return FuzzSeed.FromText(test.GetProperty("name").GetString()!, test.GetProperty("selector").GetString()!);
        }
    }

    private static IEnumerable<FuzzSeed> GetEvaluateSeeds()
    {
        using var document = FuzzResources.ReadJson(typeof(JsonPathFuzzTargets).Assembly, "cts.json");
        foreach (var test in document.RootElement.GetProperty("tests").EnumerateArray())
        {
            var json = test.TryGetProperty("document", out var value) ? value.GetRawText() : SampleDocument;
            yield return new FuzzSeed(test.GetProperty("name").GetString()!, (byte[])[.. FuzzText.Encode(test.GetProperty("selector").GetString()!), Separator, .. FuzzText.Encode(json)]);
        }
    }
}
