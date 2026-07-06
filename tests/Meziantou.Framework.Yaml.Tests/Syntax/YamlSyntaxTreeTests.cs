using Meziantou.Framework.Yaml.Syntax;

namespace Meziantou.Framework.Yaml.Tests.Syntax;
public class YamlSyntaxTreeTests
{
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
}
