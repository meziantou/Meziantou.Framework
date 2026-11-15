using Meziantou.Framework.Language.Markdown.Parsing;
using Meziantou.Xunit;

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
    [InlineData("-        { code }\n\n    b\n", "<ul>\n<li>\n<pre><code>   { code }\n</code></pre>\n<p>b</p>\n</li>\n</ul>\n")]
    [InlineData("- ```\n  a\n  ```\n\n  b\n", "<ul>\n<li>\n<pre><code>a\n</code></pre>\n<p>b</p>\n</li>\n</ul>\n")]
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

    [Theory]
    [InlineData("a[t][^1] b[^1][] c[^ 1] d![^1]\n\n[^1]: x\n", "<p>a<a id=\"fnref:1\" href=\"#fn:1\" class=\"footnote-ref\"><sup>1</sup></a> b<a id=\"fnref:2\" href=\"#fn:1\" class=\"footnote-ref\"><sup>1</sup></a> c[^ 1] d<a id=\"fnref:3\" href=\"#fn:1\" class=\"footnote-ref\"><sup>1</sup></a></p>\n")]
    [InlineData("a[^1]\n\n[^1]: x\n[^1]: y\n", "<p>x<a href=\"#fnref:1\" class=\"footnote-back-ref\">&#8617;</a></p>\n</li>\n</ol>\n</div>\n")]
    [InlineData("a[^b]\n\n[ ^a]: unused\n\n[^b]: used\n", "<li id=\"fn:1\">\n<p>used<a href=\"#fnref:1\" class=\"footnote-back-ref\">&#8617;</a></p>\n</li>\n</ol>")]
    [InlineData("[^a]: unused\n", "<div class=\"footnotes\">\n<hr />\n<ol>\n</ol>\n</div>\n")]
    [InlineData("> [^1]: x\n\na[^1]\n", "<p>a<a href=\"x\">^1</a></p>\n")]
    [InlineData("[foo]\n\n[^1]: [foo]: /url\n", "<p><a href=\"/url\">foo</a></p>\n")]
    [InlineData("a[^1]\n\n[^1]:\n    > q\n", "<li id=\"fn:1\">\n<blockquote>\n<p>q</p>\n</blockquote>\n<p><a href=\"#fnref:1\" class=\"footnote-back-ref\">&#8617;</a></p>\n</li>")]
    [InlineData("a[^1]\n\n[^1]: ```\n    code\n\nnot code\n    ```\n", "<pre><code>code\n\nnot code\n</code></pre>")]
    [InlineData("a[^1]\n\n[^1]:     x\n\n        y\n", "<pre><code>y\n</code></pre>")]
    public void RendersFootnotes(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.Footnotes });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Contains(expected, MarkdownHtmlRenderer.ToHtml(tree), StringComparison.Ordinal);
    }

    [Fact]
    public void FootnoteReferenceHasItsDefinition()
    {
        var root = MarkdownSyntaxTree.ParseText("See[^Note].\n\n[^note]: The *note*.\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.Footnotes }).GetRoot();
        var reference = root.DescendantNodes().OfType<MarkdownFootnoteReferenceSyntax>().Single();

        Assert.Equal("^Note", reference.Label);
        Assert.Same(root.Blocks[1], reference.Definition);
        Assert.Equal("^note", reference.Definition!.Label);
        Assert.Equal("The note.", reference.Definition.GetPlainText());
    }

    // The cases of 1.x: after an inline that is not text, attributes are for it; after text, for the block
    [Theory]
    [InlineData("# a {#x} b\n", "<h1 id=\"x\">a</h1>\n")]
    [InlineData("a `c`{.k} *e*{.m} <b>h</b>{.n} ![i](u){.o} t{.p}\n", "<p class=\"p\">a <code class=\"k\">c</code> <em class=\"m\">e</em> <b>h</b> <img src=\"u\" class=\"o\" alt=\"i\" /> t</p>\n")]
    [InlineData("- item {.x}\n- b\n", "<ul>\n<li>item</li>\n<li>b</li>\n</ul>\n")]
    [InlineData("```cs {.x #y}\ncode\n```\n", "<pre><code id=\"y\" class=\"x language-cs\">code\n</code></pre>\n")]
    [InlineData("{.z}\n```cs\ncode\n```\n", "<pre><code class=\"language-cs z\">code\n</code></pre>\n")]
    [InlineData("    code\n{.z}\n", "<pre><code>code\n</code></pre>\n<p class=\"z\"></p>\n")]
    [InlineData("{.a}\n\n# h\n", "<h1 class=\"a\">h</h1>\n")]
    [InlineData("a\n{.b}\nc\n", "<p>a\nc</p>\n")]
    [InlineData("[a{.x}](u)\n", "<p class=\"x\"><a href=\"u\">a</a></p>\n")]
    [InlineData("*{.c}\nA\n", "<p>*A</p>\n")]
    [InlineData("[l](u) {title=t}\n", "<p title=\"t\"><a href=\"u\">l</a></p>\n")]
    [InlineData("[l](u \"md\"){#i title=t onclick=x data-x=1 hidden}\n", "<p><a href=\"u\" id=\"i\" title=\"t\" hidden=\"\" title=\"md\">l</a></p>\n")]
    [InlineData("a{#x .b lang=de}{#y .c lang=en lang=fr}\n", "<p id=\"y\" class=\"b c\" lang=\"de\">a</p>\n")]
    [InlineData("a{#y .c lang=en lang=fr}\n", "<p id=\"y\" class=\"c\" lang=\"en\" lang=\"fr\">a</p>\n")]
    [InlineData("$$ {.e}\nx\n$$\n", "<div class=\"math e\">")]
    [InlineData("a\n{#t}\n", "<p>a</p>\n")]
    [InlineData("Title\n{#t}\n---\n", "<h2 id=\"t\">Title\n</h2>\n")]
    [InlineData("a {#t}{#u}\n", "<p id=\"u\">a</p>\n")]
    [InlineData("*b* {#a}{#b}\n", "<p id=\"b\"><em>b</em></p>\n")]
    [InlineData("*{#a}b* **{#c}d**\n", "<p><em>b</em> <strong>d</strong></p>\n")]
    [InlineData("{#p .p}\n# H {#own .c}\n", "<h1 id=\"p\" class=\"c p\">H</h1>\n")]
    [InlineData("{#p}\n```{#own}\nx\n```\n", "<pre><code id=\"p\">x\n</code></pre>\n")]
    public void RendersGenericAttributes(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.GenericAttributes | MarkdownExtensions.Mathematics });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Contains(expected, MarkdownHtmlRenderer.ToHtml(tree), StringComparison.Ordinal);
    }

    [Fact]
    public void GenericAttributesHaveTheirTarget()
    {
        var root = MarkdownSyntaxTree.ParseText("[l](u){#i .c title=\"a b\" hidden}\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.GenericAttributes }).GetRoot();
        var attributes = root.DescendantNodes().OfType<MarkdownGenericAttributesSyntax>().Single();

        Assert.Equal("i", attributes.Id);
        Assert.Equal(["c"], attributes.Classes.ToArray());
        Assert.Equal([new KeyValuePair<string, string?>("title", "a b"), new KeyValuePair<string, string?>("hidden", null)], attributes.Properties.ToArray());
        Assert.IsType<MarkdownLinkSyntax>(attributes.Target);
    }

    [Fact]
    public void AttributeFilterChoosesTheAttributesToWrite()
    {
        var tree = MarkdownSyntaxTree.ParseText("a{#i data-x=1 onclick=y}\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.GenericAttributes });

        var html = MarkdownHtmlRenderer.ToHtml(tree, new MarkdownHtmlRendererOptions { AttributeFilter = name => name.StartsWith("data-", StringComparison.Ordinal) });

        Assert.Equal("<p id=\"i\" data-x=\"1\">a</p>\n", html);
    }

    [Fact]
    public void ReadmeExample_AutoIdentifiers()
    {
        #region MarkdownSyntaxTree_AutoIdentifiers
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.AutoIdentifiers };
        var tree = MarkdownSyntaxTree.ParseText("# Getting started\n\nSee [Getting started].\n\n# Getting started\n", options);

        // The second heading with the same text gets a suffix, and the link without definition links to the last heading
        var headings = tree.GetRoot().Blocks.OfType<MarkdownHeadingSyntax>().ToList();
        Assert.Equal("getting-started", headings[0].Identifier);
        Assert.Equal("getting-started-1", headings[1].Identifier);
        Assert.Equal("<p>See <a href=\"#getting-started-1\">Getting started</a>.</p>", MarkdownHtmlRenderer.ToHtml(tree).Split('\n')[1]);
        #endregion
    }

    [Theory]
    [InlineData("# a *b* `c` [d](u) ![e](i) <x>f</x> &amp; g\\_\n", "a-b-c-d-e-f-g")]
    [InlineData("# 1.0 & ^ % *\n# 1.0 & ^ % *\n", "section,section-1")]
    [InlineData("# a\n# a-1\n# a\n# a\n", "a,a-1,a-2,a-3")]
    [InlineData("# a {#x}\n# a\n", "x,a")]
    [InlineData("{#p}\n# a {#x}\n", "p")]
    [InlineData("Title\n{#t}\n---\n", "t")]
    [InlineData("# $x$ y[^1] *[A]\n\n[^1]: # z\n\n# w\n", "y-a,z,w")]
    [InlineData("> # Quoted\n- # Listed\n", "quoted,listed")]
    public void HeadingsHaveIdentifiers(string text, string expected)
    {
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.AutoIdentifiers | MarkdownExtensions.GenericAttributes | MarkdownExtensions.Mathematics | MarkdownExtensions.Footnotes | MarkdownExtensions.Abbreviations };
        var root = MarkdownSyntaxTree.ParseText(text, options).GetRoot();

        Assert.Equal(expected, string.Join(',', root.DescendantNodes().OfType<MarkdownHeadingSyntax>().Select(heading => heading.Identifier)));
    }

    [Theory]
    [InlineData(MarkdownAutoIdentifierOptions.Default, "# Ærø Café\n[Ærø Café]\n", "<h1 id=\"aeroe-cafe\">Ærø Café</h1>\n<p><a href=\"#aeroe-cafe\">Ærø Café</a></p>\n")]
    [InlineData(MarkdownAutoIdentifierOptions.None, "# Ærø Café\n[Ærø Café]\n", "<h1 id=\"ærø-café\">Ærø Café</h1>\n<p>[Ærø Café]</p>\n")]
    [InlineData(MarkdownAutoIdentifierOptions.GitHub | MarkdownAutoIdentifierOptions.AutoLink, "# 3. Ærø\n[3. Ærø]\n", "<h1 id=\"3-ærø\">3. Ærø</h1>\n<p><a href=\"#3-%C3%A6r%C3%B8\">3. Ærø</a></p>\n")]
    public void AutoIdentifierOptionsChooseTheIdentifiers(MarkdownAutoIdentifierOptions autoIdentifierOptions, string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.AutoIdentifiers, AutoIdentifierOptions = autoIdentifierOptions });

        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Theory]
    [InlineData("[Heading]\n# Heading\n", "<p><a href=\"#heading\">Heading</a></p>")]
    [InlineData("[text][Heading]\n# Heading\n", "<p><a href=\"#heading\">text</a></p>")]
    [InlineData("![alt][Heading]\n# Heading\n", "<p><img src=\"#heading\" alt=\"alt\" /></p>")]
    [InlineData("[Heading]\n# Heading\n[heading]: /url\n", "<p><a href=\"/url\">Heading</a></p>")]
    [InlineData("[outer [Heading]](/u)\n# Heading\n", "<p><a href=\"/u\">outer [Heading]</a></p>")]
    [InlineData("[]\n#\n", "<p>[]</p>")]
    [InlineData("[x y]\n\nx {.c} y\n===\n", "<p><a href=\"#x-y\">x y</a></p>")]
    [InlineData("[x]\n# x {#i}\n", "<p><a href=\"#i\">x</a></p>")]
    [InlineData("[Line one]\n\nLine one\nline two\n===\n", "<p><a href=\"#line-one-line-two\">Line one</a></p>")]
    public void ReferenceLinksLinkToHeadings(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.AutoIdentifiers | MarkdownExtensions.GenericAttributes });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.StartsWith(expected, MarkdownHtmlRenderer.ToHtml(tree), StringComparison.Ordinal);
    }

    [Fact]
    public void LinkToAHeadingHasTheHeading()
    {
        var root = MarkdownSyntaxTree.ParseText("[Intro]\n\n## Intro\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.AutoIdentifiers }).GetRoot();
        var link = root.DescendantNodes().OfType<MarkdownLinkSyntax>().Single();

        Assert.Null(link.Definition);
        Assert.Same(root.Blocks[1], link.Heading);
        Assert.Equal("#intro", link.Destination);
        Assert.Null(link.Title);
    }

    [Fact]
    public void HeadingHasNoIdentifierWithoutTheExtension()
    {
        var heading = Assert.IsType<MarkdownHeadingSyntax>(MarkdownSyntaxTree.ParseText("# a\n").GetRoot().Blocks.Single());

        Assert.Null(heading.Identifier);
    }

    [Theory]
    [InlineData("Header identifiers in HTML", "header-identifiers-in-html")]
    [InlineData("* Dogs*?--in *my* house?", "dogs-in-my-house")]
    [InlineData("[HTML], [S5], or [RTF]?", "html-s5-or-rtf")]
    [InlineData("3. Applications", "applications")]
    [InlineData("33", "")]
    [InlineData("zeilenumbrüche", "zeilenumbrüche")]
    [InlineData("Straße", "straße")]
    [InlineData("Íslandsbanki", "íslandsbanki")]
    [InlineData("一二三四五", "一二三四五")]
    [InlineData("一,-b", "一-b")]
    [InlineData("-æ5el", "æ5el")]
    [InlineData("-frø-", "frø")]
    [InlineData("-fr-ø", "fr-ø")]
    [InlineData("b#r", "br")]
    [InlineData("b%r", "br")]
    [InlineData("b{r", "br")]
    [InlineData(@"b\r", "br")]
    [InlineData("Requirement 😀", "requirement")]
    public void IdentifierKeepsLetters(string text, string expected)
    {
        Assert.Equal(expected, HeadingIdentifier.Create(text, MarkdownAutoIdentifierOptions.None));
    }

    [Theory]
    [InlineData("a c", "a-c")]
    [InlineData("a,c", "ac")]
    [InlineData("a--", "a")]
    [InlineData("a..d", "a.d")]
    [InlineData("a  d", "a-d")]
    [InlineData("-bc", "bc")]
    [InlineData("a-.-", "a")]
    [InlineData("bΘr", "br")]
    [InlineData("四五", "")]
    [InlineData("bár", "bar")]
    [InlineData("àrrivé", "arrive")]
    [InlineData("Straße", "strasse")]
    [InlineData("Ærø", "aeroe")]
    [InlineData("Þór Ðað", "thor-dad")]
    [InlineData("äöüÄÖÜåÅ", "aouaouaa")]
    [InlineData("1,-b", "b")]
    [InlineData("b1,-", "b1")]
    [InlineData("пристаням_стремятся_", "")]
    public void IdentifierOnlyHasAscii(string text, string expected)
    {
        Assert.Equal(expected, HeadingIdentifier.Create(text, MarkdownAutoIdentifierOptions.AllowOnlyAscii));
    }

    [Theory]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    [InlineData("schön", "schon")]
    [InlineData("grüßen", "grussen")]
    [InlineData("Íslandsbanki", "islandsbanki")]
    public void IdentifierOnlyHasAsciiWithoutTheAccentsOfDecomposedLetters(string text, string expected)
    {
        Assert.Equal(expected, HeadingIdentifier.Create(text, MarkdownAutoIdentifierOptions.AllowOnlyAscii));
    }

    [Theory]
    [InlineData("* Dogs*?--in *my* house?", "-dogs--in-my-house")]
    [InlineData("3. Applications", "3-applications")]
    [InlineData("33", "33")]
    public void IdentifierIsMadeAsGitHubMakesIt(string text, string expected)
    {
        Assert.Equal(expected, HeadingIdentifier.Create(text, MarkdownAutoIdentifierOptions.GitHub));
    }

    [Fact]
    public void IdentifierIgnoresInvalidCodePoints()
    {
        // Built in code: the strings of InlineData go through UTF-8, which replaces a lone surrogate
        Assert.Equal("ab-c", HeadingIdentifier.Create("a\uFFFEb c", MarkdownAutoIdentifierOptions.AllowOnlyAscii));
        Assert.Equal("ab-c", HeadingIdentifier.Create("a\uD800b c\uFDD0", MarkdownAutoIdentifierOptions.AllowOnlyAscii));
    }

    [Fact]
    public void ReadmeExample_CustomContainers()
    {
        #region MarkdownSyntaxTree_CustomContainers
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.CustomContainers };
        var tree = MarkdownSyntaxTree.ParseText(":::warning Read this\nThis is ::important::.\n:::\n", options);

        var container = Assert.IsType<MarkdownCustomContainerSyntax>(tree.GetRoot().Blocks.Single());
        Assert.Equal("warning", container.Name);
        Assert.Equal("Read this", container.Arguments);
        Assert.Equal("<div class=\"warning\"><p>This is <span>important</span>.</p>\n</div>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Theory]
    [InlineData("::::outer\n:::inner\nx\n:::\n::::\n", "<div class=\"outer\">\n<div class=\"inner\"><p>x</p>\n</div>\n</div>\n")]
    [InlineData(":::a\n:::b\nx\n:::\n:::\n", "<div class=\"a\">\n<div class=\"b\"><p>x</p>\n</div>\n</div>\n<div></div>\n")]
    [InlineData(":::note\n```\n:::\n```\n", "<div class=\"note\">\n<pre><code></code></pre>\n</div>\n<pre><code></code></pre>\n")]
    [InlineData(":::x\n# H\n---\n:::\n", "<div class=\"x\"><h1>H</h1>\n<hr />\n</div>\n")]
    [InlineData(":::x\n- a\n:::\n", "<div class=\"x\">\n<ul>\n<li>a</li>\n</ul>\n</div>\n")]
    [InlineData("   :::x\n   y\n   :::\n", "<div class=\"x\"><p>y</p>\n</div>\n")]
    [InlineData("    :::x\n", "<pre><code>:::x\n</code></pre>\n")]
    [InlineData("para\n:::x\ny\n", "<p>para</p>\n<div class=\"x\"><p>y</p>\n</div>\n")]
    [InlineData(":::a&amp;b\\* args\n", "<div class=\"a&amp;b*\"></div>\n")]
    [InlineData("::\nx\n", "<p>::\nx</p>\n")]
    [InlineData("- :::x\n  a\n\n  b\n  :::\n- c\n", "<li>\n<div class=\"x\"><p>a</p>\n<p>b</p>\n</div>\n</li>\n<li>\n<p>c</p>\n</li>")]
    [InlineData("- :::x\n  a\n\n  b\n  :::\n", "<li>\n<div class=\"x\">ab</div>\n</li>")]
    [InlineData("- :::x\n  - a\n\n    b\n  :::\n- c\n", "<li>c</li>")]
    [InlineData("std::vector::push_back ::*a*::\n", "<p>std<span>vector</span>push_back <span><em>a</em></span></p>\n")]
    [InlineData("::a::{.k} :::b:::\n", "<p><span class=\"k\">a</span> :<span>b</span>:</p>\n")]
    public void RendersCustomContainers(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.CustomContainers | MarkdownExtensions.GenericAttributes });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Contains(expected, MarkdownHtmlRenderer.ToHtml(tree), StringComparison.Ordinal);
    }

    [Fact]
    public void CustomContainerHasItsFences()
    {
        var root = MarkdownSyntaxTree.ParseText("> ::::tip {.x}\n> # a\n> ::::\n\n:::open\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.CustomContainers | MarkdownExtensions.GenericAttributes }).GetRoot();
        var containers = root.DescendantNodes().OfType<MarkdownCustomContainerSyntax>().ToList();

        Assert.Equal("::::", containers[0].OpeningFenceToken.Text);
        Assert.Equal("::::", containers[0].ClosingFenceToken.Text);
        Assert.Equal("tip", containers[0].Name);
        Assert.Equal("", containers[0].Arguments);
        Assert.IsType<MarkdownHeadingSyntax>(containers[0].Blocks.Single());
        Assert.Equal(0, containers[1].ClosingFenceToken.RawKind);
        Assert.Empty(containers[1].Blocks);
    }

    [Fact]
    public void ReadmeExample_DefinitionLists()
    {
        #region MarkdownSyntaxTree_DefinitionLists
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.DefinitionLists };
        var tree = MarkdownSyntaxTree.ParseText("Apple\nPear\n:   A fruit.\n:   A tree.\n", options);

        // The lines of the paragraph before the marker are the terms, and a marker after a definition starts another one
        var list = Assert.IsType<MarkdownDefinitionListSyntax>(tree.GetRoot().Blocks.Single());
        Assert.Equal(["Apple", "Pear"], list.Items[0].Terms.Select(term => term.GetPlainText()).ToArray());
        Assert.Empty(list.Items[1].Terms);
        Assert.Equal("<dl>\n<dt>Apple</dt>\n<dt>Pear</dt>\n<dd>A fruit.</dd>\n<dd>A tree.</dd>\n</dl>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Theory]
    [InlineData("T\n:   a\n\n:   b\n", "<dd><p>a</p>\n</dd>\n<dd>b</dd>")]
    [InlineData("T\n:   a\n\n", "<dd><p>a</p>\n</dd>")]
    [InlineData("T\n:   a\n\nnext\n", "<dd>a</dd>\n</dl>\n<p>next</p>\n")]
    [InlineData("p\n\nT\n:   a\n    > q\n", "<dd>\n<p>a</p>\n<blockquote>")]
    [InlineData("T\n:   a\n    > q\n", "<dd><p>a</p>\n<blockquote>")]
    [InlineData("T\n:   a\n# h\n", "<dd><p>a</p>\n<h1>h</h1>\n</dd>")]
    [InlineData("T\n:   * a\n# h\n", "</ul>\n</dd>\n</dl>\n<h1>h</h1>")]
    [InlineData("T\n:   > q\nlazy\n", "<p>q\nlazy</p>")]
    [InlineData("T\n:  x\n", "<p>T\n:  x</p>")]
    [InlineData("T\n   :x\n", "<dt>T</dt>\n<dd>x</dd>")]
    [InlineData("T\n\n    : code\n", "<pre><code>: code\n</code></pre>")]
    [InlineData("Term\n\n\n~   def\n", "<dt>Term</dt>\n<dd>def</dd>")]
    [InlineData("T \n:   d\n", "<dt>T </dt>")]
    [InlineData("[a]: /u\nT\n:   d\n\n[a]\n", "<dt>[a]: /u</dt>\n<dt>T</dt>\n<dd>d</dd>\n</dl>\n<p>[a]</p>")]
    [InlineData("A\n:   1\n\nB\n:   2\n", "<dd>1</dd>\n<dt>B</dt>\n<dd>2</dd>\n</dl>")]
    [InlineData("- T\n  :   a\n- b\n", "<li>\n<dl>\n<dt>T</dt>\n<dd>a</dd>\n</dl>\n</li>")]
    [InlineData("T {.t}\n:   d\n", "<dt class=\"t\">T</dt>")]
    [InlineData("[foo]: /url\n===\nT\n:   d\n", "<p>===\nT\n:   d</p>")]
    [InlineData("[a]: /u\nx\n\n:   b\n", "<p>x</p>\n<p>:   b</p>")]
    public void RendersDefinitionLists(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.DefinitionLists | MarkdownExtensions.GenericAttributes });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Contains(expected, MarkdownHtmlRenderer.ToHtml(tree), StringComparison.Ordinal);
    }

    [Fact]
    public void DefinitionHasItsMarkerAndBlocks()
    {
        var item = MarkdownSyntaxTree.ParseText("T\n~   a\n\n    b\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.DefinitionLists }).GetRoot().DescendantNodes().OfType<MarkdownDefinitionItemSyntax>().Single();

        Assert.Equal("~", item.MarkerToken.Text);
        Assert.Equal("T", item.Terms.Single().GetPlainText());
        Assert.Equal(["a", "b"], item.Blocks.Select(block => block.GetPlainText()).ToArray());
    }

    [Fact]
    public void ReadmeExample_ListExtras()
    {
        #region MarkdownSyntaxTree_ListExtras
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.ListExtras };
        var tree = MarkdownSyntaxTree.ParseText("iv. Four\nv. Five\n", options);

        var list = Assert.IsType<MarkdownListSyntax>(tree.GetRoot().Blocks.Single());
        Assert.Equal(MarkdownOrderedListStyle.LowerRoman, list.OrderedStyle);
        Assert.Equal(4, list.StartNumber);
        Assert.Equal("<ol type=\"i\" start=\"4\">\n<li>Four</li>\n<li>Five</li>\n</ol>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Theory]
    [InlineData("text\nb. item\n", "<p>text</p>\n<ol type=\"a\" start=\"2\">")]
    [InlineData("a. x\ni. y\n", "<ol type=\"a\">\n<li>x</li>\n<li>y</li>\n</ol>")]
    [InlineData("a. x\ni) y\n", "<ol type=\"a\" start=\"9\">\n<li>y</li>")]
    [InlineData("- a\n i. b\n", "<ol type=\"a\" start=\"9\">")]
    [InlineData("a.\nvi. six\n", "<ol type=\"i\" start=\"6\">")]
    [InlineData("X) ten\n", "<ol type=\"I\" start=\"10\">")]
    [InlineData("ab. no\n", "<p>ab. no</p>")]
    [InlineData("text\nc.\n", "<p>text\nc.</p>")]
    public void RendersListExtras(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.ListExtras });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Contains(expected, MarkdownHtmlRenderer.ToHtml(tree), StringComparison.Ordinal);
    }

    [Fact]
    public void BulletListHasNoOrderedStyle()
    {
        var list = Assert.IsType<MarkdownListSyntax>(MarkdownSyntaxTree.ParseText("- a\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.ListExtras }).GetRoot().Blocks.Single());

        Assert.Null(list.OrderedStyle);
        Assert.Null(list.StartNumber);
        Assert.Equal(MarkdownOrderedListStyle.Numbers, Assert.IsType<MarkdownListSyntax>(MarkdownSyntaxTree.ParseText("3. a\n").GetRoot().Blocks.Single()).OrderedStyle);
    }

    [Fact]
    public void ReadmeExample_SmartyPants()
    {
        #region MarkdownSyntaxTree_SmartyPants
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.SmartyPants };
        var tree = MarkdownSyntaxTree.ParseText("\"Wait...\" -- it is 'done'\n", options);

        Assert.Equal("<p>&ldquo;Wait&hellip;&rdquo; &ndash; it is &lsquo;done&rsquo;</p>\n", MarkdownHtmlRenderer.ToHtml(tree));

        // The renderer can write other text, such as the characters themselves
        var french = new MarkdownHtmlRendererOptions
        {
            SmartyPants = new Dictionary<MarkdownSmartyPant, string>
            {
                [MarkdownSmartyPant.LeftDoubleQuote] = "\u00AB\u00A0",
                [MarkdownSmartyPant.RightDoubleQuote] = "\u00A0\u00BB",
            },
        };
        Assert.Equal("<p>\u00AB\u00A0Wait&hellip;\u00A0\u00BB &ndash; it is &lsquo;done&rsquo;</p>\n", MarkdownHtmlRenderer.ToHtml(tree, french));
        #endregion
    }

    [Theory]
    [InlineData("<<a>> x<<b>>\n", "<p>&laquo;a&raquo; x&lt;<b>&gt;</p>\n")]
    [InlineData("''a'' 'b \"c' d\"\n", "<p>&ldquo;a&rdquo; &lsquo;b &quot;c&rsquo; d&quot;</p>\n")]
    [InlineData("a...b ...c d...\n", "<p>a...b ...c d&hellip;</p>\n")]
    [InlineData("a--b---c----d \\--- `--`\n", "<p>a&ndash;b&mdash;c&mdash;-d -&ndash; <code>--</code></p>\n")]
    [InlineData("*'a'* ![\"i\" <b>](u)\n", "<p><em>&lsquo;a&rsquo;</em> <img src=\"u\" alt=\"&ldquo;i&rdquo; &lt;b&gt;\" /></p>\n")]
    [InlineData("l'ami 'x\n", "<p>l'ami 'x</p>\n")]
    public void RendersSmartyPants(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.SmartyPants });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Fact]
    public void SmartyPantTokensHaveTheirCharacter()
    {
        var root = MarkdownSyntaxTree.ParseText("# \"a\" -- b\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.SmartyPants | MarkdownExtensions.AutoIdentifiers }).GetRoot();
        var tokens = root.DescendantTokens().Where(token => token.RawKind == (int)SyntaxKind.SmartyPantToken).ToList();

        Assert.Equal([MarkdownSmartyPant.LeftDoubleQuote, MarkdownSmartyPant.RightDoubleQuote, MarkdownSmartyPant.EnDash], tokens.Select(token => (MarkdownSmartyPant)(int)token.Value!).ToArray());
        Assert.Equal("\u201Ca\u201D \u2013 b", root.GetPlainText());

        // Markdig leaves the characters of SmartyPants out of the identifier
        Assert.Equal("a-b", Assert.IsType<MarkdownHeadingSyntax>(root.Blocks.Single()).Identifier);
    }

    [Fact]
    public void ReadmeExample_Emoji()
    {
        #region MarkdownSyntaxTree_Emoji
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Emoji };
        Assert.Equal("<p>Ship it \U0001F680 :)</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("Ship it :rocket: :)\n", options with { EmojiMapping = MarkdownEmojiMapping.DefaultWithoutSmileys })));

        // A mapping of its own: shortcodes, and smileys for them
        var mapping = new MarkdownEmojiMapping(new Dictionary<string, string> { [":ok:"] = "\u2705" }, new Dictionary<string, string> { ["(y)"] = ":ok:" });
        Assert.Equal("<p>\u2705 \u2705 :rocket:</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(":ok: (y) :rocket:\n", options with { EmojiMapping = mapping })));
        #endregion
    }

    [Theory]
    [InlineData("a :) and :angry:\n", "<p>a \U0001F603 and \U0001F620</p>\n")]
    [InlineData("not:) x:angry:x\n", "<p>not:) x:angry:x</p>\n")]
    [InlineData("]:) []:) [a]:)\n", "<p>\U0001F608 [\U0001F608 [a]\U0001F603</p>\n")]
    [InlineData("<3 -> <=> `:)` \\:)\n", "<p>\u2764\uFE0F \u2192 \u21D4 <code>:)</code> :)</p>\n")]
    [InlineData("a :* b *c :* d*\n", "<p>a \U0001F617 b <em>c :</em> d*</p>\n")]
    [InlineData("*:)*\n", "<p><em>\U0001F603</em></p>\n")]
    public void RendersEmoji(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.Emoji });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Fact]
    public void EmojiIsAToken()
    {
        var paragraph = MarkdownSyntaxTree.ParseText("a :smile:\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.Emoji }).GetRoot().Blocks.Single();

        Assert.Equal(":smile:", paragraph.ChildTokens().Single(token => token.RawKind == (int)SyntaxKind.EmojiToken).Text);
        Assert.Equal("a \U0001F604", paragraph.GetPlainText());
        Assert.True(MarkdownEmojiMapping.Default.TryGetEmoji(":)", out var emoji));
        Assert.Equal("\U0001F603", emoji);
        Assert.Throws<ArgumentException>(() => new MarkdownEmojiMapping(new Dictionary<string, string>(), new Dictionary<string, string> { [":)"] = ":missing:" }));
    }

    [Fact]
    public void ReadmeExample_FiguresFootersCitations()
    {
        #region MarkdownSyntaxTree_FiguresFootersCitations
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Figures | MarkdownExtensions.Footers | MarkdownExtensions.Citations };
        var tree = MarkdownSyntaxTree.ParseText("^^^\n![Logo](logo.png)\n^^^ The *logo*\n\n^^ From \"\"The Book\"\"\n", options);

        Assert.Equal("<figure>\n<p><img src=\"logo.png\" alt=\"Logo\" /></p>\n<figcaption>The <em>logo</em></figcaption>\n</figure>\n<footer>From <cite>The Book</cite></footer>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Theory]
    [InlineData("^^^ top\nx\n^^^ bottom\n", "<figure>\n<figcaption>top</figcaption>\n<p>x</p>\n<figcaption>bottom</figcaption>\n</figure>\n")]
    [InlineData("^^^^\n^^^\nx\n^^^^more\n", "<figure>\n<figure>\n<p>x</p>\n</figure>\n<figcaption>more</figcaption>\n</figure>\n")]
    [InlineData("^^^\n```\n^^^\n```\n", "<figure>\n<pre><code></code></pre>\n</figure>\n<pre><code></code></pre>\n")]
    [InlineData("^^ a\n^^ b\nlazy\n\nc\n", "<footer>a\nb\nlazy</footer>\n<p>c</p>\n")]
    [InlineData("^^ f\n^^ # h\n", "<footer>f<h1>h</h1>\n</footer>\n")]
    [InlineData("^^ > q\n", "<footer>\n<blockquote>\n<p>q</p>\n</blockquote>\n</footer>\n")]
    [InlineData("a \"\"b\"\" c\"\"d\"\"e\n", "<p>a <cite>b</cite> c&quot;&quot;d&quot;&quot;e</p>\n")]
    public void RendersFiguresFootersAndCitations(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.Figures | MarkdownExtensions.Footers | MarkdownExtensions.Citations });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Fact]
    public void FigureHasItsFencesAndCaptions()
    {
        var figure = Assert.IsType<MarkdownFigureSyntax>(MarkdownSyntaxTree.ParseText("^^^ A\nx\n^^^^ B\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.Figures }).GetRoot().Blocks.Single());

        Assert.Equal("^^^", figure.OpeningFenceToken.Text);
        Assert.Equal("^^^^", figure.ClosingFenceToken.Text);
        Assert.Equal(["A", "B"], figure.Blocks.OfType<MarkdownFigureCaptionSyntax>().Select(caption => caption.GetPlainText()).ToArray());
        Assert.Equal("^^", Assert.IsType<MarkdownFooterSyntax>(MarkdownSyntaxTree.ParseText("^^ f\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.Footers }).GetRoot().Blocks.Single()).MarkerToken.Text);
    }

    [Fact]
    public void ParsesPipeTables()
    {
        #region MarkdownSyntaxTree_PipeTables
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.PipeTables };
        var tree = MarkdownSyntaxTree.ParseText("Fruits:\n| Name | Price |\n|:-----|------:|\n| Apple | 1 | each\n", options);
        var table = tree.GetRoot().Blocks.OfType<MarkdownTableSyntax>().Single();

        Assert.True(table.IsPipeTable);
        Assert.Equal([MarkdownTableAlignment.Left, MarkdownTableAlignment.Right], table.Alignments);
        Assert.Equal(["Apple", "1", "each"], table.Rows[0].Cells.Select(cell => cell.GetPlainText()));
        Assert.Equal("<p>Fruits:</p>\n<table>\n<thead>\n<tr>\n<th align=\"left\">Name</th>\n<th align=\"right\">Price</th>\n<th align=\"right\"></th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td align=\"left\">Apple</td>\n<td align=\"right\">1</td>\n<td align=\"right\">each</td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Theory]
    [InlineData("a | b\n-- | -:\n0 | 1 | 2\n", "<table>\n<thead>\n<tr>\n<th>a</th>\n<th align=\"right\">b</th>\n<th align=\"right\"></th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>0</td>\n<td align=\"right\">1</td>\n<td align=\"right\">2</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("Text\n| A |\n|---|\n| B |\nafter\n", "<p>Text</p>\n<table>\n<thead>\n<tr>\n<th>A</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>B</td>\n</tr>\n</tbody>\n</table>\n<p>after</p>\n")]
    [InlineData("a | b\n- | -\n`c|d` | [e|f](/u)\n", "<table>\n<thead>\n<tr>\n<th>a</th>\n<th>b</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td><code>c|d</code></td>\n<td><a href=\"/u\">e|f</a></td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("| A | B | C |\n||:---|---:|\n| 1 | 2 | 3 |\n", "<table>\n<thead>\n<tr>\n<th>A</th>\n<th align=\"left\">B</th>\n<th align=\"right\">C</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>1</td>\n<td align=\"left\">2</td>\n<td align=\"right\">3</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("a | b\n-|-\n0 | `|`\n", "<table>\n<thead>\n<tr>\n<th>a</th>\n<th>b</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>0</td>\n<td><code>|</code></td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("a |  `\n | ` \n", "<p>a |  `\n| `</p>\n")]
    [InlineData("| a\n| --\n", "<p>| a\n| --</p>\n")]
    [InlineData("a|b\n|\\-|-\n", "<table>\n<thead>\n<tr>\n<th>a</th>\n<th>b</th>\n</tr>\n</thead>\n</table>\n")]
    [InlineData("|||\n|||\n", "<p>|||\n|||</p>\n")]
    [InlineData("> a | b\n-|-\n", "<blockquote>\n<table>\n<thead>\n<tr>\n<th>a</th>\n<th>b</th>\n</tr>\n</thead>\n</table>\n</blockquote>\n")]
    [InlineData("- a | b\n- | -\n", "<ul>\n<li>a | b</li>\n<li>| -</li>\n</ul>\n")]
    [InlineData("a | b\n\\- | -\n", "<table>\n<thead>\n<tr>\n<th>a</th>\n<th>b</th>\n</tr>\n</thead>\n</table>\n")]
    public void RendersPipeTables(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.PipeTables });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Theory]
    [InlineData("a | b{.x}\n-|-\n", "<table class=\"x\">\n<thead>\n<tr>\n<th>a</th>\n<th>b</th>\n</tr>\n</thead>\n</table>\n")]
    [InlineData("a | b\n-|-{.x}\nc|d\n", "<table class=\"x\">\n<thead>\n<tr>\n<th>a</th>\n<th>b</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>c</td>\n<td>d</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("| a |{.c}\n|---|\n| b |\n", "<table>\n<thead>\n<tr>\n<th>a</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>b</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("|{.center}\nA paragraph\n", "<p>|\nA paragraph</p>\n")]
    public void RendersTheGenericAttributesOfPipeTables(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.PipeTables | MarkdownExtensions.GenericAttributes });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Fact]
    public void PipeTableHasItsRowsAndAlignments()
    {
        // The pipe tables replace the tables of GFM, and a cell of the delimiter row can be empty
        var table = Assert.IsType<MarkdownTableSyntax>(MarkdownSyntaxTree.ParseText("| a | b | c |\n| :-: | | -- |\n| `x\\|y` |\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.Tables | MarkdownExtensions.PipeTables }).GetRoot().Blocks.Single());

        Assert.True(table.IsPipeTable);
        Assert.Equal(SyntaxKind.PipeTable, table.Kind());
        Assert.Equal([MarkdownTableAlignment.Center, MarkdownTableAlignment.None, MarkdownTableAlignment.None], table.Alignments);
        Assert.Equal(2, table.DelimiterRow!.DelimiterTokens.Count);
        Assert.Equal(4, table.DelimiterRow!.PipeTokens.Count);
        Assert.Equal(["a", "b", "c"], table.HeaderRow!.Cells.Select(cell => cell.GetPlainText()));

        // A backslash escapes a pipe as anywhere else
        Assert.Equal("x\\|y", table.Rows.Single().DescendantNodes().OfType<MarkdownCodeSpanSyntax>().Single().Code);
        Assert.False(Assert.IsType<MarkdownTableSyntax>(MarkdownSyntaxTree.ParseText("| a |\n|---|\n", MarkdownParseOptions.GitHubFlavored).GetRoot().Blocks.Single()).IsPipeTable);
    }

    [Fact]
    public void ReadsPipeTablesWithTheOptions()
    {
        #region MarkdownSyntaxTree_PipeTableOptions
        // The first row gives the number of columns, and the dashes the widths of the columns
        var options = new MarkdownParseOptions
        {
            Extensions = MarkdownExtensions.PipeTables,
            PipeTableOptions = new MarkdownPipeTableOptions { UseHeaderForColumnCount = true, InferColumnWidthsFromSeparator = true },
        };
        var tree = MarkdownSyntaxTree.ParseText("Name | Price\n:---|------:\nApple | 1 | each\n", options);

        Assert.Equal("<table>\n<col style=\"width:33.33%\" />\n<col style=\"width:66.67%\" />\n<thead>\n<tr>\n<th align=\"left\">Name</th>\n<th align=\"right\">Price</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td align=\"left\">Apple</td>\n<td align=\"right\">1</td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));

        // A table without a delimiter row has no header row
        options = new MarkdownParseOptions
        {
            Extensions = MarkdownExtensions.PipeTables,
            PipeTableOptions = new MarkdownPipeTableOptions { RequireHeaderSeparator = false },
        };
        tree = MarkdownSyntaxTree.ParseText("a | b\nc | d\n", options);
        var table = tree.GetRoot().Blocks.OfType<MarkdownTableSyntax>().Single();

        Assert.Null(table.HeaderRow);
        Assert.Null(table.DelimiterRow);
        Assert.HasCount(2, table.Rows);
        Assert.Equal("<table>\n<tbody>\n<tr>\n<td>a</td>\n<td>b</td>\n</tr>\n<tr>\n<td>c</td>\n<td>d</td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    // A table without a delimiter row must end the paragraph, and the lines before its first pipe are a paragraph
    [Theory]
    [InlineData("a | b\nc | d\ne\n", "<p>a | b\nc | d\ne</p>\n")]
    [InlineData("x\na | b\n", "<p>x</p>\n<table>\n<tbody>\n<tr>\n<td>a</td>\n<td>b</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("a | b\n-|-\nc\n", "<table>\n<thead>\n<tr>\n<th>a</th>\n<th>b</th>\n</tr>\n</thead>\n</table>\n<p>c</p>\n")]
    public void ReadsPipeTablesWithoutDelimiterRow(string text, string expected)
    {
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.PipeTables, PipeTableOptions = new MarkdownPipeTableOptions { RequireHeaderSeparator = false } };

        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(text, options)));
    }

    // As Markdig, the empty cells that complete the short rows are limited in a document: the rows of a table that would exceed
    // the limit keep their cells
    [Theory]
    [InlineData(500, 1000)]
    [InlineData(600, 1)]
    public void LimitsTheCellsThatCompleteTheRows(int rowCount, int cellsPerRow)
    {
        var text = string.Concat(Enumerable.Repeat("|a", 1000)) + "|\n" + string.Concat(Enumerable.Repeat("|-", 1000)) + "|\n" + string.Concat(Enumerable.Repeat("|x\n", rowCount));
        var html = MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.PipeTables }));

        Assert.Equal(1000 + (rowCount * cellsPerRow), System.Text.RegularExpressions.Regex.Count(html, "<t[hd]>", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void ParsesGridTables()
    {
        #region MarkdownSyntaxTree_GridTables
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GridTables };
        var tree = MarkdownSyntaxTree.ParseText("+-------+:------:+\n| Name  | Price  |\n+=======+========+\n| Apple | 1      |\n|       | each   |\n+-------+--------+\n| - Pear         |\n+-------+--------+\n", options);
        var table = Assert.IsType<MarkdownGridTableSyntax>(tree.GetRoot().Blocks.Single());

        Assert.Equal([MarkdownTableAlignment.None, MarkdownTableAlignment.Center], table.Columns.Select(column => column.Alignment));
        Assert.True(table.Rows[0].IsHeader);

        // The text of a cell is its lines between its pipes, which is a document of its own
        var price = table.Rows[1].Cells[1];
        Assert.Equal(" 1\n each", price.Text);
        Assert.Equal("1\neach", price.Content.GetRoot().Blocks.Single().GetPlainText());
        var pear = table.Rows[2].Cells.Single();
        Assert.Equal(2, pear.ColumnSpan);
        Assert.IsType<MarkdownListSyntax>(pear.Content.GetRoot().Blocks.Single());
        #endregion
    }

    [Theory]
    [InlineData("+-------+-------+\n| Name  | Price |\n+=======+=======+\n| Apple | 1     |\n+-------+-------+\n", "<table>\n<col style=\"width:50%\" />\n<col style=\"width:50%\" />\n<thead>\n<tr>\n<th>Name</th>\n<th>Price</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>Apple</td>\n<td>1</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("+---+---+---+\n| AAAAA | B |\n+---+---+ B +\n| D | E | B |\n+ D +---+---+\n| D | CCCCC |\n+---+---+---+\n", "<table>\n<col style=\"width:33.33%\" />\n<col style=\"width:33.33%\" />\n<col style=\"width:33.33%\" />\n<tbody>\n<tr>\n<td colspan=\"2\">AAAAA</td>\n<td rowspan=\"2\">B\nB\nB</td>\n</tr>\n<tr>\n<td rowspan=\"2\">D\nD\nD</td>\n<td>E</td>\n</tr>\n<tr>\n<td colspan=\"2\">CCCCC</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("+----------+\n| - a      |\n| - b      |\n|          |\n| para     |\n+----------+\n", "<table>\n<col style=\"width:100%\" />\n<tbody>\n<tr>\n<td>\n<ul>\n<li>a</li>\n<li>b</li>\n</ul>\n<p>para</p>\n</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("+-------+\n| [x][r] |\n+-------+\n\n[r]: /url\n", "<table>\n<col style=\"width:100%\" />\n<tbody>\n<tr>\n<td><a href=\"/url\">x</a></td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("> +---+\n> | a |\n> +---+\n", "<blockquote>\n<table>\n<col style=\"width:100%\" />\n<tbody>\n<tr>\n<td>a</td>\n</tr>\n</tbody>\n</table>\n</blockquote>\n")]
    [InlineData("+----+\n| a |\n+----+\n| b\n", "<table>\n<col style=\"width:100%\" />\n<tbody>\n<tr>\n<td>a</td>\n</tr>\n<tr>\n<td>b</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("+---+---+\n| a | b |\n+=======+\n", "<table>\n<col style=\"width:50%\" />\n<col style=\"width:50%\" />\n<thead>\n<tr>\n<th>a</th>\n<th>b</th>\n</tr>\n</thead>\n</table>\n")]
    [InlineData("+---+\n| a | b |\n| c | d | e |\n+---+---+\n", "<table>\n<col style=\"width:100%\" />\n<tbody>\n<tr>\n<td>a\nc</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("+---+---+---+\n| AAAAA | B |\n+ A +---+ B +\n| A | C | B |\n+---+---+---+\n| DDDDD | E |\n+---+---+---+\n", "<p>+---+---+---+\n| AAAAA | B |\n+ A +---+ B +\n| A | C | B |\n+---+---+---+\n| DDDDD | E |\n+---+---+---+</p>\n")]
    [InlineData("+----+\n=\n", "<h1>+----+</h1>\n")]
    public void RendersGridTables(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.GridTables });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Fact]
    public void RendersMediaLinks()
    {
        #region MarkdownSyntaxTree_MediaLinks
        var tree = MarkdownSyntaxTree.ParseText("![Intro](https://youtu.be/mswPy5bt3TQ)\n\n![Song](song.mp3)\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.MediaLinks });

        Assert.Equal("<p><iframe src=\"https://www.youtube.com/embed/mswPy5bt3TQ\" class=\"youtube\" width=\"500\" height=\"281\" frameborder=\"0\" allowfullscreen=\"\"></iframe></p>\n<p><audio width=\"500\" controls=\"\"><source type=\"audio/mpeg\" src=\"song.mp3\"></source></audio></p>\n", MarkdownHtmlRenderer.ToHtml(tree));

        // The options choose the size, and the sites that are an iframe
        var options = new MarkdownHtmlRendererOptions
        {
            MediaLinks = MarkdownMediaLinkOptions.Default with
            {
                Width = "100%",
                Height = "",
                Hosts = [MarkdownMediaHost.Create("videos.example.com", uri => "https://videos.example.com/embed" + uri.AbsolutePath, iframeClass: "example")],
            },
        };
        Assert.Equal("<p><iframe src=\"https://videos.example.com/embed/42\" class=\"example\" width=\"100%\" frameborder=\"0\" allowfullscreen=\"\"></iframe></p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("![](https://videos.example.com/42)\n", tree.Options), options));
        #endregion
    }

    [Fact]
    public void RendersLinksWithTheOptions()
    {
        #region MarkdownHtmlRenderer_LinkOptions
        var tree = MarkdownSyntaxTree.ParseText("[Next page](next.md) ![Logo](logo.png) <https://example.org>\n");
        var options = new MarkdownHtmlRendererOptions
        {
            BaseUrl = new Uri("https://example.com/docs/"),
            LinkRewriter = url => url.EndsWith(".md", StringComparison.Ordinal) ? url[..^3] + ".html" : url,
            LinkRel = "nofollow noopener",
        };

        Assert.Equal("<p><a href=\"https://example.com/docs/next.html\" rel=\"nofollow noopener\">Next page</a> <img src=\"https://example.com/docs/logo.png\" alt=\"Logo\" /> <a href=\"https://example.org\" rel=\"nofollow noopener\">https://example.org</a></p>\n", MarkdownHtmlRenderer.ToHtml(tree, options));

        // Characters that are not ASCII are percent-encoded, unless the options keep them
        var image = MarkdownSyntaxTree.ParseText("![](photo-été.png)\n");
        Assert.Equal("<p><img src=\"photo-%C3%A9t%C3%A9.png\" alt=\"\" /></p>\n", MarkdownHtmlRenderer.ToHtml(image));
        Assert.Equal("<p><img src=\"photo-été.png\" alt=\"\" /></p>\n", MarkdownHtmlRenderer.ToHtml(image, new MarkdownHtmlRendererOptions { EscapeNonAsciiUrlCharacters = false }));
        #endregion
    }

    [Theory]
    [InlineData("Link: [hello](/relative.jpg)", "https://example.com", "https://example.com/relative.jpg")]
    [InlineData("Link: [hello](relative.jpg)", "https://example.com", "https://example.com/relative.jpg")]
    [InlineData("Link: [hello](/relative.jpg?a=b)", "https://example.com/", "https://example.com/relative.jpg?a=b")]
    [InlineData("Link: [hello](relative.jpg#x)", "https://example.com/", "https://example.com/relative.jpg#x")]
    [InlineData("Image: ![alt text](image.jpg \"title\")", "https://example.com", "https://example.com/image.jpg")]
    [InlineData("Link: [hello](https://other.org/a)", "https://example.com", "https://other.org/a")]
    [InlineData("<a@b.c>", "https://example.com", "mailto:a@b.c")]
    public void MakesTheRelativeUrlsAbsolute(string markdown, string baseUrl, string expected)
    {
        var html = MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(markdown), new MarkdownHtmlRendererOptions { BaseUrl = new Uri(baseUrl) });

        Assert.Contains("=\"" + expected + "\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesTheRelOfLinksButNotOfEmailAutolinks()
    {
        var options = new MarkdownHtmlRendererOptions { LinkRel = "nofollow" };

        Assert.Equal("<p><a href=\"mailto:a@b.c\">a@b.c</a> <a href=\"http://www.example.com\" rel=\"nofollow\">www.example.com</a></p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("<a@b.c> www.example.com\n", MarkdownParseOptions.GitHubFlavored), options));
    }

    [Fact]
    public void WritesThePragmaLines()
    {
        #region MarkdownHtmlRenderer_PragmaLines
        var tree = MarkdownSyntaxTree.ParseText("# Title\n\nSome text.\n\n- First\n- Second\n");

        Assert.Equal("<h1 id=\"pragma-line-0\">Title</h1>\n<p id=\"pragma-line-2\">Some text.</p>\n<ul id=\"pragma-line-4\">\n<li>First</li>\n<li id=\"pragma-line-5\">Second</li>\n</ul>\n", MarkdownHtmlRenderer.ToHtml(tree, new MarkdownHtmlRendererOptions { PragmaLines = true }));

        // The editor shows line 3, a blank line: scroll the preview to the closest element
        Assert.Equal(4, tree.GetRoot().FindClosestLine(3));
        #endregion
    }

    [Theory]
    [InlineData("# Title {#custom}\n\ntext", "<h1 id=\"custom\"><a id=\"pragma-line-0\"></a>Title</h1>\n<p id=\"pragma-line-2\">text</p>\n")]
    [InlineData("text\n\n# {#custom}\n\n---", "<p id=\"pragma-line-0\">text</p>\n<a id=\"pragma-line-2\"></a>\n<h1 id=\"custom\"></h1>\n<hr id=\"pragma-line-4\" />\n")]
    [InlineData("- a\n\n  b\n", "<ul id=\"pragma-line-0\">\n<li>\n<p>a</p>\n<p id=\"pragma-line-2\">b</p>\n</li>\n</ul>\n")]
    [InlineData("> - a\n>   - b\n> - c", "<blockquote id=\"pragma-line-0\">\n<ul>\n<li>a\n<ul id=\"pragma-line-1\">\n<li>b</li>\n</ul>\n</li>\n<li id=\"pragma-line-2\">c</li>\n</ul>\n</blockquote>\n")]
    [InlineData("a|b\n-|-\n1|2\n", "<table id=\"pragma-line-0\">\n<thead>\n<tr>\n<th>a</th>\n<th>b</th>\n</tr>\n</thead>\n<tbody>\n<tr id=\"pragma-line-2\">\n<td>1</td>\n<td>2</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("x[^1]\n\n[^1]: note\n", "<p id=\"pragma-line-0\">x<a id=\"fnref:1\" href=\"#fn:1\" class=\"footnote-ref\"><sup>1</sup></a></p>\n<div class=\"footnotes\">\n<hr />\n<ol>\n<li id=\"fn:1\">\n<p id=\"pragma-line-2\">note<a href=\"#fnref:1\" class=\"footnote-back-ref\">&#8617;</a></p>\n</li>\n</ol>\n</div>\n")]
    [InlineData("Foo\nbar\n---\n", "<h2 id=\"pragma-line-2\">Foo\nbar</h2>\n")]
    public void GivesThePragmaLineToTheOutermostBlockOfTheLine(string markdown, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(markdown, new MarkdownParseOptions { Extensions = MarkdownExtensions.PipeTables | MarkdownExtensions.Footnotes | MarkdownExtensions.GenericAttributes });

        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree, new MarkdownHtmlRendererOptions { PragmaLines = true }));
    }

    [Fact]
    public void FindsTheClosestLine()
    {
        var root = MarkdownSyntaxTree.ParseText("test1\n\ntest2\n\ntest3\n\ntest4\n\n# Heading\n\nLong para\non multiple\nlines\nto check that\nlines are\ncorrectly \nfound\n\n- item1\n- item2\n- item3\n\nThis is a last paragraph\n").GetRoot();

        foreach (var exact in new[] { 0, 2, 4, 6, 8, 10, 18, 19, 20, 22 })
        {
            Assert.Equal(exact, root.FindClosestLine(exact));
        }

        Assert.Equal(22, root.FindClosestLine(23));
        Assert.Equal(10, root.FindClosestLine(13));
        Assert.Equal(18, root.FindClosestLine(14));
        Assert.Equal(6, MarkdownSyntaxTree.ParseText("- item1\n  - item11\n  - item12\n    - item121\n  - item13\n    - item131\n      - item1311\n").GetRoot().FindClosestLine(50));
    }

    [Fact]
    public void ThePragmaLinesAreUniqueInTheSpecificationExamples()
    {
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.PipeTables | MarkdownExtensions.GridTables | MarkdownExtensions.Footnotes | MarkdownExtensions.GenericAttributes | MarkdownExtensions.DefinitionLists | MarkdownExtensions.Alerts | MarkdownExtensions.CustomContainers | MarkdownExtensions.Figures | MarkdownExtensions.Footers | MarkdownExtensions.AutoIdentifiers };
        var count = 0;
        foreach (var example in CommonMarkSpec.Examples)
        {
            var html = MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(example.Markdown, options), new MarkdownHtmlRendererOptions { PragmaLines = true });
            var ids = System.Text.RegularExpressions.Regex.Matches(html, "id=\"(?<id>pragma-line-[0-9]+)\"", System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1)).Select(match => match.Groups["id"].Value).ToList();
            Assert.HasCount(ids.Count, ids.Distinct(StringComparer.Ordinal));
            count++;
        }

        Assert.True(count > 600);
    }

    [Fact]
    public void ParsesJiraLinks()
    {
        #region MarkdownSyntaxTree_JiraLinks
        var options = new MarkdownParseOptions
        {
            Extensions = MarkdownExtensions.JiraLinks,
            JiraLinks = new MarkdownJiraLinkOptions { BaseUrl = "https://example.atlassian.net" },
        };
        var tree = MarkdownSyntaxTree.ParseText("Fixed in (PROJ-123), not in xPROJ-1.\n", options);

        var link = tree.GetRoot().DescendantNodes().OfType<MarkdownJiraLinkSyntax>().Single();
        Assert.Equal("PROJ", link.ProjectKey);
        Assert.Equal("123", link.IssueNumber);
        Assert.Equal("<p>Fixed in (<a href=\"https://example.atlassian.net/browse/PROJ-123\" target=\"_blank\">PROJ-123</a>), not in xPROJ-1.</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void ParsesMarkdigAutolinks()
    {
        #region MarkdownSyntaxTree_MarkdigAutolinks
        var options = new MarkdownParseOptions
        {
            Extensions = MarkdownExtensions.MarkdigAutolinks,
            AutolinkOptions = new MarkdownAutolinkOptions { UseHttpsForWwwLinks = true },
        };
        var tree = MarkdownSyntaxTree.ParseText("See www.example.com, mailto:me@example.com or tel:+15551234.\n", options);

        var links = tree.GetRoot().DescendantNodes().OfType<MarkdownAutolinkSyntax>().ToList();
        Assert.Equal(["https://www.example.com", "mailto:me@example.com", "tel:+15551234"], links.Select(link => link.Destination));
        Assert.Equal("<p>See <a href=\"https://www.example.com\">www.example.com</a>, <a href=\"mailto:me@example.com\">me@example.com</a> or <a href=\"tel:+15551234\">+15551234</a>.</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Theory]
    [InlineData("**http://www.a.com**", "<p><strong><a href=\"http://www.a.com\">http://www.a.com</a></strong></p>\n")]
    [InlineData("[http://a.com/ link](http://b.com)", "<p><a href=\"http://b.com\">http://a.com/ link</a></p>\n")]
    [InlineData("<a href=\"x\">http://a.com</a>", "<p><a href=\"x\">http://a.com</a></p>\n")]
    [InlineData("(http://a.com/b_(c))", "<p>(<a href=\"http://a.com/b_(c)\">http://a.com/b_(c)</a>)</p>\n")]
    [InlineData("xhttp://a.com http://localhost ftp:// mailto:@a.com", "<p>xhttp://a.com http://localhost ftp:// mailto:@a.com</p>\n")]
    public void RendersMarkdigAutolinks(string text, string expected)
    {
        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.MarkdigAutolinks })));
    }

    [Fact]
    public void ParsesCjkFriendlyEmphasis()
    {
        #region MarkdownSyntaxTree_CjkFriendlyEmphasis
        // CommonMark does not close the emphasis: the punctuation before ** is followed by a letter
        Assert.Equal("<p>**テスト。**です</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("**テスト。**です\n")));

        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.CjkFriendlyEmphasis };
        Assert.Equal("<p><strong>テスト。</strong>です</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("**テスト。**です\n", options)));
        #endregion
    }

    [Fact]
    public void RendersTheClassesOfBootstrap()
    {
        #region MarkdownHtmlRenderer_Bootstrap
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Bootstrap | MarkdownExtensions.PipeTables };
        var tree = MarkdownSyntaxTree.ParseText("> Quote\n\n![Logo](logo.png)\n\n| Name |\n|------|\n| Apple |\n", options);

        Assert.Equal("<blockquote class=\"blockquote\">\n<p>Quote</p>\n</blockquote>\n<p><img src=\"logo.png\" class=\"img-fluid\" alt=\"Logo\" /></p>\n<table class=\"table\">\n<thead>\n<tr>\n<th>Name</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>Apple</td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void RendersTheDirectionOfTheText()
    {
        #region MarkdownHtmlRenderer_Globalization
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Globalization | MarkdownExtensions.PipeTables };
        var tree = MarkdownSyntaxTree.ParseText("Hello\n\n> مرحبا *world*\n\n| الاسم |\n|-------|\n| x |\n", options);

        Assert.Equal("<p>Hello</p>\n<blockquote dir=\"rtl\">\n<p dir=\"rtl\">مرحبا <em>world</em></p>\n</blockquote>\n<table dir=\"rtl\" align=\"right\">\n<thead>\n<tr>\n<th>الاسم</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>x</td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    // The direction is the one of the first character that has one, in the text that Markdig merges: the text of a construct that
    // is not parsed, but not an emphasis delimiter, a pipe of a pipe table, or a bracket that a ] closes without a link
    [Theory]
    [InlineData("]مرحبا\n", true)]
    [InlineData("\\مرحبا\n", true)]
    [InlineData("\\*مرحبا\n", true)]
    [InlineData("!مرحبا\n", true)]
    [InlineData("_ مرحبا\n", true)]
    [InlineData("[مرحبا\n", true)]
    [InlineData("+مرحبا\n", true)]
    [InlineData("x ]مرحبا\n", false)]
    [InlineData("*مرحبا\n", false)]
    [InlineData("++مرحبا\n", false)]
    [InlineData("[مرحبا] x\n", false)]
    [InlineData("[] مرحبا\n", false)]
    [InlineData("|مرحبا\n", false)]
    [InlineData("]\nمرحبا\n", false)]
    [InlineData("&amp;مرحبا\n", false)]
    [InlineData("[مرحبا](url) x\n", true)]
    public void TheDirectionIsTheOneOfTheTextThatMarkdigMerges(string text, bool isRightToLeft)
    {
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Globalization | MarkdownExtensions.PipeTables | MarkdownExtensions.EmphasisExtras };
        var html = MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(text, options));

        Assert.Equal(isRightToLeft, html.Contains("<p dir=\"rtl\">", StringComparison.Ordinal), html);
    }

    [Fact]
    public void TheAdvancedExtensionsAreTheOnesOfMarkdig()
    {
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Advanced };
        var tree = MarkdownSyntaxTree.ParseText("| a |\n|---|\n| www.example.com |\n\n+---+\n| b |\n+---+\n", options);

        Assert.Equal("<table>\n<thead>\n<tr>\n<th>a</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td><a href=\"http://www.example.com\">www.example.com</a></td>\n</tr>\n</tbody>\n</table>\n<table>\n<col style=\"width:100%\" />\n<tbody>\n<tr>\n<td>b</td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));
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
        Assert.True(table.HeaderRow!.IsHeader);
        Assert.True(table.DelimiterRow!.IsDelimiter);
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
