namespace Meziantou.Framework.Language.Json.Tests;

/// <summary>
/// Reparsing after an edit has to produce exactly what parsing the new text from scratch would.
/// </summary>
/// <remarks>
/// A blender that gets this wrong does not throw; it hands back a tree that quietly disagrees with its own text. So
/// the tests compare against a full parse rather than against expectations, over enough edits that the awkward cases
/// come up on their own.
/// </remarks>
public sealed class JsonIncrementalParsingTests
{
    /// <summary>Documents to edit, including several that are already broken in the ways that matter.</summary>
    private static readonly string[] Documents =
    [
        "{}",
        "[]",
        "1",
        "\"a\"",
        "true",
        "null",
        "",
        "   ",
        "{\"a\":1,\"b\":2,\"c\":3}",
        "{\"a\":{\"b\":[1,2,{\"c\":null}]},\"d\":true}",
        "[1, 2, 3, 4, 5, 6, 7, 8, 9, 10]",
        "{\n  \"a\": 1,\n  // a comment\n  \"b\": 2\n}",
        "{\r\n  \"a\": 1,\r\n  \"b\": 2\r\n}",
        "{\"a\":1 /* inline */,\"b\":2}",
        "{\"a\":1,}",
        "[1,2,]",
        "{\"escaped\":\"a\\nb\\\"c\\u0041\"}",
        "{\"bad\":\"\\q\"}",
        "{\"a\": \"unterminated",
        "{} /* unterminated",
        "{,}",
        "{\"a\" 1}",
        "{\"a\": foo}",
        "{} xyz",
        "[1, :, 2]",
        "{a: 1, 'b': 'c'}",
        "{\"a\": [1, 2}",
        "{\"a\":1,\"a\":2}",
        "[0x1F, -Infinity, \"x\\u12\"]",
        "{\"a\": \"x\n, \"b\": 2}",
        "{\"a\":1}}",
    ];

    /// <summary>The fragments worth inserting: the ones that change how the text lexes.</summary>
    private static readonly string[] Fragments =
    [
        "", " ", "\t", "\n", "\r\n", "\"", "\\", "\\\\", "\\n", "\\u0041",
        "/", "//", "/*", "*/", "{", "}", "[", "]", ",", ":",
        "0", "9", "-", ".", "e", "e+", "+", "true", "fals", "nul", "\"a\"", "\"\"", "x",
        "'", "\f", "\u00A0", "\uFEFF", "\u0001", "a:", "\"a\":",
    ];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ARandomEdit_ReparsesToTheSameTreeAsAFullParse(int seed)
    {
        var random = new DeterministicRandom(seed);
        foreach (var document in Documents)
        {
            for (var iteration = 0; iteration < 200; iteration++)
            {
                AssertMatchesFullParse(document, RandomChange(random, document));
            }
        }
    }

    [Fact]
    public void InsertingEveryFragmentAtEveryPosition_ReparsesToTheSameTreeAsAFullParse()
    {
        // Exhaustive over the dimension that off-by-one margins live in.
        foreach (var document in Documents)
        {
            if (document.Length > 40)
                continue;

            for (var position = 0; position <= document.Length; position++)
            {
                foreach (var fragment in Fragments)
                {
                    AssertMatchesFullParse(document, new TextChange(new TextSpan(position, 0), fragment));
                }
            }
        }
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void EditsAppliedOneAfterAnother_KeepMatchingAFullParse(int seed)
    {
        // Feeding a reparsed tree back in is the only way to notice a blender that corrupts the tree it produces.
        var random = new DeterministicRandom(seed);
        foreach (var document in Documents)
        {
            for (var chain = 0; chain < 20; chain++)
            {
                var tree = JsonSyntaxTree.ParseText(document);
                for (var step = 0; step < 5; step++)
                {
                    var change = RandomChange(random, tree.GetText().Text);
                    var expectedText = tree.GetText().WithChanges([change]);

                    tree = tree.WithChanges(change);

                    AssertSameAsFullParse(tree, expectedText.Text, $"seed {seed}, document {Escape(document)}, step {step}");
                }
            }
        }
    }

    [Fact]
    public void EditingOneMember_KeepsTheOthers()
    {
        var document = "{" + string.Join(",", Enumerable.Range(0, 200).Select(index => $"\"key{index}\":{index}")) + "}";
        var tree = JsonSyntaxTree.ParseText(document);
        var target = Assert.IsType<JsonObjectSyntax>(tree.GetRoot().Value).GetMember("key100")!;

        var updated = tree.WithChanges(new TextChange(target.Value.Span, "999999"));

        Assert.Equal(document.Replace(":100,", ":999999,", StringComparison.Ordinal), updated.GetText().Text);
        Assert.True(updated.ReusedNodeCount >= 190, $"Expected most members to be reused, but only {updated.ReusedNodeCount} were.");
    }

    [Fact]
    public void AnUntouchedMemberIsTheVerySameNodeAfterAnEdit()
    {
        var tree = JsonSyntaxTree.ParseText("{\"a\":{\"deep\":1},\n\"b\":2}");
        var before = Assert.IsType<JsonObjectSyntax>(tree.GetRoot().Value).GetMember("a")!;

        var updated = tree.WithChanges(new TextChange(new TextSpan(tree.GetText().Text.IndexOf('2', StringComparison.Ordinal), 1), "3"));
        var after = Assert.IsType<JsonObjectSyntax>(updated.GetRoot().Value).GetMember("a")!;

        Assert.True(before.IsIncrementallyIdenticalTo(after), "The member that was not edited should not have been parsed again.");
    }

    [Fact]
    public void ReparsingTheSameText_ReturnsTheSameTree()
    {
        var tree = JsonSyntaxTree.ParseText("{\"a\":1}");

        Assert.Same(tree, tree.WithChangedText(tree.GetText()));
    }

    [Theory]
    [InlineData("{\"a\":1,\"b\":2}", 6, 0, "\"")]
    [InlineData("{\"a\":1,\"b\":2}", 6, 0, "//")]
    [InlineData("{\"a\":1,\"b\":2}", 6, 0, "/*")]
    [InlineData("{\"a\":1,\"b\":2}", 6, 1, "")]
    [InlineData("[1,2]", 2, 0, "0")]
    [InlineData("{\"a\":1} /* c */", 13, 2, "")]
    public void AnEditThatChangesHowTheRestLexes_IsNotReusedAcross(string document, int start, int length, string replacement)
    {
        AssertMatchesFullParse(document, new TextChange(new TextSpan(start, length), replacement));
    }

    /// <summary>Cases the fuzzer found, kept so they stay fixed.</summary>
    [Theory]
    [InlineData("{\"a\":1,\"b\":2}", 0, 0, "")]
    public void PinnedRegressions(string document, int start, int length, string replacement)
    {
        AssertMatchesFullParse(document, new TextChange(new TextSpan(start, length), replacement));
    }

    private static TextChange RandomChange(DeterministicRandom random, string text)
    {
        var start = random.Next(text.Length + 1);
        var kind = random.Next(10);

        if (kind < 4)
            return new TextChange(new TextSpan(start, 0), Fragments[random.Next(Fragments.Length)]);

        var length = Math.Min(random.Next(9), text.Length - start);
        if (kind < 7)
            return new TextChange(new TextSpan(start, length), "");

        return new TextChange(new TextSpan(start, length), Fragments[random.Next(Fragments.Length)]);
    }

    private static void AssertMatchesFullParse(string document, TextChange change)
    {
        var tree = JsonSyntaxTree.ParseText(document);
        var newText = tree.GetText().WithChanges([change]);
        var incremental = tree.WithChanges(change);

        AssertSameAsFullParse(incremental, newText.Text, $"document {Escape(document)}, change [{change.Span.Start}..{change.Span.End}) -> {Escape(change.NewText)}");
    }

    private static void AssertSameAsFullParse(JsonSyntaxTree incremental, string expectedText, string context)
    {
        var full = JsonSyntaxTree.ParseText(expectedText);

        Assert.Equal(expectedText, incremental.GetText().Text, message: context);
        Assert.Equal(expectedText, incremental.GetRoot().ToFullString(), message: context);

        var incrementalItems = incremental.GetRoot().DescendantNodesAndTokens().ToArray();
        var fullItems = full.GetRoot().DescendantNodesAndTokens().ToArray();

        Assert.HasCount(fullItems.Length, incrementalItems, message: $"{context}: the trees have different shapes.");
        for (var i = 0; i < fullItems.Length; i++)
        {
            var expected = fullItems[i];
            var actual = incrementalItems[i];

            Assert.Equal(expected.RawKind, actual.RawKind, message: $"{context}: item {i} has the wrong kind.");
            Assert.Equal(expected.FullSpan, actual.FullSpan, message: $"{context}: item {i} has the wrong full span.");
            Assert.Equal(expected.Span, actual.Span, message: $"{context}: item {i} has the wrong span.");
            Assert.Equal(expected.ToFullString(), actual.ToFullString(), message: $"{context}: item {i} has the wrong text.");
            Assert.Equal(expected.IsMissing, actual.IsMissing, message: $"{context}: item {i} disagrees about being missing.");
        }

        var expectedDiagnostics = Describe(full.GetDiagnostics());
        var actualDiagnostics = Describe(incremental.GetDiagnostics());

        Assert.Equal(expectedDiagnostics, actualDiagnostics, message: $"{context}: the diagnostics differ.");
    }

    private static string Describe(IReadOnlyList<Diagnostic> diagnostics)
        => string.Join("\n", diagnostics.Select(diagnostic => $"{diagnostic.Id} {diagnostic.Location.SourceSpan} {diagnostic.Message}"));

    private static string Escape(string text) => text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>
    /// A small generator whose sequence does not change between runtimes, so a failure can be reproduced from its seed.
    /// </summary>
    private sealed class DeterministicRandom(int seed)
    {
        private uint _state = (uint)seed | 1;

        public int Next(int exclusiveMax)
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;

            return exclusiveMax <= 0 ? 0 : (int)(_state % (uint)exclusiveMax);
        }
    }
}
