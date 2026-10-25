using System.Diagnostics.CodeAnalysis;
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
/// <see cref="JsonPathEvaluationException"/> or gives the matches of Lax mode, and it reports an error on a
/// <see cref="JsonDocument"/> exactly when it does on a <see cref="JsonNode"/>. A custom <see cref="JsonPathNavigator{TValue}"/>
/// over another object model gives the matches of the <see cref="JsonNode"/> navigator in both modes.
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

            CheckToJsonArray(nodeMatches);
            var strictFails = CheckStrictMode(path, node, nodeMatches);
            CheckStrictModeOnDocument(path, document, elementMatches, strictFails);
            CheckCustomNavigator(path, node, nodeMatches, strictFails);
            CheckNormalizedPaths(node, nodeMatches);
        }
    }

    private static void CheckToJsonArray(JsonPathResult matches)
    {
        var array = matches.ToJsonArray();
        FuzzAssert.Equal(matches.Count, array.Count, "ToJsonArray does not hold one value per match");
        for (var i = 0; i < matches.Count; i++)
        {
            FuzzAssert.True(AreEqual(matches[i].Value, array[i]), $"ToJsonArray gives another value for {matches[i].Path}");
            FuzzAssert.True(array[i] is null || !ReferenceEquals(matches[i].Value, array[i]), $"ToJsonArray does not copy the value of {matches[i].Path}");
        }
    }

    // Returns whether Strict mode reports an error
    private static bool CheckStrictMode(JsonPathQuery path, JsonNode? node, JsonPathResult laxMatches)
    {
        if (!TryEvaluateStrict(() => path.Evaluate(node, JsonPathEvaluationMode.Strict), out var strictMatches))
        {
            FuzzAssert.False(TryEvaluateStrict(() => path.EvaluateValue(node, JsonPathEvaluationMode.Strict), out _), "EvaluateValue(JsonNode, Strict) does not report the error of Evaluate");
            return true;
        }

        FuzzAssert.Equal(Format(laxMatches), Format(strictMatches), "Strict mode gives different matches than Lax mode without reporting an error");
        FuzzAssert.True(TryEvaluateStrict(() => path.EvaluateValue(node, JsonPathEvaluationMode.Strict), out var firstValue), "EvaluateValue(JsonNode, Strict) reports an error that Evaluate does not");
        FuzzAssert.True(ReferenceEquals(firstValue, laxMatches.Count > 0 ? laxMatches[0].Value : null), "EvaluateValue(JsonNode, Strict) does not return the first match");
        return false;
    }

    // Strict mode on a JsonDocument or a JsonElement reports an error exactly when it does on a JsonNode, and otherwise
    // gives the matches of Lax mode
    private static void CheckStrictModeOnDocument(JsonPathQuery path, JsonDocument document, JsonPathResult<JsonElement> laxMatches, bool strictFails)
    {
        var root = document.RootElement;
        CheckStrictElementMatches("Evaluate(JsonDocument, Strict)", () => path.Evaluate(document, JsonPathEvaluationMode.Strict));
        CheckStrictElementMatches("Evaluate(JsonElement, Strict)", () => path.Evaluate(root, JsonPathEvaluationMode.Strict));
        CheckStrictElementMatches("JsonElement.Evaluate(path, Strict)", () => root.Evaluate(path, JsonPathEvaluationMode.Strict));
        CheckStrictElementValue("EvaluateValue(JsonDocument, Strict)", () => path.EvaluateValue(document, JsonPathEvaluationMode.Strict));
        CheckStrictElementValue("EvaluateValue(JsonElement, Strict)", () => path.EvaluateValue(root, JsonPathEvaluationMode.Strict));

        void CheckStrictElementMatches(string method, Func<JsonPathResult<JsonElement>> evaluate)
        {
            var succeeded = TryEvaluateStrict(evaluate, out var strictMatches);
            FuzzAssert.Equal(strictFails, !succeeded, $"{method} and Evaluate(JsonNode, Strict) disagree on reporting an error");
            if (!succeeded)
                return;

            FuzzAssert.Equal(laxMatches.Count, strictMatches!.Count, $"{method} gives a different number of matches than Lax mode");
            for (var i = 0; i < laxMatches.Count; i++)
            {
                FuzzAssert.Equal(laxMatches[i].Path, strictMatches[i].Path, $"{method} gives a different path than Lax mode for match {i}");
                FuzzAssert.True(AreEqual(laxMatches[i].Value, strictMatches[i].Value), $"{method} gives a different value than Lax mode at {laxMatches[i].Path}");
            }
        }

        void CheckStrictElementValue(string method, Func<JsonElement?> evaluate)
        {
            var succeeded = TryEvaluateStrict(evaluate, out var value);
            FuzzAssert.Equal(strictFails, !succeeded, $"{method} and Evaluate(JsonNode, Strict) disagree on reporting an error");
            if (!succeeded)
                return;

            FuzzAssert.Equal(laxMatches.Count > 0, value.HasValue, $"{method} does not return the first match");
            if (value is { } element)
            {
                FuzzAssert.True(AreEqual(laxMatches[0].Value, element), $"{method} does not return the first match");
            }
        }
    }

    // A navigator over another object model gives the matches of the JsonNode navigator, in both modes
    private static void CheckCustomNavigator(JsonPathQuery path, JsonNode? node, JsonPathResult nodeMatches, bool strictFails)
    {
        var tree = TreeValue.Create(node);
        CheckMatches("Lax", path.Evaluate(tree, TreeNavigator.Instance));
        CheckMatches("Lax", path.Evaluate(tree, TreeNavigator.Instance, JsonPathEvaluationMode.Lax));
        CheckFirstValue("Lax", path.EvaluateValue(tree, TreeNavigator.Instance));

        var succeeded = TryEvaluateStrict(() => path.Evaluate(tree, TreeNavigator.Instance, JsonPathEvaluationMode.Strict), out var strictMatches);
        FuzzAssert.Equal(strictFails, !succeeded, "The custom navigator and the JsonNode navigator disagree on reporting an error in Strict mode");
        var valueSucceeded = TryEvaluateStrict(() => path.EvaluateValue(tree, TreeNavigator.Instance, JsonPathEvaluationMode.Strict), out var strictValue);
        FuzzAssert.Equal(succeeded, valueSucceeded, "EvaluateValue(custom navigator, Strict) and Evaluate disagree on reporting an error");
        if (succeeded)
        {
            CheckMatches("Strict", strictMatches!);
            CheckFirstValue("Strict", strictValue);
        }

        void CheckMatches(string mode, JsonPathResult<TreeValue> matches)
        {
            FuzzAssert.Equal(nodeMatches.Count, matches.Count, $"The custom and JsonNode navigators give a different number of matches ({mode})");
            for (var i = 0; i < nodeMatches.Count; i++)
            {
                FuzzAssert.Equal(nodeMatches[i].Path, matches[i].Path, $"The custom and JsonNode navigators give different paths for match {i} ({mode})");
                FuzzAssert.True(matches[i].Value is { } value && ReferenceEquals(nodeMatches[i].Value, value.Source), $"The custom and JsonNode navigators give different values at {nodeMatches[i].Path} ({mode})");
            }
        }

        void CheckFirstValue(string mode, TreeValue? value)
        {
            FuzzAssert.Equal(nodeMatches.Count > 0, value is not null, $"EvaluateValue(custom navigator) does not return the first match ({mode})");
            if (value is not null)
            {
                FuzzAssert.True(ReferenceEquals(nodeMatches[0].Value, value.Source), $"EvaluateValue(custom navigator) does not return the first match ({mode})");
            }
        }
    }

    private static bool TryEvaluateStrict<T>(Func<T> evaluate, [MaybeNullWhen(false)] out T result)
    {
        try
        {
            result = evaluate();
            return true;
        }
        catch (JsonPathEvaluationException)
        {
            result = default;
            return false;
        }
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

        return AreEqual(node, JsonNode.Parse(element.GetRawText(), documentOptions: DocumentOptions));
    }

    // JsonNode.DeepEquals and JsonElement.DeepEquals throw ArgumentOutOfRangeException for a number whose exponent does not
    // fit in an int (0.1e01234567890123456789). The compared values are read from the same text, so their compact JSON is
    // the same when they are.
    private static bool AreEqual(JsonNode? left, JsonNode? right)
    {
        try
        {
            return JsonNode.DeepEquals(left, right);
        }
        catch (ArgumentOutOfRangeException)
        {
            return string.Equals(left?.ToJsonString(), right?.ToJsonString(), StringComparison.Ordinal);
        }
    }

    private static bool AreEqual(JsonElement left, JsonElement right)
    {
        try
        {
            return JsonElement.DeepEquals(left, right);
        }
        catch (ArgumentOutOfRangeException)
        {
            return AreEqual(JsonNode.Parse(left.GetRawText(), documentOptions: DocumentOptions), JsonNode.Parse(right.GetRawText(), documentOptions: DocumentOptions));
        }
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

    /// <summary>A minimal object model, built from a <see cref="JsonNode"/>, that the custom navigator walks.</summary>
    private sealed class TreeValue
    {
        private TreeValue(JsonPathNodeKind kind, JsonNode? source)
        {
            Kind = kind;
            Source = source;
        }

        public JsonPathNodeKind Kind { get; }

        // The node this value was built from, to compare the matches with the ones of the JsonNode navigator
        public JsonNode? Source { get; }

        public bool Boolean { get; private init; }

        // Null when the number cannot be represented as a double, like the JsonNode navigator
        public double? Number { get; private init; }

        public string? String { get; private init; }

        public TreeValue[] Elements { get; private init; } = [];

        public KeyValuePair<string, TreeValue>[] Properties { get; private init; } = [];

        public Dictionary<string, TreeValue> PropertiesByName { get; private init; } = [];

        // JSON null is a value of kind Null rather than a null reference, which the navigator also accepts
        public static TreeValue Create(JsonNode? node)
        {
            switch (node)
            {
                case null:
                    return new TreeValue(JsonPathNodeKind.Null, source: null);

                case JsonArray array:
                    return new TreeValue(JsonPathNodeKind.Array, node) { Elements = [.. array.Select(Create)] };

                case JsonObject obj:
                    KeyValuePair<string, TreeValue>[] properties = [.. obj.Select(property => KeyValuePair.Create(property.Key, Create(property.Value)))];
                    return new TreeValue(JsonPathNodeKind.Object, node)
                    {
                        Properties = properties,
                        PropertiesByName = properties.ToDictionary(StringComparer.Ordinal),
                    };

                default:
                    var element = node.GetValue<JsonElement>();
                    return element.ValueKind switch
                    {
                        JsonValueKind.True or JsonValueKind.False => new TreeValue(JsonPathNodeKind.Boolean, node) { Boolean = element.GetBoolean() },
                        JsonValueKind.Number => new TreeValue(JsonPathNodeKind.Number, node) { Number = element.TryGetDouble(out var number) ? number : null },
                        JsonValueKind.String => new TreeValue(JsonPathNodeKind.String, node) { String = element.GetString() },
                        _ => throw new FuzzAssertionException($"Unexpected value kind {element.ValueKind}"),
                    };
            }
        }
    }

    private sealed class TreeNavigator : JsonPathNavigator<TreeValue>
    {
        public static TreeNavigator Instance { get; } = new();

        public override JsonPathNodeKind GetKind(TreeValue? value) => value?.Kind ?? JsonPathNodeKind.Null;

        public override bool TryGetPropertyValue(TreeValue? value, string name, out TreeValue? result)
        {
            result = null;
            return value is { Kind: JsonPathNodeKind.Object } && value.PropertiesByName.TryGetValue(name, out result);
        }

        public override IEnumerable<JsonPathProperty<TreeValue>> GetProperties(TreeValue? value)
        {
            if (value is not { Kind: JsonPathNodeKind.Object })
                return [];

            return value.Properties.Select(property => new JsonPathProperty<TreeValue>(property.Key, property.Value));
        }

        public override int GetArrayLength(TreeValue? value) => value is { Kind: JsonPathNodeKind.Array } ? value.Elements.Length : 0;

        public override bool TryGetElement(TreeValue? value, int index, out TreeValue? result)
        {
            if (value is { Kind: JsonPathNodeKind.Array } && (uint)index < (uint)value.Elements.Length)
            {
                result = value.Elements[index];
                return true;
            }

            result = null;
            return false;
        }

        public override bool TryGetString(TreeValue? value, out string? result)
        {
            result = value?.String;
            return value is { Kind: JsonPathNodeKind.String };
        }

        public override bool TryGetNumber(TreeValue? value, out double result)
        {
            if (value is { Kind: JsonPathNodeKind.Number, Number: { } number })
            {
                result = number;
                return true;
            }

            result = 0;
            return false;
        }

        public override bool TryGetBoolean(TreeValue? value, out bool result)
        {
            result = value?.Boolean ?? false;
            return value is { Kind: JsonPathNodeKind.Boolean };
        }
    }
}
