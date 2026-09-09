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
}
