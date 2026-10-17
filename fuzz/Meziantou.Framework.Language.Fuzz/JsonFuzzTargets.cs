using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Language.Json;

namespace Meziantou.Framework.Language.Fuzz;

/// <summary>
/// JSON documents: the shared invariants, the values of the nodes, the agreement with System.Text.Json about which
/// documents are valid, and the incremental reparse, which reuses the nodes of the old tree and must give the tree a full
/// parse of the new text gives.
/// </summary>
internal static class JsonFuzzTargets
{
    /// <summary>Separates the document from the text the edit inserts (an invalid UTF-8 byte, so it never ends a character).</summary>
    private const byte InsertedTextSeparator = 0xFF;

    private const int GetMemberSampleCount = 64;

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
