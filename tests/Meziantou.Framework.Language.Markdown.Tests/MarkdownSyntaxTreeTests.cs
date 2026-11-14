namespace Meziantou.Framework.Language.Markdown.Tests;

public sealed class MarkdownSyntaxTreeTests
{
    [Fact]
    public void ReadmeExample_Overview()
    {
        #region MarkdownSyntaxTree_Overview
        var text = """
            > A quote with *emphasis*
            > on two lines
            """;
        var tree = MarkdownSyntaxTree.ParseText(text);

        // Every character is in the tree: the markers of the quote on its second line are trivia
        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal("<blockquote>\n<p>A quote with <em>emphasis</em>\non two lines</p>\n</blockquote>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void ReadmeExample_Editing()
    {
        #region MarkdownSyntaxTree_Editing
        var root = MarkdownSyntaxTree.ParseText("> - first\n>   item\n> - second\n").GetRoot();
        var first = root.DescendantTokens().First(token => token.Text == "first");

        // The new token takes the trivia of the one it replaces
        var replacement = MarkdownSyntaxTree.ParseText("updated").GetRoot().DescendantTokens().First().WithTriviaFrom(first);
        var updated = root.ReplaceToken(first, replacement);

        // The markers of the quote and the indentation of the list item are where they were
        Assert.Equal("> - updated\n>   item\n> - second\n", updated.ToFullString());
        #endregion
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("a\r\nb\r\n")]
    [InlineData("a\rb\r")]
    [InlineData("> a\r\n> b\r\n\r\n> c")]
    [InlineData("\t\tcode\n\n\tmore\n")]
    [InlineData("-\tfoo\n\n\t\tbar\n")]
    [InlineData(">\t\tfoo\n")]
    [InlineData("a\0b\n")]
    [InlineData("\uFEFF# heading\n")]
    [InlineData("- a\n  > b\n  >\n- c\n")]
    [InlineData("[a]: /url 'title'\n\n[a]\n")]
    [InlineData("> [a\n> b]: /url\n> \"t\n> u\"\n")]
    [InlineData("> `a\n> b`\n")]
    [InlineData("```\n")]
    [InlineData("2) ````\n\t")]
    public void TreeReproducesTheText(string text)
    {
        var tree = MarkdownSyntaxTree.ParseText(text);

        Assert.Equal(text, tree.GetRoot().ToFullString());
        foreach (var token in tree.GetRoot().DescendantTokens())
        {
            foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia))
            {
                Assert.NotEqual((int)SyntaxKind.SkippedTextTrivia, trivia.RawKind);
            }
        }
    }

    [Fact]
    public void MarkersOfContinuationLinesAreTrivia()
    {
        var root = MarkdownSyntaxTree.ParseText("> a\n> b\n").GetRoot();
        var quote = Assert.Single(root.ChildNodes().Cast<MarkdownSyntaxNode>());
        var b = root.DescendantTokens().Single(token => token.Text == "b");

        Assert.Equal(SyntaxKind.BlockQuote, quote.Kind());
        Assert.Equal((int)SyntaxKind.BlockQuoteMarkerToken, quote.ChildTokens().Single().RawKind);
        Assert.Equal([SyntaxKind.BlockQuoteMarkerTrivia, SyntaxKind.WhitespaceTrivia], b.LeadingTrivia.Select(trivia => (SyntaxKind)trivia.RawKind).ToArray());
    }

    [Fact]
    public void IndentationOfListItemContinuationLinesIsTrivia()
    {
        var root = MarkdownSyntaxTree.ParseText("- a\n\n  b\n").GetRoot();
        var b = root.DescendantTokens().Single(token => token.Text == "b");

        Assert.Equal([SyntaxKind.EndOfLineTrivia, SyntaxKind.ListItemIndentTrivia], b.LeadingTrivia.Select(trivia => (SyntaxKind)trivia.RawKind).ToArray());
    }

    [Fact]
    public void LineEndingsInParagraphsAreTokens()
    {
        var root = MarkdownSyntaxTree.ParseText("a  \nb\nc\n").GetRoot();
        var paragraph = Assert.Single(root.ChildNodes().Cast<MarkdownSyntaxNode>());

        Assert.Equal(
            [SyntaxKind.TextToken, SyntaxKind.HardLineBreakToken, SyntaxKind.TextToken, SyntaxKind.LineEndingToken, SyntaxKind.TextToken],
            paragraph.ChildTokens().Select(token => (SyntaxKind)token.RawKind).ToArray());
    }

    [Fact]
    public void EmphasisDelimitersAreTokensOfTheEmphasis()
    {
        var root = MarkdownSyntaxTree.ParseText("***a***\n").GetRoot();
        var strong = root.DescendantNodes().Cast<MarkdownSyntaxNode>().Single(node => node.Kind() == SyntaxKind.StrongEmphasis);
        var emphasis = root.DescendantNodes().Cast<MarkdownSyntaxNode>().Single(node => node.Kind() == SyntaxKind.Emphasis);

        // <em><strong>a</strong></em>: the delimiters next to the content are used first
        Assert.Equal(["*", "*"], emphasis.ChildTokens().Select(token => token.Text).ToArray());
        Assert.Equal(["**", "a", "**"], strong.ChildTokens().Select(token => token.Text).ToArray());
        Assert.Same(emphasis, strong.Parent);
    }

    [Theory]
    [InlineData("2) ````\n\t", "<ol start=\"2\">\n<li>\n<pre><code>\n</code></pre>\n</li>\n</ol>\n")]
    [InlineData("[a](\t<b)c>)\n", "<p><a href=\"b)c\">a</a></p>\n")]
    [InlineData("[a](<')~>)\n", "<p><a href=\"')~\">a</a></p>\n")]
    public void RendersHtml(string text, string expected)
    {
        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(text)));
    }

    [Fact]
    public void LoneSurrogateInADestinationIsEncodedAsTheReplacementCharacter()
    {
        // Built in code: the strings of InlineData go through UTF-8, which replaces a lone surrogate
        var text = "[a](" + '\uD800' + ")\n";

        Assert.Equal("<p><a href=\"%EF%BF%BD\">a</a></p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(text)));
    }
}
