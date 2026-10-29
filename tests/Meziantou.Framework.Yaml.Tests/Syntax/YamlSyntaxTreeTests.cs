using Meziantou.Framework.Yaml.Syntax;

namespace Meziantou.Framework.Yaml.Tests.Syntax;
public class YamlSyntaxTreeTests
{
    [Fact]
    public void ReadmeExample_Parse()
    {
        #region YamlSyntaxTree_Parse
        var yaml = """
            # deployment settings
            replicas: 3 # keep in sync with the chart
            """;

        var tree = YamlSyntaxTree.Parse(yaml, new YamlSyntaxOptions { IncludeTrivia = true });

        var tokens = new List<string>();
        foreach (var token in tree.Tokens)
        {
            tokens.Add($"{token.Kind} {token.Span} {token.Text}");
        }

        Assert.Equal(
        [
            "StreamStart 0:0 ",
            "CommentTrivia 0:21 # deployment settings",
            "NewLineTrivia 21:22 \n",
            "BlockMappingStart 22:22 ",
            "Key 22:22 ",
            "Scalar 22:30 replicas",
            "Value 30:31 :",
            "WhitespaceTrivia 31:32  ",
            "Scalar 32:33 3",
            "WhitespaceTrivia 33:34  ",
            "CommentTrivia 34:63 # keep in sync with the chart",
            "BlockEnd 63:63 ",
            "StreamEnd 63:63 ",
        ], tokens);
        Assert.Equal(yaml, tree.ToFullString());
        #endregion
    }

    [Fact]
    public void Documentation_EditValue()
    {
        #region YamlSyntaxTree_EditValue
        var yaml = """
            # deployment settings
            replicas: 3 # keep in sync with the chart
            image: nginx
            """;

        // Without trivia, the tokens are the significant ones: Key, Scalar, Value, Scalar, ...
        var tokens = YamlSyntaxTree.Parse(yaml, new YamlSyntaxOptions { IncludeTrivia = false }).Tokens;
        var keyIndex = tokens.ToList().FindIndex(token => token.Kind is YamlSyntaxKind.Scalar && token.Text == "replicas");
        var value = tokens[keyIndex + 2];
        Assert.Equal(YamlSyntaxKind.Scalar, value.Kind);

        // Replace the text of the value only: the comments and the layout are kept
        var updated = string.Concat(yaml.AsSpan(0, value.Span.Start.Index), "5", yaml.AsSpan(value.Span.End.Index));
        Assert.Equal("""
            # deployment settings
            replicas: 5 # keep in sync with the chart
            image: nginx
            """, updated);
        #endregion
    }

    public static TheoryData<string> RoundTripCases { get; } =
    [
        "# comment\nroot:\n  key: value\n\nlist:\n  - a\n  - b\n",
        "flow: { a: 1, b: [2, 3] }\nquoted: \"line\\nvalue\"\n",
        "%TAG !e! tag:example.com,2026:\n---\nnode: &n1 !e!name value\nalias: *n1\n...\n",
        "a: 1\r\nb:\r\n  - 2\r\n  - 3\r\n",
    ];

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void ParseAndRoundTripPreservesOriginalText(string yaml)
    {
        var tree = YamlSyntaxTree.Parse(yaml);

        Assert.Equal(yaml, tree.ToFullString());

        using var writer = new StringWriter();
        tree.WriteTo(writer);
        Assert.Equal(yaml, writer.ToString());
    }

    [Fact]
    public void ParseExposesExpectedSpans()
    {
        const string Yaml = "key: value\n# comment\nlist:\n  - 1\n";
        var tree = YamlSyntaxTree.Parse(Yaml);
        var scalarTokens = tree.Tokens.Where(token => token.Kind is YamlSyntaxKind.Scalar).ToArray();
        var commentToken = tree.Tokens.First(token => token.Kind is YamlSyntaxKind.CommentTrivia);

        Assert.HasCountGreaterThanOrEqual(3, scalarTokens);
        Assert.Equal(0, scalarTokens[0].Span.Start.Index);
        Assert.Equal(0, scalarTokens[0].Span.Start.Line);
        Assert.Equal(0, scalarTokens[0].Span.Start.Column);

        Assert.Equal(5, scalarTokens[1].Span.Start.Index);
        Assert.Equal(0, scalarTokens[1].Span.Start.Line);
        Assert.Equal(5, scalarTokens[1].Span.Start.Column);

        Assert.Equal(11, commentToken.Span.Start.Index);
        Assert.Equal(1, commentToken.Span.Start.Line);
        Assert.Equal(0, commentToken.Span.Start.Column);
    }

    [Fact]
    public void ParseIncludesTriviaByDefault()
    {
        const string Yaml = "a: 1\n# c\n";
        var tree = YamlSyntaxTree.Parse(Yaml);

        Assert.Contains(tree.Tokens, token => token.Kind is YamlSyntaxKind.CommentTrivia);
        Assert.Contains(tree.Tokens, token => token.Kind is YamlSyntaxKind.NewLineTrivia);
    }

    [Fact]
    public void ParseCanExcludeTrivia()
    {
        const string Yaml = "a: 1\n# c\n";
        var tree = YamlSyntaxTree.Parse(Yaml, new YamlSyntaxOptions { IncludeTrivia = false });

        Assert.DoesNotContain(tree.Tokens, token => token.Kind is YamlSyntaxKind.CommentTrivia);
        Assert.DoesNotContain(tree.Tokens, token => token.Kind is YamlSyntaxKind.NewLineTrivia);
    }

    [Theory]
    [InlineData("---\na: 1\n? b\n&anchor c: 3\n")]
    [InlineData("&a a: b\nc: &d d\n")]
    public void ParseKeepsTheScannerOrderOfTokensAtTheSamePosition(string yaml)
    {
        static string Format(IEnumerable<YamlSyntaxToken> tokens) => string.Join('\n', tokens.Select(token => $"{token.Kind} {token.Span.Start.Index}..{token.Span.End.Index}"));

        var withTrivia = YamlSyntaxTree.Parse(yaml, new YamlSyntaxOptions { IncludeTrivia = true });
        var withoutTrivia = YamlSyntaxTree.Parse(yaml, new YamlSyntaxOptions { IncludeTrivia = false });

        var expected = Format(withoutTrivia.Tokens);
        Assert.Equal(expected, Format(withTrivia.Tokens.Where(token => token.Kind is not (YamlSyntaxKind.WhitespaceTrivia or YamlSyntaxKind.NewLineTrivia or YamlSyntaxKind.CommentTrivia))));
        Assert.True(withoutTrivia.Tokens.ToList().FindIndex(token => token.Kind is YamlSyntaxKind.BlockMappingStart) < withoutTrivia.Tokens.ToList().FindIndex(token => token.Kind is YamlSyntaxKind.Key));
        Assert.Equal(YamlSyntaxKind.StreamEnd, withoutTrivia.Tokens[^1].Kind);
    }

    [Fact]
    public void ParseInvalidYamlThrowsWithMarks()
    {
        const string Yaml = "a: [1, 2\n";
        YamlException ex;
        try
        {
            YamlSyntaxTree.Parse(Yaml);
            Assert.Fail("Expected a YAML exception.");
            return;
        }
        catch (YamlException yamlException)
        {
            ex = yamlException;
        }

        Assert.True(ex.Start.Index >= 0);
        Assert.True(ex.End.Index >= ex.Start.Index);
    }

    [Fact]
    public void ParseDoesNotTreatAHashInsideAQuotedScalarAsAComment()
    {
        const string Yaml = "value: \"a#b\" # real\n";
        var tree = YamlSyntaxTree.Parse(Yaml);

        var comment = Assert.Single(tree.Tokens, token => token.Kind is YamlSyntaxKind.CommentTrivia);
        Assert.Equal("# real", comment.Text);
        Assert.Equal(13, comment.Span.Start.Index);
        Assert.Equal(19, comment.Span.End.Index);

        var scalars = tree.Tokens.Where(token => token.Kind is YamlSyntaxKind.Scalar).ToArray();
        Assert.Equal("\"a#b\"", scalars[1].Text);
        Assert.Equal(7, scalars[1].Span.Start.Index);
        Assert.Equal(12, scalars[1].Span.End.Index);
    }

    [Fact]
    public void ParseDoesNotTreatAHashInsideAPlainScalarAsAComment()
    {
        const string Yaml = "value: a#b\n";
        var tree = YamlSyntaxTree.Parse(Yaml);

        Assert.DoesNotContain(tree.Tokens, token => token.Kind is YamlSyntaxKind.CommentTrivia);
    }

    [Fact]
    public void ParseDoesNotTreatABlockScalarBodyAsTrivia()
    {
        const string Yaml = "a: |\n  x#y\n  z\nb: 1 # c\n";
        var tree = YamlSyntaxTree.Parse(Yaml);

        var comment = Assert.Single(tree.Tokens, token => token.Kind is YamlSyntaxKind.CommentTrivia);
        Assert.Equal("# c", comment.Text);

        // The block scalar body is covered by its scalar token, so its indentation is not whitespace trivia either.
        Assert.DoesNotContain(tree.Tokens, token =>
            token.Kind is YamlSyntaxKind.WhitespaceTrivia &&
            token.Span.Start.Index >= 4 &&
            token.Span.Start.Index < 15);
    }

    [Fact]
    public void ParseKeepsTriviaSpansOutsideOfScannerTokens()
    {
        const string Yaml = "key: value # c\n";
        var tree = YamlSyntaxTree.Parse(Yaml);

        foreach (var trivia in tree.Tokens.Where(token => token.Kind is YamlSyntaxKind.WhitespaceTrivia or YamlSyntaxKind.NewLineTrivia or YamlSyntaxKind.CommentTrivia))
        {
            Assert.Equal(Yaml[trivia.Span.Start.Index..trivia.Span.End.Index], trivia.Text);
        }
    }

    [Fact]
    public void ParseComputesTriviaPositionsAcrossLineBreakKinds()
    {
        const string Yaml = "a: 1 # one\r\nb: 2 # two\rc: 3 # three\nd: 4 # four\n";
        var tree = YamlSyntaxTree.Parse(Yaml);

        var comments = tree.Tokens.Where(token => token.Kind is YamlSyntaxKind.CommentTrivia).ToArray();

        Assert.Equal(new[] { "# one", "# two", "# three", "# four" }, comments.Select(token => token.Text).ToArray());
        Assert.Equal(new[] { 0, 1, 2, 3 }, comments.Select(token => token.Span.Start.Line).ToArray());
        Assert.Equal(new[] { 5, 5, 5, 5 }, comments.Select(token => token.Span.Start.Column).ToArray());
    }
}
