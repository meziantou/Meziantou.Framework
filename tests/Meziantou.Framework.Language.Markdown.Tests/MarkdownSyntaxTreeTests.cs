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
    [InlineData("<div>a\0\r\nb</div>\r\n", "<div>a\uFFFD\nb</div>\n")]
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
    [InlineData("[r]: /u\n-\n\n[r]\n", "<p>-</p>\n<p><a href=\"/u\">r</a></p>\n")]
    [InlineData("[r]: /u\n-\n|-|\n\n[r]\n", "<table>\n<thead>\n<tr>\n<th>-</th>\n</tr>\n</thead>\n</table>\n<p><a href=\"/u\">r</a></p>\n")]
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
    [InlineData("<div>\n<script\nx\n", "<div>\n&lt;script\nx\n")]
    [InlineData("<div>\n<script\n", "<div>\n<script\n")]
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

    [Theory]
    [InlineData(MarkdownExtensions.EmphasisExtras, "~a~ ~~b~~ ~~~c~~~\n", "<p><sub>a</sub> <del>b</del> <sub><del>c</del></sub></p>\n")]
    [InlineData(MarkdownExtensions.Strikethrough, "~a~ ~~b~~ ~~~c~~~\n", "<p><del>a</del> <del>b</del> ~~~c~~~</p>\n")]
    [InlineData(MarkdownExtensions.Subscript, "~a~ ~~b~~\n", "<p><sub>a</sub> <sub><sub>b</sub></sub></p>\n")]
    [InlineData(MarkdownExtensions.EmphasisExtras, "+a+ ++b++ +++c+++ ==d== =e=\n", "<p>+a+ <ins>b</ins> +<ins>c</ins>+ <mark>d</mark> =e=</p>\n")]
    // The punctuation that the extra emphasis reads as text is punctuation for * and _, as CommonMark says
    [InlineData(MarkdownExtensions.EmphasisExtras, "x^-1^ x^a b^ a*-b*\n", "<p>x<sup>-1</sup> x<sup>a b</sup> a*-b*</p>\n")]
    [InlineData(MarkdownExtensions.EmphasisExtras, "^*a*^ *^a^*\n", "<p><sup><em>a</em></sup> <em><sup>a</sup></em></p>\n")]
    [InlineData(MarkdownExtensions.HardLineBreaks, "a\nb\\\nc\n", "<p>a<br />\nb<br />\nc</p>\n")]
    public void RendersTheHtmlOfTheExtensions(MarkdownExtensions extensions, string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = extensions });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Theory]
    [InlineData("<div>\na\n</div>\n", "<p>&lt;div&gt;\na\n&lt;/div&gt;</p>\n")]
    [InlineData("<b>a</b> <https://a.b> <!-- c -->\n", "<p>&lt;b&gt;a&lt;/b&gt; <a href=\"https://a.b\">https://a.b</a> &lt;!-- c --&gt;</p>\n")]
    public void DisableHtmlRendersHtmlAsText(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { DisableHtml = true });

        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));
        Assert.DoesNotContain(tree.GetRoot().DescendantNodes(), node => node is MarkdownHtmlBlockSyntax or MarkdownRawHtmlSyntax);
    }

    [Fact]
    public void FrontMatterHasItsYaml()
    {
        var root = MarkdownSyntaxTree.ParseText("---  \r\ntitle: a\r\n\r\ntags: [b]\r\n...\r\n# Heading\r\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.YamlFrontMatter }).GetRoot();
        var frontMatter = Assert.IsType<MarkdownFrontMatterSyntax>(root.Blocks[0]);

        Assert.Equal("title: a\n\ntags: [b]\n", frontMatter.Yaml);
        Assert.Equal("...", frontMatter.ClosingFenceToken.Text);
        Assert.IsType<MarkdownHeadingSyntax>(root.Blocks[1]);
        Assert.Equal("Heading", root.GetPlainText());
    }

    [Theory]
    [InlineData("---\na: b\n", "<hr />\n<p>a: b</p>\n")]
    [InlineData(" ---\na\n---\n", "<hr />\n<h2>a</h2>\n")]
    [InlineData("---\n---\nb\n", "<p>b</p>\n")]
    [InlineData("```mermaid\na --> b & <c>\n```\n```nomnoml\n[a]\n```\n```c\n<x>\n```\n", "<pre class=\"mermaid\">a --> b &amp; &lt;c>\n</pre>\n<div class=\"nomnoml\">[a]\n</div>\n<pre><code class=\"language-c\">&lt;x&gt;\n</code></pre>\n")]
    public void RendersFrontMatterAndDiagrams(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.YamlFrontMatter | MarkdownExtensions.Diagrams });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Theory]
    [InlineData(false, "> [!Custom]\n> a\n", "<div class=\"markdown-alert markdown-alert-custom\">\n<p>a</p>\n</div>\n")]
    [InlineData(false, "> [!NOTE]\n", "<div class=\"markdown-alert markdown-alert-note\">\n<p class=\"markdown-alert-title\">")]
    [InlineData(false, "- > [!NOTE]\n", "<ul>\n<li>\n<blockquote>\n<p>[!NOTE]</p>\n</blockquote>\n</li>\n</ul>\n")]
    [InlineData(true, "- > [!NOTE]\n", "<ul>\n<li>\n<div class=\"markdown-alert markdown-alert-note\">\n")]
    [InlineData(true, "> [!NOTE]\n> > [!TIP]\n", "</p>\n<blockquote>\n<p>[!TIP]</p>\n</blockquote>\n</div>\n")]
    [InlineData(false, "> [!NOTE] a\n> [!] b\n", "<blockquote>\n<p>[!NOTE] a\n[!] b</p>\n</blockquote>\n")]
    public void RendersAlerts(bool allowNested, string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.Alerts, AllowNestedAlerts = allowNested });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Contains(expected, MarkdownHtmlRenderer.ToHtml(tree), StringComparison.Ordinal);
    }

    // The occurrences are found in the literals of Markdig: an escaped character starts one, and a run of delimiters ends one
    [Theory]
    [InlineData("*[A  B]: x\n\nA B and A  B\n", "<p><abbr title=\"x\">A B</abbr> and A  B</p>\n")]
    [InlineData("*[a\\]b]: x\n\na]b a\\]b\n", "<p><abbr title=\"x\">a]b</abbr> a]b</p>\n")]
    [InlineData("*[*A]: x\n\n\\*A and A\n", "<p><abbr title=\"x\">*A</abbr> and A</p>\n")]
    [InlineData("*[A]: x\n\n\\*A &amp;A *A* _A b*A !A wA\n", "<p>*A &amp;<abbr title=\"x\">A</abbr> <em><abbr title=\"x\">A</abbr></em> _<abbr title=\"x\">A</abbr> b*<abbr title=\"x\">A</abbr> !A wA</p>\n")]
    [InlineData("*[A]: x\n\n[A](u) ![A](u) `A` A, A.b A.\n", "<p><a href=\"u\"><abbr title=\"x\">A</abbr></a> <img src=\"u\" alt=\"A\" /> <code>A</code> <abbr title=\"x\">A</abbr>, A.b <abbr title=\"x\">A</abbr>.</p>\n")]
    [InlineData("*[A]: x\n*[A]: y\n\nA\n", "<p><abbr title=\"y\">A</abbr></p>\n")]
    [InlineData("*[A]: \u00A0x\u00A0 \n\nA\n", "<p><abbr title=\"\u00A0x\u00A0\">A</abbr></p>\n")]
    public void RendersAbbreviations(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.Abbreviations });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Fact]
    public void AbbreviationsCannotExpandTheTextBeyondItsLimit()
    {
        // The titles that the abbreviations write are limited to the length of the text, and to at least 100,000 characters
        var text = "*[A]: " + new string('x', 1000) + "\n\n" + string.Concat(Enumerable.Repeat("A ", 200)) + "\n";

        var abbreviations = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.Abbreviations }).GetRoot().DescendantNodes().OfType<MarkdownAbbreviationSyntax>().ToList();

        Assert.HasCount(100, abbreviations);
        Assert.Equal("A", abbreviations[0].Label);
        Assert.HasCount(1000, abbreviations[0].Definition!.Title);
    }

    [Fact]
    public void AlertHasItsKind()
    {
        var alert = Assert.IsType<MarkdownAlertSyntax>(MarkdownSyntaxTree.ParseText("> [!Warning]  \n> Be *careful*\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.Alerts }).GetRoot().Blocks.Single());

        Assert.Equal("Warning", alert.AlertKind);
        Assert.Equal("[!Warning]", alert.KindToken.Text);
        Assert.Equal("Be careful", alert.GetPlainText());
    }

    [Fact]
    public void MathHasItsContent()
    {
        var root = MarkdownSyntaxTree.ParseText("- $$  a \\$ b  $$\n\n  $$\n  x < y\n  $$\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.Mathematics }).GetRoot();
        var inline = root.DescendantNodes().OfType<MarkdownMathInlineSyntax>().Single();
        var block = root.DescendantNodes().OfType<MarkdownMathBlockSyntax>().Single();

        Assert.Equal("a \\$ b", inline.Math);
        Assert.Equal("$$", inline.ClosingDelimiterToken.Text);
        Assert.Equal("x < y\n", block.Math);
        Assert.Equal("$$", block.ClosingFenceToken.Text);
        Assert.Equal("<ul>\n<li>\n<p><span class=\"math\">\\(a \\$ b\\)</span></p>\n<div class=\"math\">\n\\[\nx &lt; y\n\\]</div>\n</li>\n</ul>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(root.ToFullString(), new MarkdownParseOptions { Extensions = MarkdownExtensions.Mathematics })));
    }

    [Fact]
    public void EmphasisExtraHasItsStyle()
    {
        var styles = MarkdownSyntaxTree.ParseText("~a~ ^b^ ++c++ ==d==\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.EmphasisExtras })
            .GetRoot().DescendantNodes().OfType<MarkdownEmphasisExtraSyntax>().ToList();

        Assert.Equal([MarkdownEmphasisExtraStyle.Subscript, MarkdownEmphasisExtraStyle.Superscript, MarkdownEmphasisExtraStyle.Inserted, MarkdownEmphasisExtraStyle.Marked], styles.Select(style => style.Style).ToArray());
        Assert.Equal("++", styles[2].OpeningDelimiterToken.Text);
        Assert.Equal("d", styles[3].GetPlainText());
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

    [Fact]
    public void ReadmeExample_TypedApi()
    {
        #region MarkdownSyntaxTree_TypedApi
        var tree = MarkdownSyntaxTree.ParseText("""
            # Install *the* tool

            See [the guide][guide] or ![logo](/logo.png "Logo").

            [guide]: https://example.com/guide
            """);
        var root = tree.GetRoot();

        var heading = Assert.IsType<MarkdownHeadingSyntax>(root.Blocks[0]);
        Assert.Equal(1, heading.Level);
        Assert.Equal("Install the tool", heading.GetPlainText());

        // A reference link gets its destination from its definition
        var links = root.DescendantNodes().OfType<MarkdownLinkSyntax>().ToList();
        Assert.Equal(MarkdownLinkKind.Full, links[0].LinkKind);
        Assert.Equal("https://example.com/guide", links[0].Destination);
        Assert.True(links[1].IsImage);
        Assert.Equal("Logo", links[1].Title);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Rewriter()
    {
        #region MarkdownSyntaxRewriter_Usage
        var tree = MarkdownSyntaxTree.ParseText("<div>\nhidden\n</div>\n\nText with <b>tags</b>.\n");

        var root = new RemoveHtml().Visit(tree.GetRoot())!;

        Assert.Equal("\nText with tags.\n", root.ToFullString());
        #endregion
    }

    #region MarkdownSyntaxRewriter_RemoveHtml
    private sealed class RemoveHtml : MarkdownSyntaxRewriter
    {
        public override SyntaxNode? VisitMarkdownHtmlBlock(MarkdownHtmlBlockSyntax node) => null;

        public override SyntaxNode? VisitMarkdownRawHtml(MarkdownRawHtmlSyntax node) => null;
    }
    #endregion

    [Fact]
    public void ReadmeExample_InsertBlocks()
    {
        #region MarkdownSyntaxTree_InsertBlocks
        var tree = MarkdownSyntaxTree.ParseText("> - first\n> - second\n");
        var first = tree.GetRoot().DescendantNodes().OfType<MarkdownListItemSyntax>().First();

        // The markers of the block quote are added to the new line
        var updated = tree.InsertBlocksAfter(first, "- inserted");

        Assert.Equal("> - first\n> - inserted\n> - second\n", updated.GetRoot().ToFullString());
        Assert.HasCount(3, updated.GetRoot().DescendantNodes().OfType<MarkdownListItemSyntax>());
        #endregion
    }

    [Theory]
    [InlineData("# a #\n", 1, false)]
    [InlineData("###### a\n", 6, false)]
    [InlineData("a\n===\n", 1, true)]
    [InlineData("a\n---\n", 2, true)]
    public void HeadingHasItsLevel(string text, int level, bool setext)
    {
        var heading = Assert.IsType<MarkdownHeadingSyntax>(MarkdownSyntaxTree.ParseText(text).GetRoot().Blocks.Single());

        Assert.Equal(level, heading.Level);
        Assert.Equal(setext, heading.IsSetext);
        Assert.Equal("a", heading.GetPlainText());
    }

    [Fact]
    public void ListHasItsMarkersAndTightness()
    {
        var blocks = MarkdownSyntaxTree.ParseText("3) a\n4) b\n\n- c\n\n- d\n", MarkdownParseOptions.GitHubFlavored).GetRoot().Blocks;
        var ordered = Assert.IsType<MarkdownListSyntax>(blocks[0]);
        var bullet = Assert.IsType<MarkdownListSyntax>(blocks[1]);

        Assert.True(ordered.IsOrdered);
        Assert.Equal(3, ordered.StartNumber);
        Assert.Equal(')', ordered.MarkerCharacter);
        Assert.True(ordered.IsTight);
        Assert.Equal(2, ordered.Items.Count);
        Assert.False(bullet.IsOrdered);
        Assert.Null(bullet.StartNumber);
        Assert.Equal('-', bullet.MarkerCharacter);
        Assert.False(bullet.IsTight);
    }

    [Fact]
    public void TaskListItemIsChecked()
    {
        var items = MarkdownSyntaxTree.ParseText("- [x] a\n- [ ] b\n- c\n", MarkdownParseOptions.GitHubFlavored).GetRoot().DescendantNodes().OfType<MarkdownListItemSyntax>().ToList();

        Assert.Equal([true, true, false], items.Select(item => item.IsTask).ToArray());
        Assert.Equal([true, false, false], items.Select(item => item.IsChecked).ToArray());
        Assert.Equal("[x]", items[0].TaskListItemMarkerToken.Text);
    }

    [Fact]
    public void CodeBlockHasItsCodeAndLanguage()
    {
        var blocks = MarkdownSyntaxTree.ParseText("> ```c\\# extra\n> a\n>\tb\n> ```\n\n    code\n").GetRoot().DescendantNodes().OfType<MarkdownCodeBlockSyntax>().ToList();

        Assert.True(blocks[0].IsFenced);
        Assert.Equal("c# extra", blocks[0].Info);
        Assert.Equal("c#", blocks[0].Language);
        Assert.Equal("a\n  b\n", blocks[0].Code);
        Assert.Equal("```", blocks[0].ClosingFenceToken.Text);
        Assert.False(blocks[1].IsFenced);
        Assert.Null(blocks[1].Language);
        Assert.Equal("code\n", blocks[1].Code);
        Assert.Equal(0, blocks[1].OpeningFenceToken.RawKind);
    }

    [Fact]
    public void HtmlBlockAndRawHtmlHaveTheirHtml()
    {
        var root = MarkdownSyntaxTree.ParseText("<div>\r\na\r\n</div>\r\n\r\nb <span\r\nid=\"x\">\r\n").GetRoot();

        Assert.Equal("<div>\na\n</div>", Assert.IsType<MarkdownHtmlBlockSyntax>(root.Blocks[0]).Html);
        Assert.Equal("<span\nid=\"x\">", root.DescendantNodes().OfType<MarkdownRawHtmlSyntax>().Single().Html);
    }

    [Theory]
    [InlineData("[a](</my url> 'the \\'title\\'')\n", MarkdownLinkKind.Inline, "/my url", "the 'title'", null)]
    [InlineData("[a][B]\n\n[b]: /u \"t\"\n", MarkdownLinkKind.Full, "/u", "t", "B")]
    [InlineData("[a][]\n\n[A]: /u\n", MarkdownLinkKind.Collapsed, "/u", null, "a")]
    [InlineData("[*a*]\n\n[*A*]: /u&amp;v\n", MarkdownLinkKind.Shortcut, "/u&v", null, "*a*")]
    public void LinkHasItsDestination(string text, MarkdownLinkKind kind, string destination, string? title, string? label)
    {
        var link = MarkdownSyntaxTree.ParseText(text).GetRoot().DescendantNodes().OfType<MarkdownLinkSyntax>().Single();

        Assert.Equal(kind, link.LinkKind);
        Assert.Equal(destination, link.Destination);
        Assert.Equal(title, link.Title);
        Assert.Equal(label, link.Label);
        Assert.Equal("a", link.GetPlainText());
    }

    [Fact]
    public void LinkReferenceDefinitionsAreFoundByLabel()
    {
        var root = MarkdownSyntaxTree.ParseText("> [Foo  Bar]: /first\n\n[foo bar]: /second\n").GetRoot();

        // The first definition of a label wins, and labels are compared without case and extra whitespace
        Assert.HasCount(2, root.LinkReferenceDefinitions);
        Assert.Equal("/first", root.GetLinkReferenceDefinition("FOO BAR")!.Destination);
        Assert.Equal("Foo  Bar", root.GetLinkReferenceDefinition("foo bar")!.Label);
        Assert.Null(root.GetLinkReferenceDefinition("other"));
    }

    [Theory]
    [InlineData("<https://a.b/c>", false, false, "https://a.b/c", "https://a.b/c")]
    [InlineData("<a@b.c>", false, true, "a@b.c", "mailto:a@b.c")]
    [InlineData("www.a.b", true, false, "www.a.b", "http://www.a.b")]
    [InlineData("a\\_b@c.d", true, true, "a_b@c.d", "mailto:a_b@c.d")]
    public void AutolinkHasItsDestination(string text, bool extended, bool email, string expectedText, string destination)
    {
        var autolink = MarkdownSyntaxTree.ParseText(text, MarkdownParseOptions.GitHubFlavored).GetRoot().DescendantNodes().OfType<MarkdownAutolinkSyntax>().Single();

        Assert.Equal(extended, autolink.IsExtended);
        Assert.Equal(email, autolink.IsEmail);
        Assert.Equal(expectedText, autolink.Text);
        Assert.Equal(destination, autolink.Destination);
    }

    [Fact]
    public void InlinesHaveTheirContent()
    {
        var root = MarkdownSyntaxTree.ParseText("**a** _b_ ~~c~~ ``  ` d ` `` e\\*&lt;\n", MarkdownParseOptions.GitHubFlavored).GetRoot();
        var emphases = root.DescendantNodes().OfType<MarkdownEmphasisSyntax>().ToList();

        Assert.True(emphases[0].IsStrong);
        Assert.Equal("**", emphases[0].OpeningDelimiterToken.Text);
        Assert.False(emphases[1].IsStrong);
        Assert.Equal("~~", root.DescendantNodes().OfType<MarkdownStrikethroughSyntax>().Single().ClosingDelimiterToken.Text);
        Assert.Equal(" ` d `", root.DescendantNodes().OfType<MarkdownCodeSpanSyntax>().Single().Code);
        Assert.Equal("a b c  ` d ` e*<", root.Blocks[0].GetPlainText());
    }

    [Fact]
    public void TableHasItsRowsAndAlignments()
    {
        var table = Assert.IsType<MarkdownTableSyntax>(MarkdownSyntaxTree.ParseText("| a | b | c | d |\n|---|:--|:-:|--:|\n| `x\\|y` | [l](/u\\|v) |\n", MarkdownParseOptions.GitHubFlavored).GetRoot().Blocks.Single());

        Assert.Equal([MarkdownTableAlignment.None, MarkdownTableAlignment.Left, MarkdownTableAlignment.Center, MarkdownTableAlignment.Right], table.Alignments.ToArray());
        Assert.True(table.HeaderRow.IsHeader);
        Assert.True(table.DelimiterRow.IsDelimiter);
        Assert.Equal(4, table.DelimiterRow.DelimiterTokens.Count);
        Assert.Equal(["a", "b", "c", "d"], table.HeaderRow.Cells.Select(cell => cell.GetPlainText()).ToArray());

        // In a cell, \| is a pipe, even in a code span and a link destination
        var row = Assert.Single(table.Rows);
        Assert.Equal("x|y", row.DescendantNodes().OfType<MarkdownCodeSpanSyntax>().Single().Code);
        Assert.Equal("/u|v", row.DescendantNodes().OfType<MarkdownLinkSyntax>().Single().Destination);
    }

    [Fact]
    public void WalkerVisitsEveryNode()
    {
        var root = MarkdownSyntaxTree.ParseText(CommonMarkSpec.Examples[300].Markdown + "\n" + CommonMarkSpec.GfmExamples[0].Markdown, MarkdownParseOptions.GitHubFlavored).GetRoot();
        var walker = new CountingWalker();

        walker.Visit(root);

        Assert.Equal(root.DescendantNodesAndSelf().Count(), walker.Count);
    }

    [Fact]
    public void RewriterThatChangesNothingKeepsTheNodes()
    {
        var root = MarkdownSyntaxTree.ParseText("> - a *b*\n", MarkdownParseOptions.GitHubFlavored).GetRoot();

        Assert.Same(root, new MarkdownSyntaxRewriter().Visit(root));
    }

    [Fact]
    public void RewriterReplacesTokens()
    {
        var root = MarkdownSyntaxTree.ParseText("> # old\n>\n> *old*\n").GetRoot();

        var rewritten = new ReplaceText().Visit(root)!;

        Assert.Equal("> # new\n>\n> *new*\n", rewritten.ToFullString());
        Assert.IsType<MarkdownDocumentSyntax>(rewritten);
        Assert.Equal("new\nnew", ((MarkdownDocumentSyntax)rewritten).GetPlainText());
    }

    [Theory]
    [InlineData("- a\n- b\n", true, 1, "- c\n", "- a\n- c\n- b\n")]
    [InlineData("- a\n\n  b\n", false, 0, "c\n\n", "- c\n\n  a\n\n  b\n")]
    [InlineData("> a\r\n", false, 0, "b\n\nc", "> b\r\n>\r\n> c\r\n> a\r\n")]
    public void InsertsBlocksBefore(string text, bool listItem, int blockIndex, string markdown, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text);
        var nodes = tree.GetRoot().DescendantNodes();
        var block = listItem ? (MarkdownBlockSyntax)nodes.OfType<MarkdownListItemSyntax>().ElementAt(blockIndex) : nodes.OfType<MarkdownParagraphSyntax>().ElementAt(blockIndex);

        Assert.Equal(expected, tree.InsertBlocksBefore(block, markdown).GetRoot().ToFullString());
    }

    [Theory]
    [InlineData("a", "\nb\n", "a\n\nb")]
    [InlineData("```\ncode\n```\nc\n", "# t", "```\ncode\n```\n# t\nc\n")]
    [InlineData("> - a\n>   b\n", "\n  c", ">   b\n>\n>     c\n")]
    public void InsertsBlocksAfter(string text, string markdown, string expectedEnd)
    {
        var tree = MarkdownSyntaxTree.ParseText(text);
        var block = tree.GetRoot().DescendantNodes().OfType<MarkdownBlockSyntax>().First(node => node is MarkdownParagraphSyntax or MarkdownCodeBlockSyntax);

        Assert.EndsWith(expectedEnd, tree.InsertBlocksAfter(block, markdown).GetRoot().ToFullString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a\n\nb\n\nc\n", 1, "a\n\n\nc\n")]
    [InlineData("> a\n>\n> b\n", 0, ">\n> b\n")]
    [InlineData("- a\n- b\n", 0, "- b\n")]
    [InlineData("1. a\n", 1, "1. \n")]
    [InlineData("a\r\n\r\nb", 1, "a\r\n\r\n")]
    public void RemovesBlocks(string text, int blockIndex, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text);
        var block = tree.GetRoot().DescendantNodes().OfType<MarkdownBlockSyntax>().Where(node => node is not (MarkdownBlockQuoteSyntax or MarkdownListSyntax)).ToList()[blockIndex];

        Assert.Equal(expected, tree.RemoveBlock(block).GetRoot().ToFullString());
    }

    [Fact]
    public void EditingABlockOfAnotherTreeThrows()
    {
        var tree = MarkdownSyntaxTree.ParseText("a\n");
        var other = MarkdownSyntaxTree.ParseText("b\n").GetRoot().Blocks[0];

        Assert.Throws<ArgumentException>("block", () => tree.RemoveBlock(other));
    }

    private sealed class CountingWalker : MarkdownSyntaxWalker
    {
        public int Count { get; private set; }

        public override void DefaultVisit(MarkdownSyntaxNode node)
        {
            Count++;
            base.DefaultVisit(node);
        }
    }

    private sealed class ReplaceText : MarkdownSyntaxRewriter
    {
        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            if (token.RawKind == (int)SyntaxKind.TextToken && token.Text == "old")
            {
                return MarkdownSyntaxTree.ParseText("new").GetRoot().DescendantTokens().First().WithTriviaFrom(token);
            }

            return base.VisitToken(token);
        }
    }
}
