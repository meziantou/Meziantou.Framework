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

    [Fact]
    public void ReadmeExample_GitHubFlavored()
    {
        #region MarkdownSyntaxTree_GitHubFlavored
        var text = """
            | Task       | Done |
            | ---------- | :--: |
            | ~~Draft~~  | yes  |

            - [x] Read www.example.com
            """;
        var tree = MarkdownSyntaxTree.ParseText(text, MarkdownParseOptions.GitHubFlavored);

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal("""
            <table>
            <thead>
            <tr>
            <th>Task</th>
            <th align="center">Done</th>
            </tr>
            </thead>
            <tbody>
            <tr>
            <td><del>Draft</del></td>
            <td align="center">yes</td>
            </tr>
            </tbody>
            </table>
            <ul>
            <li><input checked="" disabled="" type="checkbox"> Read <a href="http://www.example.com">www.example.com</a></li>
            </ul>

            """.ReplaceLineEndings("\n"), MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void ReadmeExample_Extensions()
    {
        #region MarkdownSyntaxTree_Extensions
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Strikethrough };
        var tree = MarkdownSyntaxTree.ParseText("~~old~~ www.example.com", options);

        // Strikethrough only: the address is text
        Assert.Equal("<p><del>old</del> www.example.com</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
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

    [Theory]
    [InlineData("a\n| b |\n| - |\n", "<p>a</p>\n<table>\n<thead>\n<tr>\n<th>b</th>\n</tr>\n</thead>\n</table>\n")]
    [InlineData("[r]: /u\n| [r] |\n| - |\n", "<table>\n<thead>\n<tr>\n<th><a href=\"/u\">r</a></th>\n</tr>\n</thead>\n</table>\n")]
    [InlineData("a | b\n- | -\n", "<p>a | b</p>\n<ul>\n<li>| -</li>\n</ul>\n")]
    [InlineData("| a |\n| - |\n|\n", "<table>\n<thead>\n<tr>\n<th>a</th>\n</tr>\n</thead>\n</table>\n<p>|</p>\n")]
    [InlineData("| a |\n| - |\n    b\n", "<table>\n<thead>\n<tr>\n<th>a</th>\n</tr>\n</thead>\n</table>\n<pre><code>b\n</code></pre>\n")]
    [InlineData("| a \\\\| b |\n| - |\n", "<table>\n<thead>\n<tr>\n<th>a | b</th>\n</tr>\n</thead>\n</table>\n")]
    [InlineData("| a\v|\n| - \f|\n|\fb\n", "<table>\n<thead>\n<tr>\n<th>a</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>b</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("Foo\\\n---\f\n", "<table>\n<thead>\n<tr>\n<th>Foo\\</th>\n</tr>\n</thead>\n</table>\n")]
    [InlineData("| a | b |\n| - | - | - |\n| c | d |\n", "<p>| a | b |\n| - | - | - |\n| c | d |</p>\n")]
    [InlineData("- [x] a\n\n- [ ] b\n", "<ul>\n<li>\n<p><input checked=\"\" disabled=\"\" type=\"checkbox\"> a</p>\n</li>\n<li>\n<p><input disabled=\"\" type=\"checkbox\"> b</p>\n</li>\n</ul>\n")]
    [InlineData("> 1. [X] a\n", "<blockquote>\n<ol>\n<li><input checked=\"\" disabled=\"\" type=\"checkbox\"> a</li>\n</ol>\n</blockquote>\n")]
    [InlineData("- [ ]\n- [x]a\n- a [ ] b\n", "<ul>\n<li>[ ]</li>\n<li>[x]a</li>\n<li>a [ ] b</li>\n</ul>\n")]
    [InlineData("~a~ ~~b~~ ~~~c~~~\n", "<p><del>a</del> <del>b</del> ~~~c~~~</p>\n")]
    [InlineData("~~a~ ~b~~\n", "<p>~~a~ ~b~~</p>\n")]
    [InlineData("~~*a*~~ *~b~*\n", "<p><del><em>a</em></del> <em><del>b</del></em></p>\n")]
    [InlineData("see http://a.b/c. or HTTPS://a.b\n", "<p>see <a href=\"http://a.b/c\">http://a.b/c</a>. or <a href=\"HTTPS://a.b\">HTTPS://a.b</a></p>\n")]
    [InlineData("xhttp://a.b mailto://a.b\n", "<p>xhttp://a.b mailto://a.b</p>\n")]
    [InlineData("a(www.x.com) awww.x.com www.x\n", "<p>a(<a href=\"http://www.x.com\">www.x.com</a>) awww.x.com <a href=\"http://www.x\">www.x</a></p>\n")]
    [InlineData("**www.a.com**\n", "<p><strong><a href=\"http://www.a.com\">www.a.com</a></strong></p>\n")]
    [InlineData("[www.a.com and a@b.com\n", "<p>[www.a.com and <a href=\"mailto:a@b.com\">a@b.com</a></p>\n")]
    [InlineData("[a@b.com](/u)\n", "<p><a href=\"/u\">a@b.com</a></p>\n")]
    [InlineData("*a@b.com* a\\_b@c.com\n", "<p><em><a href=\"mailto:a@b.com\">a@b.com</a></em> <a href=\"mailto:a_b@c.com\">a_b@c.com</a></p>\n")]
    [InlineData("a@b.c1 a@b@c.d\n", "<p>a@b.c1 a@<a href=\"mailto:b@c.d\">b@c.d</a></p>\n")]
    [InlineData("<title>a</title>\n", "&lt;title>a&lt;/title>\n")]
    [InlineData("a <xmp/> <xmpx> <scripT\n/>\n", "<p>a &lt;xmp/> <xmpx> &lt;scripT\n/></p>\n")]
    public void RendersGitHubFlavoredHtml(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, MarkdownParseOptions.GitHubFlavored);

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Theory]
    [InlineData(MarkdownExtensions.Tables, "| a |\n| - |\n~~b~~ www.c.d\n", "<table>\n<thead>\n<tr>\n<th>a</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>~~b~~ www.c.d</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData(MarkdownExtensions.Strikethrough, "| a |\n| - |\n~~b~~\n", "<p>| a |\n| - |\n<del>b</del></p>\n")]
    [InlineData(MarkdownExtensions.None, "- [ ] a <script>\n", "<ul>\n<li>[ ] a <script></li>\n</ul>\n")]
    public void ParsesOnlyTheExtensionsAskedFor(MarkdownExtensions extensions, string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = extensions });

        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Fact]
    public void PipesOfATableAreTokensOfItsRows()
    {
        var root = MarkdownSyntaxTree.ParseText("| a\\|b | c |\n|:-| -: |\n", MarkdownParseOptions.GitHubFlavored).GetRoot();
        var table = Assert.Single(root.ChildNodes().Cast<MarkdownSyntaxNode>());
        var rows = table.ChildNodes().Cast<MarkdownSyntaxNode>().ToList();

        Assert.Equal(SyntaxKind.Table, table.Kind());
        Assert.Equal([SyntaxKind.TableHeaderRow, SyntaxKind.TableDelimiterRow], rows.Select(row => row.Kind()).ToArray());
        Assert.Equal(["|", "|", "|"], rows[0].ChildTokens().Select(token => token.Text).ToArray());

        // The backslash of an escaped pipe is in the token of the cell
        Assert.Equal(["a\\|b", "c"], rows[0].ChildNodes().Select(cell => string.Concat(cell.DescendantTokens().Select(token => token.Text))).ToArray());
        Assert.Equal(["|", ":-", "|", "-:", "|"], rows[1].ChildTokens().Select(token => token.Text).ToArray());
    }

    [Fact]
    public void TaskListItemMarkerIsATokenOfTheListItem()
    {
        var root = MarkdownSyntaxTree.ParseText("- [x] a\n", MarkdownParseOptions.GitHubFlavored).GetRoot();
        var item = root.DescendantNodes().Cast<MarkdownSyntaxNode>().Single(node => node.Kind() == SyntaxKind.ListItem);

        Assert.Equal([SyntaxKind.ListMarkerToken, SyntaxKind.TaskListItemMarkerToken], item.ChildTokens().Select(token => (SyntaxKind)token.RawKind).ToArray());
    }

    [Fact]
    public void LoneSurrogateInADestinationIsEncodedAsTheReplacementCharacter()
    {
        // Built in code: the strings of InlineData go through UTF-8, which replaces a lone surrogate
        var text = "[a](" + '\uD800' + ")\n";

        Assert.Equal("<p><a href=\"%EF%BF%BD\">a</a></p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(text)));
    }
}
