using System.Text.Json.Nodes;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Json;
using Meziantou.Framework.Language.Json;
using JsonPathQuery = Meziantou.Framework.Json.JsonPath;

namespace Meziantou.Framework.Language.Fuzz;

/// <summary>
/// JSON documents: the shared invariants, the values of the nodes, the agreement with System.Text.Json about which
/// documents are valid, and the incremental reparse, which reuses the nodes of the old tree and must give the tree a full
/// parse of the new text gives. JSONPath queries over the syntax tree never throw (Strict mode only throws
/// <see cref="JsonPathEvaluationException"/>), whatever the tree holds, and give the matches of a query over the
/// <see cref="JsonNode"/> of a valid document.
/// </summary>
internal static class JsonFuzzTargets
{
    /// <summary>Separates the document from the text the edit inserts (an invalid UTF-8 byte, so it never ends a character).</summary>
    private const byte InsertedTextSeparator = 0xFF;

    /// <summary>Separates the JSONPath expression from the document: neither JSON nor JSONPath accepts a raw NUL.</summary>
    private const byte JsonPathSeparator = 0;

    private const int GetMemberSampleCount = 64;

    /// <summary>Only the first matches go through the normalized-path check, which evaluates one query per match.</summary>
    private const int MaxCheckedNormalizedPaths = 16;

    /// <summary>
    /// The options System.Text.Json reads a document of the JSONPath target with: those of the syntax tree, and no
    /// duplicate names, which a <see cref="JsonObject"/> cannot hold (the syntax tree only warns about them).
    /// </summary>
    private static readonly System.Text.Json.JsonDocumentOptions JsonPathDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        MaxDepth = 256,
        AllowDuplicateProperties = false,
    };

    private static readonly string[] JsonPathSelectors =
    [
        "$", "$.*", "$..*", "$[*]", "$..[0]", "$[-1]", "$[::-1]", "$.a", "$['a','b']", "$..a", "$.items[1:]", "$[?@.x]", "$..[?@ == 1]", "$[?@ > 1]",
        "$..[?length(@) > 1]", "$..[?match(@, '[a-z]+')]", "$..[?@ == @]", "$..[?search(@, 'x')]", "$..[?count(@.*) == 2]", "$..[?value(@..x) == 1]",
    ];

    private static readonly System.Text.Json.JsonDocumentOptions SystemTextJsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        MaxDepth = 256,
    };

    private static readonly string[] Tokens =
    [
        "{", "}", "[", "]", ":", ",", "\"", "\"\"", "'", "\\", "\\\"", "\\\\", "\\/", "\\b", "\\f", "\\n", "\\r", "\\t", "\\u0041", "\\uD83D\\uDE00", "\\uD800", "\\u",
        "true", "false", "null", "NaN", "Infinity", "-Infinity", "0", "-0", "1.5", "1e10", "1E-5", "-1.25e+3", "0x1F", "01", "1.", ".5", "+1",
        "//", "/*", "*/", "\n", "\r\n", "\r", "\t", " ", "\f", "\u00A0", "\uFEFF", "\u2028", "{\"a\":", "[[[[", "]]]]", "\"name\": ",
    ];

    private static readonly string[] JsonPathTokens =
    [
        "$", "@", "..", "*", "[*]", "[0]", "[-1]", "[1:3]", "[::-1]", "?", "[?", "==", "!=", "<", "<=", ">", ">=", "&&", "||", "!",
        "length(", "count(", "match(", "search(", "value(", "\0",
    ];

    private static readonly string[] Documents =
    [
        "{\n  // the name of the thing\n  \"name\": \"value\",\n  \"items\": [1, 2,],\n}",
        "{\"a\": 1, \"b\": [true, false, null], \"c\": {\"d\": -1.5e+10, \"e\": \"\\u00e9\\n\\\"\"}}",
        "[1, 2, 3]",
        "\"just a string\"",
        "-0.0e-0",
        "/* leading */ {\"a\": /* inline */ 1} // trailing",
        "{\"a\": }",
        "{\"a\": [1, 2}",
        "{name: 1, 'other': 'value'}",
        "[0x1F, 1.2.3, -Infinity, +1, NaN, 01, 1., .5]",
        "{\"a\": 1 \"b\": 2}",
        "[1,,2]",
        "{\"a\":1,\"a\":2}",
        "\uFEFF{\"bom\": true}",
        "{\"tab\": \"a\tb\", \"ctrl\": \"\u0001\"}",
        "{\"unterminated\": \"abc\n}",
        "[\"\\uD83D\\uDE00\", \"\\uD800\", \"\\x\", \"\\u12\"]",
        "{} {}",
        "",
        "   ",
        "/* unterminated",
        "[[[[[[[[[[[[[[[[[[[[1]]]]]]]]]]]]]]]]]]]",
        "{\"a\"\f: 1,\u00A0\"b\": 2}",
        "{\"array\": [{\"x\": 1}, {\"y\": [2, 3]}, []], \"empty\": {}}",
    ];

    public static IReadOnlyList<FuzzTarget> Targets { get; } =
    [
        new("language-json", Parse) { Seeds = GetDocumentSeeds, Dictionary = Tokens },
        new("language-json-incremental", Incremental) { Seeds = GetIncrementalSeeds, Dictionary = Tokens },
        new("language-json-jsonpath", JsonPathEvaluate) { Seeds = GetJsonPathSeeds, Dictionary = [.. Tokens, .. JsonPathTokens] },
    ];

    private static void Parse(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        var tree = JsonSyntaxTree.ParseText(text);
        var shape = SyntaxTreeInvariants.Check(tree, text, "JSON", diagnosticsOnNodes: true, diagnosticsInSourceOrder: true);
        var root = tree.GetRoot();
        FuzzAssert.Equal(text, root.Values.ToFullString() + root.EndOfFileToken.ToFullString(), "The values and the end of the file do not reproduce the text");

        CheckValues(root);
        CheckAgainstSystemTextJson(tree, text);

        var walker = new CountingWalker();
        try
        {
            walker.Visit(root);
            SyntaxTreeInvariants.CheckWalker(shape, walker.TokenCount, walker.TriviaCount);
        }
        catch (InsufficientExecutionStackException)
        {
            // Documented by the walker for a tree nested too deeply to walk
        }
    }

    /// <summary>
    /// Bytes 0-1: where the edit starts. Byte 2: how many characters it removes. Then the document, then 0xFF and the
    /// text the edit inserts. The edit is then undone, which must give the tree of the original text back.
    /// </summary>
    private static void Incremental(ReadOnlySpan<byte> data)
    {
        var start = FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8);
        var removed = (int)FuzzInput.ReadByte(ref data);
        var originalText = FuzzText.Decode(FuzzInput.ReadUntil(ref data, InsertedTextSeparator));
        var inserted = FuzzText.Decode(data);

        start %= originalText.Length + 1;
        removed %= originalText.Length - start + 1;
        var removedText = originalText.Substring(start, removed);
        var newText = string.Concat(originalText.AsSpan(0, start), inserted, originalText.AsSpan(start + removed));
        var edit = $"replacing [{start}..{start + removed}) with {FuzzAssert.Format(inserted)} in {FuzzAssert.Format(originalText)}";

        var original = JsonSyntaxTree.ParseText(originalText);
        var edited = original.WithChanges(new TextChange(new TextSpan(start, removed), inserted));
        var full = AssertSameAsFullParse(edited, newText, edit);

        var changes = edited.GetChanges(original);
        FuzzAssert.Equal(newText, original.GetText().WithChanges(changes).Text, $"The changes GetChanges reports after {edit} do not turn the old text into the new one");

        var undone = edited.WithChanges(new TextChange(new TextSpan(start, inserted.Length), removedText));
        AssertSameAsFullParse(undone, originalText, "undoing " + edit);
        FuzzAssert.True(undone.IsEquivalentTo(original), $"Undoing {edit} does not give a tree equivalent to the original one");
        FuzzAssert.True(edited.IsEquivalentTo(full), $"The tree after {edit} is not equivalent to a full parse of the new text");
    }

    private static JsonSyntaxTree AssertSameAsFullParse(JsonSyntaxTree incremental, string expectedText, string context)
    {
        var full = JsonSyntaxTree.ParseText(expectedText);
        SyntaxTreeInvariants.Check(incremental, expectedText, "JSON", diagnosticsOnNodes: true, diagnosticsInSourceOrder: true);

        using var incrementalItems = incremental.GetRoot().DescendantNodesAndTokens().GetEnumerator();
        using var fullItems = full.GetRoot().DescendantNodesAndTokens().GetEnumerator();
        var index = 0;
        while (true)
        {
            var hasIncremental = incrementalItems.MoveNext();
            var hasFull = fullItems.MoveNext();
            FuzzAssert.Equal(hasFull, hasIncremental, $"After {context}, the incremental tree and a full parse have a different number of nodes and tokens");
            if (!hasFull)
                break;

            var expected = fullItems.Current;
            var actual = incrementalItems.Current;
            var item = $"After {context}, the item {index} ({expected.RawKind} at {expected.FullSpan})";
            FuzzAssert.Equal(expected.RawKind, actual.RawKind, $"{item} has the wrong kind");
            FuzzAssert.Equal(expected.FullSpan, actual.FullSpan, $"{item} has the wrong full span");
            FuzzAssert.Equal(expected.Span, actual.Span, $"{item} has the wrong span");
            FuzzAssert.Equal(expected.IsMissing, actual.IsMissing, $"{item} disagrees about being missing");
            FuzzAssert.Equal(expected.ContainsDiagnostics, actual.ContainsDiagnostics, $"{item} disagrees about containing diagnostics");
            if (expected.IsToken)
            {
                FuzzAssert.Equal(expected.ToFullString(), actual.ToFullString(), $"{item} has the wrong text");
            }

            index++;
        }

        FuzzAssert.Equal(Describe(full.GetDiagnostics()), Describe(incremental.GetDiagnostics()), $"After {context}, the incremental tree and a full parse have different diagnostics");
        return full;

        static string Describe(IReadOnlyList<Diagnostic> diagnostics)
            => string.Join('\n', diagnostics.Select(diagnostic => $"{diagnostic.Id} {diagnostic.Severity} {diagnostic.Location.SourceSpan} {diagnostic.Message}"));
    }

    private static void CheckValues(JsonDocumentSyntax root)
    {
        var getMemberBudget = GetMemberSampleCount;
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case JsonStringSyntax value:
                    _ = value.Value;
                    break;

                case JsonNumberSyntax value:
                    FuzzAssert.Equal(value.NumberToken.Text, value.Text, "The text of a number is not the text of its token");
                    break;

                case JsonMemberSyntax member:
                    var name = member.Name;
                    if (getMemberBudget > 0 && member.Parent is JsonObjectSyntax parent)
                    {
                        getMemberBudget--;
                        var found = parent.GetMember(name);
                        FuzzAssert.True(found is not null && string.Equals(found.Name, name, StringComparison.Ordinal), $"GetMember({FuzzAssert.Format(name)}) does not find a member of its object");
                    }

                    break;
            }
        }
    }

    /// <summary>A document without errors is one System.Text.Json reads, with comments and trailing commas allowed.</summary>
    private static void CheckAgainstSystemTextJson(JsonSyntaxTree tree, string text)
    {
        // System.Text.Json reads UTF-8, which cannot hold an unpaired surrogate
        if (HasUnpairedSurrogate(text))
            return;

        // A byte order mark is ignored by the parser, as RFC 8259 allows, but System.Text.Json reads it as a character
        var json = text.StartsWith('\uFEFF', StringComparison.Ordinal) ? text[1..] : text;
        string? error = null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json, SystemTextJsonOptions);
        }
        catch (System.Text.Json.JsonException ex)
        {
            // System.Text.Json rejects U+2028 and U+2029 inside a comment, as JavaScript would end a line comment there.
            // Comments are an extension of RFC 8259, and the parser reads them up to a line feed or a carriage return.
            if (ex.Message.Contains("line or paragraph separator", StringComparison.Ordinal))
                return;

            error = ex.Message;
        }

        var errors = tree.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        if (error is null)
        {
            FuzzAssert.True(errors.Count == 0, $"System.Text.Json reads the document but the parser reports {string.Join(", ", errors.Select(diagnostic => $"{diagnostic.Id} at {diagnostic.Location.SourceSpan}: {diagnostic.Message}"))}");
        }
        else
        {
            FuzzAssert.True(errors.Count > 0, $"System.Text.Json rejects the document ({error}) but the parser reports no error");
        }
    }

    private static bool HasUnpairedSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The JSONPath expression, then 0x00 and the document.</summary>
    private static void JsonPathEvaluate(ReadOnlySpan<byte> data)
    {
        var expression = FuzzText.Decode(FuzzInput.ReadUntil(ref data, JsonPathSeparator));
        var text = FuzzText.Decode(data);
        if (!JsonPathQuery.TryParse(expression, out var path))
            return;

        var tree = JsonSyntaxTree.ParseText(text);
        var root = tree.GetRoot();
        var query = $"{FuzzAssert.Format(expression)} over {FuzzAssert.Format(text)}";

        // Lax mode never throws, whatever the tree holds: a missing value reads as null
        var matches = path.Evaluate(tree);
        CheckSameMatches(matches, path.Evaluate(root), $"Evaluate(JsonDocumentSyntax) and Evaluate(JsonSyntaxTree) disagree for {query}");
        CheckSameMatches(matches, path.Evaluate((JsonSyntaxNode?)root.Value), $"Evaluate(root value) and Evaluate(JsonSyntaxTree) disagree for {query}");
        CheckSameMatches(matches, tree.Evaluate(path, JsonPathEvaluationMode.Lax), $"JsonSyntaxTree.Evaluate(path, Lax) and Evaluate(JsonSyntaxTree) disagree for {query}");
        var first = matches.Count > 0 ? matches[0].Value : null;
        FuzzAssert.True(ReferenceEquals(first, path.EvaluateValue(tree)), $"EvaluateValue(JsonSyntaxTree) is not the first match of {query}");
        FuzzAssert.True(ReferenceEquals(first, root.EvaluateValue(path)), $"EvaluateValue(JsonDocumentSyntax) is not the first match of {query}");

        JsonPathResult<JsonSyntaxNode>? strictMatches;
        try
        {
            strictMatches = path.Evaluate(tree, JsonPathEvaluationMode.Strict);
        }
        catch (JsonPathEvaluationException)
        {
            strictMatches = null;
        }

        if (strictMatches is not null)
        {
            CheckSameMatches(matches, strictMatches, $"Strict mode gives other matches than Lax mode without reporting an error for {query}");
        }

        // A tree without diagnostics, not even a duplicate name, is one System.Text.Json reads as a JsonNode
        if (tree.GetDiagnostics().Count > 0)
            return;

        // Normalized paths are only defined for strings of Unicode scalar values, which System.Text.Json requires too: a
        // name holding an unpaired surrogate has a normalized path that is not a valid query
        if (ParseJsonNode(text) is not { } document)
            return;

        CheckNormalizedPaths(tree, matches, query);
        var nodeMatches = path.Evaluate(document.Node);
        FuzzAssert.Equal(nodeMatches.Count, matches.Count, $"The syntax tree and JsonNode give a different number of matches for {query}");
        for (var i = 0; i < matches.Count; i++)
        {
            FuzzAssert.Equal(nodeMatches[i].Path, matches[i].Path, $"The syntax tree and JsonNode give different paths for the match {i} of {query}");
            var value = matches[i].Value;
            FuzzAssert.True(value is not null, $"The match {i} of {query} has no syntax node");
            FuzzAssert.True(JsonNode.DeepEquals(nodeMatches[i].Value, JsonNode.Parse(value.ToString(), documentOptions: JsonPathDocumentOptions)), $"The syntax tree and JsonNode give different values at {matches[i].Path} for {query}");
        }

        bool nodeStrictThrows;
        try
        {
            _ = path.Evaluate(document.Node, JsonPathEvaluationMode.Strict);
            nodeStrictThrows = false;
        }
        catch (JsonPathEvaluationException)
        {
            nodeStrictThrows = true;
        }

        FuzzAssert.Equal(nodeStrictThrows, strictMatches is null, $"Strict mode over the syntax tree and over JsonNode disagree about reporting an error for {query}");
    }

    private static void CheckSameMatches(JsonPathResult<JsonSyntaxNode> expected, JsonPathResult<JsonSyntaxNode> actual, string message)
    {
        FuzzAssert.Equal(expected.Count, actual.Count, message + ": the number of matches");
        for (var i = 0; i < expected.Count; i++)
        {
            FuzzAssert.Equal(expected[i].Path, actual[i].Path, $"{message}: the path of the match {i}");
            FuzzAssert.True(ReferenceEquals(expected[i].Value, actual[i].Value), $"{message}: the node of the match {i} at {expected[i].Path}");
        }
    }

    /// <summary>The normalized path of every match selects exactly that node (RFC 9535 section 2.7).</summary>
    private static void CheckNormalizedPaths(JsonSyntaxTree tree, JsonPathResult<JsonSyntaxNode> matches, string query)
    {
        var count = Math.Min(matches.Count, MaxCheckedNormalizedPaths);
        for (var i = 0; i < count; i++)
        {
            var match = matches[i];
            FuzzAssert.True(JsonPathQuery.TryParse(match.Path, out var normalizedPath), $"The normalized path {FuzzAssert.Format(match.Path)} of {query} is not a valid query");

            var result = normalizedPath.Evaluate(tree);
            FuzzAssert.Equal(1, result.Count, $"The normalized path {FuzzAssert.Format(match.Path)} of {query} does not select exactly one node");
            FuzzAssert.Equal(match.Path, result[0].Path, $"The normalized path of the normalized path {FuzzAssert.Format(match.Path)} differs");
            FuzzAssert.True(ReferenceEquals(match.Value, result[0].Value), $"The normalized path {FuzzAssert.Format(match.Path)} of {query} selects another node");
        }
    }

    /// <summary>Parses a document as System.Text.Json does, or returns <see langword="null"/> for one it cannot represent.</summary>
    private static ParsedJsonNode? ParseJsonNode(string text)
    {
        // System.Text.Json reads UTF-8, which cannot hold an unpaired surrogate, and does not skip a byte order mark
        if (HasUnpairedSurrogate(text))
            return null;

        var json = text.StartsWith('\uFEFF', StringComparison.Ordinal) ? text[1..] : text;
        try
        {
            using (var document = System.Text.Json.JsonDocument.Parse(json, JsonPathDocumentOptions))
            {
                // An escaped unpaired surrogate ("\uD800") is read, and throws once the string is
                if (!HasValidStrings(document.RootElement))
                    return null;
            }

            return new ParsedJsonNode(JsonNode.Parse(json, documentOptions: JsonPathDocumentOptions));
        }
        catch (System.Text.Json.JsonException)
        {
            // Deeper than System.Text.Json reads, or a comment holding U+2028 or U+2029
            return null;
        }
        catch (InvalidOperationException)
        {
            // The duplicate name check reads the names, and an escaped unpaired surrogate in one throws
            return null;
        }

        static bool HasValidStrings(System.Text.Json.JsonElement element)
        {
            try
            {
                return element.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => element.GetString() is not null,
                    System.Text.Json.JsonValueKind.Array => element.EnumerateArray().All(HasValidStrings),
                    System.Text.Json.JsonValueKind.Object => element.EnumerateObject().All(property => property.Name is not null && HasValidStrings(property.Value)),
                    _ => true,
                };
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetJsonPathSeeds()
    {
        using var document = FuzzResources.ReadJson(typeof(JsonFuzzTargets).Assembly, "jsonpath.cts.json");
        foreach (var test in document.RootElement.GetProperty("tests").EnumerateArray())
        {
            // The invalid selectors have no document: they stop at the parse
            var json = test.TryGetProperty("document", out var value) ? value.GetRawText() : Documents[1];
            yield return JsonPathSeed("cts-" + test.GetProperty("name").GetString(), test.GetProperty("selector").GetString()!, json);
        }

        for (var i = 0; i < Documents.Length; i++)
        {
            yield return JsonPathSeed(FormattableString.Invariant($"document-{i}"), JsonPathSelectors[i % JsonPathSelectors.Length], Documents[i]);
        }

        // Numbers out of the range of a double compare as infinities over both trees
        yield return JsonPathSeed("numbers", "$[?@ > 1]", "[1e400, -1e400, 1.0, 1, 0.1e1, 123456789012345678901234567890, 1e-400, -0, 1E+2]");
        yield return JsonPathSeed("duplicates", "$..*", "{\"a\": 1, \"a\": [2, {\"a\": 3}], \"b\": {\"a\": 4}}");
        yield return JsonPathSeed("strict", "$[?@.a == 1]", "[{\"a\": 1}, 2, \"x\", null]");

        static FuzzSeed JsonPathSeed(string name, string selector, string json)
            => new(name, (byte[])[.. FuzzText.Encode(selector), JsonPathSeparator, .. FuzzText.Encode(json)]);
    }

    private static IEnumerable<FuzzSeed> GetDocumentSeeds()
    {
        for (var i = 0; i < Documents.Length; i++)
        {
            yield return FuzzSeed.FromText(FormattableString.Invariant($"document-{i}"), Documents[i]);
        }
    }

    private static IEnumerable<FuzzSeed> GetIncrementalSeeds()
    {
        string[] insertions = ["", "1", ", \"x\": 2", "]", "{", "\"", "/*", "\n"];
        for (var i = 0; i < Documents.Length; i++)
        {
            var document = Documents[i];
            var start = (document.Length * (i % 4)) / 4;
            var removed = i % 3;
            var inserted = insertions[i % insertions.Length];
            yield return new FuzzSeed(FormattableString.Invariant($"edit-{i}"), (byte[])[(byte)start, (byte)(start >> 8), (byte)removed, .. FuzzText.Encode(document), InsertedTextSeparator, .. FuzzText.Encode(inserted)]);
        }
    }

    /// <summary>A document System.Text.Json read, whose root is <see langword="null"/> for the JSON <c>null</c>.</summary>
    private sealed record ParsedJsonNode(JsonNode? Node);

    private sealed class CountingWalker() : JsonSyntaxWalker(SyntaxWalkerDepth.Trivia)
    {
        public int TokenCount { get; private set; }

        public int TriviaCount { get; private set; }

        public override void VisitToken(SyntaxToken token)
        {
            TokenCount++;
            base.VisitToken(token);
        }

        public override void VisitTrivia(SyntaxTrivia trivia) => TriviaCount++;
    }
}
