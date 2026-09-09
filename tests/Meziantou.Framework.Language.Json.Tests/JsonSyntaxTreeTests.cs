using Meziantou.Framework.Json;

namespace Meziantou.Framework.Language.Json.Tests;

public sealed class JsonSyntaxTreeTests
{
    public static TheoryData<string> RoundTripSamples => new()
    {
        """{"name":"value","enabled":true,"items":[1,false,null]}""",
        """
{
  // leading comment
  "name": "value",
  "items": [
    1,
    true,
    null,
  ],
  /* block comment */
  "nested": {
    "number": -12.5e+2,
  },
}
""",
        """
// document comment
[
  "escaped\nvalue",
  "\u0041",
]
""",
        """
{
  "invalid": @@@,
  "missingComma": [1 2,],
  /* unterminated
""",
    };

    [Theory]
    [MemberData(nameof(RoundTripSamples))]
    public void Parse_Save_RoundTripsSamples(string text)
    {
        var tree = JsonSyntaxTree.ParseText(text);

        Assert.Equal(text, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void ParseText_BuildsObjectTree()
    {
        const string Text = """{ "name": "value", "enabled": true, "items": [1, null] }""";
        var tree = JsonSyntaxTree.ParseText(Text);

        Assert.Empty(tree.GetDiagnostics());
        var obj = Assert.IsType<JsonObjectSyntax>(tree.GetRoot().Value);
        Assert.Equal(3, obj.Members.Count);
        Assert.Equal("name", obj.Members[0].Name);
        Assert.Equal("value", Assert.IsType<JsonStringSyntax>(obj.Members[0].Value).Value);
        Assert.Equal(SyntaxKind.JsonTrueLiteral, obj.Members[1].Value.Kind());

        var array = Assert.IsType<JsonArraySyntax>(obj.Members[2].Value);
        Assert.Equal(2, array.Elements.Count);
        Assert.Equal("1", Assert.IsType<JsonNumberSyntax>(array.Elements[0]).Text);
        Assert.Equal(SyntaxKind.JsonNullLiteral, array.Elements[1].Kind());
    }

    [Fact]
    public void ParseText_TrailingCommasAndComments_DoNotCreateDiagnostics()
    {
        const string Text = """
{
  "a": 1,
  // ok
  "b": [true, false,],
}
""";

        var tree = JsonSyntaxTree.ParseText(Text);

        Assert.Empty(tree.GetDiagnostics());
        Assert.Equal(Text, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void ParseText_InvalidJson_DoesNotThrowAndKeepsSkippedText()
    {
        const string Text = """{ "a": @@@, "b": [1 2,], }""";

        var exception = Record.Exception(() => JsonSyntaxTree.ParseText(Text));
        var tree = JsonSyntaxTree.ParseText(Text);

        Assert.Null(exception);
        Assert.NotEmpty(tree.GetDiagnostics());
        Assert.True(tree.GetRoot().ContainsSkippedText);
        Assert.Equal(Text, tree.GetRoot().ToFullString());
        Assert.All(tree.GetDiagnostics(), diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
    }

    public static TheoryData<string> UnexpectedTokenInContainerSamples => new()
    {
        "[:]",
        "[:",
        "[}]",
        "[1, :, 2]",
        "[[:]]",
        """{"a":]}""",
        """{"a":]""",
        "{]}",
        "{:}",
        """{"a":}, "b":1}""",
        """{"a":{"b":]}}""",
    };

    [Theory]
    [MemberData(nameof(UnexpectedTokenInContainerSamples))]
    public void ParseText_UnexpectedTokenInContainer_TerminatesAndKeepsSkippedText(string text)
    {
        var tree = ParseWithTimeout(text);

        Assert.NotEmpty(tree.GetDiagnostics());
        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.All(tree.GetDiagnostics(), diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
        Assert.True(tree.GetDiagnostics().Count <= text.Length * 2, $"Expected a bounded number of diagnostics, got {tree.GetDiagnostics().Count}.");
    }

    [Fact]
    public void ParseText_UnexpectedTokenAfterValue_StillRecoversFollowingMembers()
    {
        const string Text = """{"a":], "b":2}""";

        var tree = ParseWithTimeout(Text);
        var obj = Assert.IsType<JsonObjectSyntax>(tree.GetRoot().Value);

        var member = obj.GetMember("b");

        Assert.Equal(Text, tree.GetRoot().ToFullString());
        Assert.NotNull(member);
        Assert.Equal("2", Assert.IsType<JsonNumberSyntax>(member.Value).Text);
    }

    [Fact]
    public void ParseText_UnexpectedTokenInArray_StillRecoversFollowingElements()
    {
        const string Text = "[1, :, 2]";

        var tree = ParseWithTimeout(Text);
        var array = Assert.IsType<JsonArraySyntax>(tree.GetRoot().Value);

        Assert.Equal(Text, tree.GetRoot().ToFullString());
        Assert.Contains(array.Elements, element => element is JsonNumberSyntax { Text: "1" });
        Assert.Contains(array.Elements, element => element is JsonNumberSyntax { Text: "2" });
    }

    /// <summary>Parses <paramref name="text"/> on a dedicated thread so a parser that fails to make progress fails the test instead of hanging the test run.</summary>
    private static JsonSyntaxTree ParseWithTimeout(string text)
    {
        JsonSyntaxTree? tree = null;
        var thread = new Thread(() => tree = JsonSyntaxTree.ParseText(text)) { IsBackground = true };
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), $"Parsing '{text}' did not complete; the parser is likely stuck on a token it never consumes.");

        return tree!;
    }

    [Fact]
    public void ParseText_CommentsAreTriviaWithSourceLocations()
    {
        const string Text = """
{
  // comment
  "a": 1
}
""";

        var tree = JsonSyntaxTree.ParseText(Text);
        var comment = Assert.Single(tree.GetRoot().DescendantTrivia(), trivia => trivia.Kind() == SyntaxKind.SingleLineCommentTrivia);
        var property = Assert.IsType<JsonObjectSyntax>(tree.GetRoot().Value).Members[0];

        Assert.Equal("// comment", comment.ToString());
        Assert.Equal(Text.IndexOf("//", StringComparison.Ordinal), comment.Span.Start);
        Assert.Equal(Text.IndexOf("\"a\"", StringComparison.Ordinal), property.Span.Start);
    }

    [Fact]
    public void ReplaceNode_ReplacesExactInstance_WhenNodeTextIsDuplicated()
    {
        var tree = JsonSyntaxTree.ParseText("[1, 1]");
        var numbers = tree.GetRoot().DescendantNodes().OfType<JsonNumberSyntax>().ToArray();
        JsonDocumentSyntax updated = tree.GetRoot().ReplaceNode(numbers[1], SyntaxFactory.JsonNumber("2"));

        Assert.Equal("[1, 2]", updated.ToFullString());
    }

    [Fact]
    public void ReplaceTrivia_ReplacesComment()
    {
        const string Text = """
{
  // old
  "a": 1
}
""";

        var tree = JsonSyntaxTree.ParseText(Text);
        var comment = Assert.Single(tree.GetRoot().DescendantTrivia(), trivia => trivia.Kind() == SyntaxKind.SingleLineCommentTrivia);

        var updated = tree.GetRoot().ReplaceTrivia(comment, SyntaxFactory.Trivia(SyntaxKind.MultiLineCommentTrivia, "/* new */"));

        Assert.Equal(
            """
{
  /* new */
  "a": 1
}
""",
            updated.ToFullString());
    }

    [Fact]
    public void SyntaxFactory_CreatesNodes()
    {
        var obj = SyntaxFactory.JsonObject(
            SyntaxFactory.JsonMember("name", SyntaxFactory.JsonString("value")),
            SyntaxFactory.JsonMember("count", SyntaxFactory.JsonNumber("42")));

        Assert.Equal("""{"name":"value","count":42}""", obj.ToFullString());
    }

    [Fact]
    public void WithChanges_ReparsesUpdatedText()
    {
        var tree = JsonSyntaxTree.ParseText("""{"a":1}""");

        var updated = tree.WithChanges(new TextChange(new TextSpan(5, 1), "2"));

        Assert.Equal("""{"a":2}""", updated.GetRoot().ToFullString());
    }

    [Fact]
    public void Visitor_VisitsAllValues()
    {
        var tree = JsonSyntaxTree.ParseText("""{"a":[1,"b",null]}""");
        var visitor = new CountingVisitor();

        visitor.Visit(tree.GetRoot());

        Assert.Equal(5, visitor.ValueCount);
    }

    [Fact]
    public void Rewriter_CanUpdateMemberValue()
    {
        var tree = JsonSyntaxTree.ParseText("""{"a":1,"b":2}""");
        var rewriter = new ReplaceMemberValueRewriter("b", SyntaxFactory.JsonNumber("9"));

        var rewritten = rewriter.Visit(tree.GetRoot());
        var updated = Assert.IsType<JsonDocumentSyntax>(rewritten);

        Assert.Equal("""{"a":1,"b":9}""", updated.ToFullString());
    }

    [Fact]
    public void EvaluateJsonPath_JsonSyntaxTree_ReturnsSyntaxNodes()
    {
        var tree = CreateJsonPathSyntaxTree();
        var path = JsonPath.Parse("$.items[1].name");

        var result = path.Evaluate(tree);

        Assert.Single(result);
        Assert.Equal("$['items'][1]['name']", result[0].Path);

        var name = Assert.IsType<JsonStringSyntax>(result[0].Value);
        Assert.Equal("bar", name.Value);
        Assert.Equal("\"bar\"", name.StringToken.Text);
        Assert.Equal(tree.GetText().Text.IndexOf("\"bar\"", StringComparison.Ordinal), name.Span.Start);
    }

    [Fact]
    public void EvaluateJsonPath_JsonSyntaxTree_CanEvaluateFromRoot()
    {
        var tree = CreateJsonPathSyntaxTree();
        var path = JsonPath.Parse("$.items[1].name");

        var treeResult = tree.Evaluate(path);
        var nodeResult = tree.GetRoot().Evaluate(path);
        var treeValue = tree.EvaluateValue(path);
        var nodeValue = tree.GetRoot().EvaluateValue(path);

        Assert.Single(treeResult);
        Assert.Single(nodeResult);
        Assert.Equal("$['items'][1]['name']", treeResult[0].Path);
        Assert.Equal("$['items'][1]['name']", nodeResult[0].Path);
        Assert.Equal("bar", Assert.IsType<JsonStringSyntax>(treeResult[0].Value).Value);
        Assert.Equal("bar", Assert.IsType<JsonStringSyntax>(nodeResult[0].Value).Value);
        Assert.Equal("bar", Assert.IsType<JsonStringSyntax>(treeValue).Value);
        Assert.Equal("bar", Assert.IsType<JsonStringSyntax>(nodeValue).Value);
        Assert.Throws<JsonPathEvaluationException>(() => tree.Evaluate(JsonPath.Parse("$.missing"), JsonPathEvaluationMode.Strict));
    }

    [Fact]
    public void EvaluateJsonPath_JsonSyntaxNode_NormalizesDocumentRoot()
    {
        var tree = CreateJsonPathSyntaxTree();
        var path = JsonPath.Parse("$");

        var treeResult = path.Evaluate(tree);
        var nodeResult = path.Evaluate(tree.GetRoot());

        Assert.Single(treeResult);
        Assert.Single(nodeResult);
        Assert.IsType<JsonObjectSyntax>(treeResult[0].Value);
        Assert.IsType<JsonObjectSyntax>(nodeResult[0].Value);
        Assert.Same(tree.GetRoot().Value, treeResult[0].Value);
        Assert.Same(tree.GetRoot().Value, nodeResult[0].Value);
    }

    [Fact]
    public void EvaluateJsonPath_JsonSyntaxTree_SelectorsFiltersAndFunctions()
    {
        var tree = CreateJsonPathSyntaxTree();

        AssertJsonSyntaxNames(JsonPath.Parse("$.items[*]").Evaluate(tree), "foo", "bar", "foobar");
        AssertJsonSyntaxNames(JsonPath.Parse("$.items[0:3:2]").Evaluate(tree), "foo", "foobar");
        AssertJsonSyntaxNames(JsonPath.Parse("$.items[?@.price < $.limit]").Evaluate(tree), "foo", "foobar");
        AssertJsonSyntaxNames(JsonPath.Parse("$.items[?@.available == true]").Evaluate(tree), "foo", "foobar");
        AssertJsonSyntaxNames(JsonPath.Parse("$.items[?length(@.tags) > 1]").Evaluate(tree), "foo");
        AssertJsonSyntaxNames(JsonPath.Parse("$.items[?count(@.*) == 6]").Evaluate(tree), "bar");
        AssertJsonSyntaxNames(JsonPath.Parse("$.items[?value(@.id) == 2]").Evaluate(tree), "bar");
        AssertJsonSyntaxNames(JsonPath.Parse("""$.items[?match(@.name, "foo")]""").Evaluate(tree), "foo");
        AssertJsonSyntaxNames(JsonPath.Parse("""$.items[?search(@.name, "foo")]""").Evaluate(tree), "foo", "foobar");

        var descendantResult = JsonPath.Parse("$..title").Evaluate(tree);

        Assert.Single(descendantResult);
        Assert.Equal("$['metadata']['title']", descendantResult[0].Path);
        Assert.Equal("Catalog", Assert.IsType<JsonStringSyntax>(descendantResult[0].Value).Value);
    }

    [Fact]
    public void EvaluateJsonPathValue_JsonSyntaxTree_ReturnsSyntaxNode()
    {
        var tree = CreateJsonPathSyntaxTree();
        var path = JsonPath.Parse("$.items[-1].name");

        var value = path.EvaluateValue(tree);

        var name = Assert.IsType<JsonStringSyntax>(value);
        Assert.Equal("foobar", name.Value);
        Assert.True(name.Span.Start > 0);
    }

    [Fact]
    public void EvaluateJsonPath_JsonSyntaxTree_NullValue_ReturnsNullLiteralNode()
    {
        var tree = CreateJsonPathSyntaxTree();

        var result = JsonPath.Parse("$.none").Evaluate(tree);

        Assert.Single(result);
        var value = Assert.IsType<JsonLiteralSyntax>(result[0].Value);
        Assert.Equal(SyntaxKind.JsonNullLiteral, value.Kind());
        Assert.Equal("null", value.LiteralToken.Text);
    }

    [Fact]
    public void EvaluateJsonPath_JsonSyntaxTree_MissingMember_StrictMode()
    {
        var tree = CreateJsonPathSyntaxTree();
        var path = JsonPath.Parse("$.missing");

        Assert.Throws<JsonPathEvaluationException>(() => path.Evaluate(tree, JsonPathEvaluationMode.Strict));
    }

    [Fact]
    public void EvaluateJsonPath_JsonSyntaxTree_InvalidSyntax_DoesNotThrow()
    {
        const string Text = """[{"name":"valid"},{"name": @@@,},{"name":"after"}]""";
        var tree = JsonSyntaxTree.ParseText(Text);

        var result = JsonPath.Parse("$[?@.name == null]").Evaluate(tree);
        var afterInvalid = JsonPath.Parse("$[2].name").EvaluateValue(tree);

        Assert.NotEmpty(tree.GetDiagnostics());
        Assert.Equal(Text, tree.GetRoot().ToFullString());
        Assert.Single(result);
        Assert.Equal("$[1]", result[0].Path);
        Assert.Equal("after", Assert.IsType<JsonStringSyntax>(afterInvalid).Value);
    }

    [Theory]
    [MemberData(nameof(RoundTripSamples))]
    [MemberData(nameof(UnexpectedTokenInContainerSamples))]
    public void EveryPrefixAndEveryDeletion_StillRoundTrips(string text)
    {
        // Cutting a document short, or dropping one character out of it, is how most broken JSON is actually reached.
        for (var length = 0; length <= text.Length; length++)
        {
            AssertRoundTrips(text[..length]);
        }

        for (var index = 0; index < text.Length; index++)
        {
            AssertRoundTrips(text.Remove(index, 1));
        }

        static void AssertRoundTrips(string candidate)
        {
            var root = JsonSyntaxTree.ParseText(candidate).GetRoot();

            Assert.Equal(candidate, root.ToFullString());

            // Concatenating the tokens has to give the text back too: a round-trip alone would not notice a token
            // that was dropped from the tree while its text stayed in a node above it.
            Assert.Equal(candidate, string.Concat(root.DescendantTokens().Select(token => token.ToFullString())));
        }
    }

    [Fact]
    public void TriviaAtTheEndOfALine_BelongsToTheTokenThatEndsIt()
    {
        const string Text = """
{
  // comment
  "a": 1
}
""";

        var tree = JsonSyntaxTree.ParseText(Text);
        var openBrace = Assert.IsType<JsonObjectSyntax>(tree.GetRoot().Value).OpenBraceToken;
        var name = Assert.IsType<JsonObjectSyntax>(tree.GetRoot().Value).Members[0].NameToken;

        Assert.Equal("\n", openBrace.TrailingTrivia.ToFullString());
        Assert.Equal("  // comment\n  ", name.LeadingTrivia.ToFullString());
    }

    [Fact]
    public void TriviaOnTheSameLineAsATokenStaysWithIt()
    {
        var tree = JsonSyntaxTree.ParseText("[1 , 2]");
        var array = Assert.IsType<JsonArraySyntax>(tree.GetRoot().Value);

        Assert.Equal(" ", Assert.IsType<JsonNumberSyntax>(array.Elements[0]).NumberToken.TrailingTrivia.ToFullString());
        Assert.Equal("", Assert.IsType<JsonNumberSyntax>(array.Elements[1]).NumberToken.TrailingTrivia.ToFullString());
    }

    [Theory]
    [InlineData("""[1,2]""", 2, 1, false)]
    [InlineData("""[1,2,]""", 2, 2, true)]
    [InlineData("""[1]""", 1, 0, false)]
    [InlineData("""[]""", 0, 0, false)]
    public void SeparatedList_CountsElementsAndCommasSeparately(string text, int expectedCount, int expectedSeparators, bool expectedTrailing)
    {
        var elements = Assert.IsType<JsonArraySyntax>(JsonSyntaxTree.ParseText(text).GetRoot().Value).Elements;

        Assert.Equal(expectedCount, elements.Count);
        Assert.Equal(expectedSeparators, elements.SeparatorCount);
        Assert.Equal(expectedTrailing, elements.HasTrailingSeparator);
    }

    [Fact]
    public void AMissingCommaIsRecordedAsAMissingSeparator()
    {
        var tree = JsonSyntaxTree.ParseText("[1 2]");
        var elements = Assert.IsType<JsonArraySyntax>(tree.GetRoot().Value).Elements;

        Assert.Equal(2, elements.Count);
        Assert.Equal(1, elements.SeparatorCount);
        Assert.True(elements.GetSeparator(0).IsMissing);
        Assert.Equal("[1 2]", tree.GetRoot().ToFullString());
        Assert.Contains(tree.GetDiagnostics(), diagnostic => diagnostic.Id == "JSON0009");
    }

    [Fact]
    public void ReplaceNode_ReturnsADocumentAndKeepsTheRestOfTheTreeAsItWas()
    {
        var tree = JsonSyntaxTree.ParseText("""{"a":{"deep":1},"b":2}""");
        var root = tree.GetRoot();
        var members = Assert.IsType<JsonObjectSyntax>(root.Value).Members;
        var untouched = members[0];

        // No cast: replacing inside a document gives back a document.
        JsonDocumentSyntax updated = root.ReplaceNode(members[1].Value, SyntaxFactory.JsonNumber("9"));
        var updatedMembers = Assert.IsType<JsonObjectSyntax>(updated.Value).Members;

        Assert.Equal("""{"a":{"deep":1},"b":9}""", updated.ToFullString());
        Assert.True(untouched.IsIncrementallyIdenticalTo(updatedMembers[0]), "The member that was not edited should be the very same node.");
        Assert.Equal("""{"a":{"deep":1},"b":2}""", root.ToFullString());
    }

    [Fact]
    public void ReplaceNode_MovesEverythingAfterTheEditedNode()
    {
        var tree = JsonSyntaxTree.ParseText("""{"a":1,"b":2}""");
        var members = Assert.IsType<JsonObjectSyntax>(tree.GetRoot().Value).Members;

        var updated = tree.GetRoot().ReplaceNode(members[0].Value, SyntaxFactory.JsonNumber("1000"));
        var text = updated.ToFullString();

        Assert.Equal("""{"a":1000,"b":2}""", text);
        foreach (var token in updated.DescendantTokens())
        {
            Assert.Equal(token.Text, text.Substring(token.Span.Start, token.Span.Length));
        }
    }

    [Fact]
    public void AnAnnotationSurvivesAnEditSomewhereElse()
    {
        var annotation = new SyntaxAnnotation("marker");
        var tree = JsonSyntaxTree.ParseText("""{"a":1,"b":2}""");
        var members = Assert.IsType<JsonObjectSyntax>(tree.GetRoot().Value).Members;

        var marked = tree.GetRoot().ReplaceNode(members[0], members[0].WithAdditionalAnnotations(annotation));
        var markedMembers = Assert.IsType<JsonObjectSyntax>(marked.Value).Members;
        var edited = marked.ReplaceNode(markedMembers[1].Value, SyntaxFactory.JsonNumber("9"));

        Assert.Equal("""{"a":1,"b":9}""", edited.ToFullString());
        Assert.Equal("\"a\":1", edited.GetAnnotatedNodes(annotation).Single().ToString());
    }

    [Fact]
    public void AnAnnotationSurvivesAWithMethod()
    {
        var annotation = new SyntaxAnnotation();
        var member = SyntaxFactory.JsonMember("a", SyntaxFactory.JsonNumber("1")).WithAdditionalAnnotations(annotation);

        var updated = member.WithValue(SyntaxFactory.JsonNumber("2"));

        Assert.True(updated.HasAnnotation(annotation));
        Assert.Equal("\"a\":2", updated.ToFullString());
    }

    [Fact]
    public void RemoveNode_TakesTheMemberAndItsComma()
    {
        var tree = JsonSyntaxTree.ParseText("""{"a":1,"b":2,"c":3}""");
        var members = Assert.IsType<JsonObjectSyntax>(tree.GetRoot().Value).Members;

        var updated = tree.GetRoot().RemoveNode(members[1], SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal("""{"a":1,"c":3}""", updated.ToFullString());
        Assert.Equal(2, Assert.IsType<JsonObjectSyntax>(updated.Value).Members.Count);
    }

    [Fact]
    public void RemoveNode_OfTheLastMember_TakesTheCommaBeforeIt()
    {
        var tree = JsonSyntaxTree.ParseText("""{"a":1,"b":2}""");
        var members = Assert.IsType<JsonObjectSyntax>(tree.GetRoot().Value).Members;

        var updated = tree.GetRoot().RemoveNode(members[1], SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal("""{"a":1}""", updated.ToFullString());
    }

    [Fact]
    public void RemoveNode_KeepingLeadingTrivia_KeepsTheCommentThatDescribedIt()
    {
        const string Text = """
{
  // this one matters
  "a": 1,
  "b": 2
}
""";
        var tree = JsonSyntaxTree.ParseText(Text);
        var members = Assert.IsType<JsonObjectSyntax>(tree.GetRoot().Value).Members;

        var kept = tree.GetRoot().RemoveNode(members[0], SyntaxRemoveOptions.KeepLeadingTrivia);
        var dropped = tree.GetRoot().RemoveNode(members[0], SyntaxRemoveOptions.KeepNoTrivia);

        // Keeping the trivia moves the comment onto what follows; the indentation is not tidied up, only preserved.
        Assert.Contains("// this one matters", kept.ToFullString());
        Assert.DoesNotContain("// this one matters", dropped.ToFullString());
        Assert.Equal("{\n  \"b\": 2\n}", dropped.ToFullString());
    }

    [Fact]
    public void RemoveNodes_TakesSeveralMembersAtOnce()
    {
        var tree = JsonSyntaxTree.ParseText("""{"a":1,"b":2,"c":3,"d":4}""");
        var members = Assert.IsType<JsonObjectSyntax>(tree.GetRoot().Value).Members;

        var updated = tree.GetRoot().RemoveNodes([members[0], members[2]], SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal("""{"b":2,"d":4}""", updated.ToFullString());
    }

    /// <summary>
    /// Trivia kept from a removed member belongs where that member was, not where the first removal happened.
    /// </summary>
    [Fact]
    public void RemoveNodes_KeepsEachRemovedMembersTriviaWhereItWas()
    {
        const string Text = """
            {
              // about a
              "a": 1,
              "b": 2,
              // about c
              "c": 3,
              "d": 4
            }
            """;
        var tree = JsonSyntaxTree.ParseText(Text);
        var members = Assert.IsType<JsonObjectSyntax>(tree.GetRoot().Value).Members;

        var updated = tree.GetRoot().RemoveNodes([members[0], members[2]], SyntaxRemoveOptions.KeepLeadingTrivia);
        var result = updated.ToFullString();

        // Each comment moves onto the member that now stands where the one it described was.
        Assert.True(
            result.IndexOf("// about a", StringComparison.Ordinal) < result.IndexOf("\"b\"", StringComparison.Ordinal),
            result);
        Assert.True(
            result.IndexOf("\"b\"", StringComparison.Ordinal) < result.IndexOf("// about c", StringComparison.Ordinal),
            result);
        Assert.True(
            result.IndexOf("// about c", StringComparison.Ordinal) < result.IndexOf("\"d\"", StringComparison.Ordinal),
            result);
    }

    [Fact]
    public void RemoveNode_LeavesTheDocumentReadableAndItsSpansConsistent()
    {
        var tree = JsonSyntaxTree.ParseText("""{"a":[1,2,3],"b":{"c":4},"d":5}""");
        var members = Assert.IsType<JsonObjectSyntax>(tree.GetRoot().Value).Members;

        var updated = tree.GetRoot().RemoveNode(members[1], SyntaxRemoveOptions.KeepNoTrivia);
        var text = updated.ToFullString();

        Assert.Equal("""{"a":[1,2,3],"d":5}""", text);
        Assert.Empty(JsonSyntaxTree.ParseText(text).GetDiagnostics());
        foreach (var token in updated.DescendantTokens())
        {
            Assert.Equal(token.Text, text.Substring(token.Span.Start, token.Span.Length));
        }
    }

    [Fact]
    public void FindTokenAndFindNode_LandOnWhatIsAtThePosition()
    {
        const string Text = """{ "name": [1, 2] }""";
        var tree = JsonSyntaxTree.ParseText(Text);
        var root = tree.GetRoot();
        var namePosition = Text.IndexOf("\"name\"", StringComparison.Ordinal);

        Assert.Equal(SyntaxKind.StringToken, root.FindToken(namePosition).Kind());
        Assert.Equal("\"name\"", root.FindToken(namePosition).Text);
        Assert.Equal(SyntaxKind.JsonArray, root.FindNode(new TextSpan(Text.IndexOf('[', StringComparison.Ordinal), 6)).Kind());
        Assert.Equal(SyntaxKind.EndOfFileToken, root.FindToken(Text.Length).Kind());
    }

    [Fact]
    public void FactoryBuiltNodesHaveConsistentSpansWithoutBeingParsed()
    {
        var obj = SyntaxFactory.JsonObject(
            SyntaxFactory.JsonMember("name", SyntaxFactory.JsonString("value")),
            SyntaxFactory.JsonMember("items", SyntaxFactory.JsonArray(SyntaxFactory.JsonNumber("1"), SyntaxFactory.JsonNumber("2"))));

        var text = obj.ToFullString();

        Assert.Equal("""{"name":"value","items":[1,2]}""", text);
        Assert.Equal(0, obj.FullSpan.Start);
        Assert.Equal(text.Length, obj.FullSpan.Length);
        foreach (var token in obj.DescendantTokens())
        {
            Assert.Equal(token.Text, text.Substring(token.Span.Start, token.Span.Length));
        }
    }

    [Fact]
    public void Walker_AtTriviaDepth_SeesTheCommentsToo()
    {
        var tree = JsonSyntaxTree.ParseText("""
{
  // one
  "a": 1 // two
}
""");
        var walker = new CommentCountingWalker();

        walker.Visit(tree.GetRoot());

        Assert.Equal(2, walker.CommentCount);
    }

    private sealed class CommentCountingWalker() : JsonSyntaxWalker(SyntaxWalkerDepth.Trivia)
    {
        public int CommentCount { get; private set; }

        public override void VisitTrivia(SyntaxTrivia trivia)
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                CommentCount++;
            }
        }
    }

    private static JsonSyntaxTree CreateJsonPathSyntaxTree()
    {
        const string Text = """
{
  // query limit
  "limit": 10,
  "items": [
    {
      "id": 1,
      "name": "foo",
      "price": 8,
      "available": true,
      "tags": ["a", "b"],
    },
    {
      "id": 2,
      "name": "bar",
      "price": 12,
      "available": false,
      "tags": ["c"],
      "isbn": "123",
    },
    {
      "id": 3,
      "name": "foobar",
      "price": 7,
      "available": true,
      "tags": ["d"],
    },
  ],
  /* metadata */
  "metadata": { "title": "Catalog", },
  "none": null,
}
""";

        var tree = JsonSyntaxTree.ParseText(Text);
        Assert.Empty(tree.GetDiagnostics());

        return tree;
    }

    private static void AssertJsonSyntaxNames(JsonPathResult<JsonSyntaxNode> result, params string[] expectedNames)
    {
        Assert.Equal(expectedNames.Length, result.Count);
        for (var i = 0; i < expectedNames.Length; i++)
        {
            var item = Assert.IsType<JsonObjectSyntax>(result[i].Value);
            var nameMember = item.GetMember("name");

            Assert.NotNull(nameMember);
            Assert.Equal(expectedNames[i], Assert.IsType<JsonStringSyntax>(nameMember.Value).Value);
        }
    }

    private sealed class CountingVisitor : JsonSyntaxWalker
    {
        public int ValueCount { get; private set; }

        public override void VisitJsonObject(JsonObjectSyntax node)
        {
            ValueCount++;
            base.VisitJsonObject(node);
        }

        public override void VisitJsonArray(JsonArraySyntax node)
        {
            ValueCount++;
            base.VisitJsonArray(node);
        }

        public override void VisitJsonNumber(JsonNumberSyntax node)
        {
            ValueCount++;
            base.VisitJsonNumber(node);
        }

        public override void VisitJsonString(JsonStringSyntax node)
        {
            ValueCount++;
            base.VisitJsonString(node);
        }

        public override void VisitJsonLiteral(JsonLiteralSyntax node)
        {
            ValueCount++;
            base.VisitJsonLiteral(node);
        }
    }

    [Fact]
    public void SourceText_ToStringReturnsTheText()
    {
        const string Text = "{\n  \"a\": 1\n}";

        Assert.Equal(Text, JsonSyntaxTree.ParseText(Text).GetText().ToString());
    }

    private sealed class ReplaceMemberValueRewriter : JsonSyntaxRewriter
    {
        private readonly string _name;
        private readonly JsonValueSyntax _value;

        public ReplaceMemberValueRewriter(string name, JsonValueSyntax value)
        {
            _name = name;
            _value = value;
        }

        public override SyntaxNode? VisitJsonMember(JsonMemberSyntax node)
        {
            if (string.Equals(node.Name, _name, StringComparison.Ordinal))
                return node.WithValue(_value);

            return base.VisitJsonMember(node);
        }
    }

    [Fact]
    public void Diagnostics_AreLocatedInTheTreesOwnSourceText()
    {
        var tree = JsonSyntaxTree.ParseText("{\n  \"a\": \n}");
        var diagnostic = tree.GetDiagnostics()[0];

        Assert.Same(tree.GetText(), diagnostic.Location.SourceText);
        Assert.Equal(1, diagnostic.Location.GetLineSpan().Start.Line);
    }

    /// <summary>
    /// A member always has a value, so taking it out would leave a member whose own type says the value is there and
    /// whose property returns nothing. The edit is refused instead.
    /// </summary>
    [Fact]
    public void RemoveNode_OfAChildThatFillsARequiredSlot_IsRejected()
    {
        var tree = JsonSyntaxTree.ParseText("""{"a":1}""");
        var member = tree.GetRoot().DescendantNodes().OfType<JsonMemberSyntax>().Single();

        Assert.Throws<InvalidOperationException>(() => tree.GetRoot().RemoveNode(member.Value, SyntaxRemoveOptions.KeepNoTrivia));
    }

    /// <summary>
    /// An array of one element holds no comma yet, so what kind of list it is cannot be read off its contents.
    /// </summary>
    [Fact]
    public void InsertNodesAfter_InAnArrayOfOneElement_StillAddsTheComma()
    {
        var tree = JsonSyntaxTree.ParseText("[1]");
        var array = tree.GetRoot().DescendantNodes().OfType<JsonArraySyntax>().Single();

        var updated = tree.GetRoot().InsertNodesAfter(array.Elements[0], [SyntaxFactory.JsonNumber("2")]);

        Assert.Equal("[1,2]", updated.ToFullString());
        Assert.Equal(2, updated.DescendantNodes().OfType<JsonArraySyntax>().Single().Elements.Count);
    }

    /// <summary>
    /// A member holds one value and not a list of them, which the tree cannot show: a list of one collapses to its
    /// only element, so the slot of a member looks exactly like the slot of an array with one element in it.
    /// </summary>
    [Fact]
    public void InsertNodesAfter_WhereOnlyOneNodeFits_IsRejected()
    {
        var tree = JsonSyntaxTree.ParseText("""{"a":1}""");
        var root = tree.GetRoot();
        var member = Assert.IsType<JsonObjectSyntax>(root.Value).Members[0];

        Assert.Throws<ArgumentException>(() => root.InsertNodesAfter(member.Value, [SyntaxFactory.JsonNumber("2")]));
        Assert.Throws<ArgumentException>(() => root.InsertNodesBefore(member.Value, [SyntaxFactory.JsonNumber("2")]));
        Assert.Throws<ArgumentException>(() => root.ReplaceNode(member.Value, [SyntaxFactory.JsonNumber("2")]));

        // The member still reads as a member: nothing was written into a slot that cannot hold it.
        Assert.Equal("""{"a":1}""", root.ToFullString());
        Assert.Equal("1", member.Value.ToFullString());
    }

    [Fact]
    public void ReplaceNode_WithSeveralNodes_InAnArrayOfOneElement_StillAddsTheComma()
    {
        var tree = JsonSyntaxTree.ParseText("[1]");
        var array = tree.GetRoot().DescendantNodes().OfType<JsonArraySyntax>().Single();

        var updated = tree.GetRoot().ReplaceNode(array.Elements[0], [SyntaxFactory.JsonNumber("2"), SyntaxFactory.JsonNumber("3")]);

        Assert.Equal("[2,3]", updated.ToFullString());
    }

    /// <summary>The document holds its values in a plain list, so removing one takes nothing else with it.</summary>
    [Fact]
    public void RemoveNode_FromTheDocumentsPlainList_TakesOnlyThatValue()
    {
        var tree = JsonSyntaxTree.ParseText("1 2 3");
        var values = tree.GetRoot().Values;

        var updated = tree.GetRoot().RemoveNode(values[1], SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal("1 3", updated.ToFullString());
        Assert.Equal(2, updated.Values.Count);
    }

    [Fact]
    public void ParseText_OfADocumentNestedTooDeeply_ReportsItRatherThanRunningOutOfStack()
    {
        var text = new string('[', 20_000) + new string(']', 20_000);

        var tree = JsonSyntaxTree.ParseText(text);

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Contains(tree.GetDiagnostics(), diagnostic => string.Equals(diagnostic.Id, "JSON0012", StringComparison.Ordinal));
    }

    /// <summary>A batch naming an element of another tree is rejected, the way a single foreign node is.</summary>
    [Fact]
    public void RemoveNodes_RejectsABatchHoldingANodeFromAnotherTree()
    {
        var first = Assert.IsType<JsonArraySyntax>(JsonSyntaxTree.ParseText("[1,2]").GetRoot().Value);
        var second = Assert.IsType<JsonArraySyntax>(JsonSyntaxTree.ParseText("[3,4]").GetRoot().Value);

        Assert.Throws<ArgumentException>(() => first.RemoveNodes([first.Elements[0], second.Elements[0]], SyntaxRemoveOptions.KeepNoTrivia));
    }

    /// <summary>Inserting nothing is not an error, and changes nothing.</summary>
    [Fact]
    public void InsertNodes_WithNothingToInsertReturnsTheSameTree()
    {
        var array = Assert.IsType<JsonArraySyntax>(JsonSyntaxTree.ParseText("[1,2]").GetRoot().Value);

        Assert.Equal("[1,2]", array.InsertNodesAfter(array.Elements[0], []).ToFullString());
        Assert.Equal("[1,2]", array.InsertNodesBefore(array.Elements[0], []).ToFullString());
    }

    /// <summary>
    /// A node covering exactly the span asked for is not necessarily the one wanted: by default the outermost of
    /// the nodes sharing that span is, which is what the option is for.
    /// </summary>
    [Fact]
    public void FindNode_ReturnsTheOutermostNodeSharingTheSpanUnlessAskedForTheInnermost()
    {
        var root = JsonSyntaxTree.ParseText("1").GetRoot();

        Assert.IsType<JsonDocumentSyntax>(root.FindNode(root.FullSpan));
        Assert.IsType<JsonNumberSyntax>(root.FindNode(root.FullSpan, getInnermostNodeForTie: true));
    }

    /// <summary>A rewriter that overrides VisitTrivia takes part in an ordinary rewrite.</summary>
    [Fact]
    public void Rewriter_PutsTheTriviaOfEveryTokenThroughVisitTrivia()
    {
        var tree = JsonSyntaxTree.ParseText("/*hello*/1");
        var rewriter = new CommentRemover();

        var rewritten = rewriter.Visit(tree.GetRoot());

        Assert.Equal("1", rewritten?.ToFullString());
        Assert.True(rewriter.Visited > 0);
    }

    private sealed class CommentRemover : JsonSyntaxRewriter
    {
        public int Visited { get; private set; }

        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
        {
            Visited++;

            return trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ? default : trivia;
        }
    }
}
