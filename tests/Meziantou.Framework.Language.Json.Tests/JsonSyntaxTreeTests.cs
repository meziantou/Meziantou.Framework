using Meziantou.Framework.Json;
using Meziantou.Framework.Language.Json;

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

        Assert.Equal(text, tree.Root.ToFullString());
    }

    [Fact]
    public void ParseText_BuildsObjectTree()
    {
        const string Text = """{ "name": "value", "enabled": true, "items": [1, null] }""";
        var tree = JsonSyntaxTree.ParseText(Text);

        Assert.Empty(tree.Diagnostics);
        var obj = Assert.IsType<JsonObjectSyntax>(tree.Root.Value);
        Assert.Equal(3, obj.Members.Count);
        Assert.Equal("name", obj.Members[0].Name);
        Assert.Equal("value", Assert.IsType<JsonStringSyntax>(obj.Members[0].Value).Value);
        Assert.Equal(JsonSyntaxKind.JsonTrueLiteral, obj.Members[1].Value.Kind);

        var array = Assert.IsType<JsonArraySyntax>(obj.Members[2].Value);
        Assert.Equal(2, array.Elements.Count);
        Assert.Equal("1", Assert.IsType<JsonNumberSyntax>(array.Elements[0].Value).Text);
        Assert.Equal(JsonSyntaxKind.JsonNullLiteral, array.Elements[1].Value.Kind);
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

        Assert.Empty(tree.Diagnostics);
        Assert.Equal(Text, tree.Root.ToFullString());
    }

    [Fact]
    public void ParseText_InvalidJson_DoesNotThrowAndKeepsSkippedText()
    {
        const string Text = """{ "a": @@@, "b": [1 2,], }""";

        var exception = Record.Exception(() => JsonSyntaxTree.ParseText(Text));
        var tree = JsonSyntaxTree.ParseText(Text);

        Assert.Null(exception);
        Assert.NotEmpty(tree.Diagnostics);
        Assert.True(tree.Root.ContainsSkippedText);
        Assert.Equal(Text, tree.Root.ToFullString());
        Assert.All(tree.Diagnostics, diagnostic => Assert.Equal(JsonDiagnosticSeverity.Error, diagnostic.Severity));
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
        var comment = Assert.Single(tree.Root.DescendantTrivia().Where(trivia => trivia.Kind == JsonSyntaxKind.SingleLineCommentTrivia));
        var property = Assert.IsType<JsonObjectSyntax>(tree.Root.Value).Members[0];

        Assert.Equal("// comment", comment.Text);
        Assert.Equal(Text.IndexOf("//", StringComparison.Ordinal), comment.Span.Start);
        Assert.Equal(Text.IndexOf("\"a\"", StringComparison.Ordinal), property.Span.Start);
    }

    [Fact]
    public void ReplaceNode_ReplacesExactInstance_WhenNodeTextIsDuplicated()
    {
        var tree = JsonSyntaxTree.ParseText("[1, 1]");
        var numbers = tree.Root.DescendantNodes().OfType<JsonNumberSyntax>().ToArray();
        var replacement = new JsonNumberSyntax(numbers[1].NumberToken.WithText("2"));

        var updated = tree.Root.ReplaceNode(numbers[1], replacement);

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
        var comment = Assert.Single(tree.Root.DescendantTrivia().Where(trivia => trivia.Kind == JsonSyntaxKind.SingleLineCommentTrivia));

        var updated = tree.Root.ReplaceTrivia(comment, SyntaxFactory.Trivia(JsonSyntaxKind.MultiLineCommentTrivia, "/* new */"));

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
        var obj = SyntaxFactory.Object(
            SyntaxFactory.Member("name", SyntaxFactory.String("value")),
            SyntaxFactory.Member("count", SyntaxFactory.Number("42")));

        Assert.Equal("""{"name":"value","count":42}""", obj.ToFullString());
    }

    [Fact]
    public void WithChanges_ReparsesUpdatedText()
    {
        var tree = JsonSyntaxTree.ParseText("""{"a":1}""");

        var updated = tree.WithChanges(new JsonTextChange(new TextSpan(5, 1), "2"));

        Assert.Equal("""{"a":2}""", updated.Root.ToFullString());
    }

    [Fact]
    public void Visitor_VisitsAllValues()
    {
        var tree = JsonSyntaxTree.ParseText("""{"a":[1,"b",null]}""");
        var visitor = new CountingVisitor();

        visitor.Visit(tree.Root);

        Assert.Equal(5, visitor.ValueCount);
    }

    [Fact]
    public void Rewriter_CanUpdateMemberValue()
    {
        var tree = JsonSyntaxTree.ParseText("""{"a":1,"b":2}""");
        var rewriter = new ReplaceMemberValueRewriter("b", SyntaxFactory.Number("9"));

        var rewritten = rewriter.Visit(tree.Root);
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
        Assert.Equal(tree.Text.IndexOf("\"bar\"", StringComparison.Ordinal), name.Span.Start);
    }

    [Fact]
    public void EvaluateJsonPath_JsonSyntaxTree_CanEvaluateFromRoot()
    {
        var tree = CreateJsonPathSyntaxTree();
        var path = JsonPath.Parse("$.items[1].name");

        var treeResult = tree.Evaluate(path);
        var nodeResult = tree.Root.Evaluate(path);
        var treeValue = tree.EvaluateValue(path);
        var nodeValue = tree.Root.EvaluateValue(path);

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
        var nodeResult = path.Evaluate(tree.Root);

        Assert.Single(treeResult);
        Assert.Single(nodeResult);
        Assert.IsType<JsonObjectSyntax>(treeResult[0].Value);
        Assert.IsType<JsonObjectSyntax>(nodeResult[0].Value);
        Assert.Same(tree.Root.Value, treeResult[0].Value);
        Assert.Same(tree.Root.Value, nodeResult[0].Value);
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
        Assert.Equal(JsonSyntaxKind.JsonNullLiteral, value.Kind);
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

        Assert.NotEmpty(tree.Diagnostics);
        Assert.Equal(Text, tree.Root.ToFullString());
        Assert.Single(result);
        Assert.Equal("$[1]", result[0].Path);
        Assert.Equal("after", Assert.IsType<JsonStringSyntax>(afterInvalid).Value);
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
        Assert.Empty(tree.Diagnostics);

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
            Assert.Equal(expectedNames[i], Assert.IsType<JsonStringSyntax>(nameMember!.Value).Value);
        }
    }

    private sealed class CountingVisitor : JsonSyntaxVisitor
    {
        public int ValueCount { get; private set; }

        public override void VisitObject(JsonObjectSyntax node)
        {
            ValueCount++;
            base.VisitObject(node);
        }

        public override void VisitArray(JsonArraySyntax node)
        {
            ValueCount++;
            base.VisitArray(node);
        }

        public override void VisitNumber(JsonNumberSyntax node)
        {
            ValueCount++;
            base.VisitNumber(node);
        }

        public override void VisitString(JsonStringSyntax node)
        {
            ValueCount++;
            base.VisitString(node);
        }

        public override void VisitLiteral(JsonLiteralSyntax node)
        {
            ValueCount++;
            base.VisitLiteral(node);
        }
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

        public override JsonSyntaxNode? VisitMember(JsonMemberSyntax node)
        {
            if (string.Equals(node.Name, _name, StringComparison.Ordinal))
                return node.WithValue(_value);

            return base.VisitMember(node);
        }
    }
}
