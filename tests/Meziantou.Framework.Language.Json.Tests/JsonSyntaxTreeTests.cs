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
