using System.Diagnostics;
using Meziantou.Framework.Markdown.Parsing;
using Meziantou.Xunit;

namespace Meziantou.Framework.Markdown.Tests;

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

    [Fact]
    public void DocumentationExample_Tables()
    {
        #region MarkdownSyntaxTree_Tables
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Tables };
        var tree = MarkdownSyntaxTree.ParseText("| Fruit | Price |\n| :---- | ----: |\n| Apple | 1 |\n| Pear |\n", options);
        var table = Assert.IsType<MarkdownTableSyntax>(tree.GetRoot().Blocks.Single());

        Assert.Equal([MarkdownTableAlignment.Left, MarkdownTableAlignment.Right], table.Alignments);
        Assert.Equal("Fruit", table.HeaderRow!.Cells[0].GetPlainText());
        Assert.Equal(["Apple", "1"], table.Rows[0].Cells.Select(cell => cell.GetPlainText()));

        // The row with fewer cells than the header gets empty cells
        Assert.Equal("<table>\n<thead>\n<tr>\n<th align=\"left\">Fruit</th>\n<th align=\"right\">Price</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td align=\"left\">Apple</td>\n<td align=\"right\">1</td>\n</tr>\n<tr>\n<td align=\"left\">Pear</td>\n<td align=\"right\"></td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void DocumentationExample_Strikethrough()
    {
        #region MarkdownSyntaxTree_Strikethrough
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Strikethrough };
        var tree = MarkdownSyntaxTree.ParseText("The meeting is ~~Monday~~ Tuesday.\n", options);

        var strikethrough = tree.GetRoot().DescendantNodes().OfType<MarkdownStrikethroughSyntax>().Single();
        Assert.Equal("Monday", strikethrough.GetPlainText());
        Assert.Equal("<p>The meeting is <del>Monday</del> Tuesday.</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void DocumentationExample_TaskLists()
    {
        #region MarkdownSyntaxTree_TaskLists
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.TaskLists };
        var tree = MarkdownSyntaxTree.ParseText("- [x] Write the draft\n- [ ] Review it\n", options);

        var items = tree.GetRoot().DescendantNodes().OfType<MarkdownListItemSyntax>().ToList();
        Assert.True(items[0].IsTask);
        Assert.True(items[0].IsChecked);
        Assert.False(items[1].IsChecked);
        Assert.Equal("<ul>\n<li><input checked=\"\" disabled=\"\" type=\"checkbox\"> Write the draft</li>\n<li><input disabled=\"\" type=\"checkbox\"> Review it</li>\n</ul>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void DocumentationExample_Autolinks()
    {
        #region MarkdownSyntaxTree_Autolinks
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Autolinks };
        var tree = MarkdownSyntaxTree.ParseText("See www.example.com, https://example.com/docs or contact@example.com.\n", options);

        var links = tree.GetRoot().DescendantNodes().OfType<MarkdownAutolinkSyntax>().ToList();
        Assert.Equal(["http://www.example.com", "https://example.com/docs", "mailto:contact@example.com"], links.Select(link => link.Destination));
        Assert.Equal("<p>See <a href=\"http://www.example.com\">www.example.com</a>, <a href=\"https://example.com/docs\">https://example.com/docs</a> or <a href=\"mailto:contact@example.com\">contact@example.com</a>.</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void DocumentationExample_TagFilter()
    {
        #region MarkdownHtmlRenderer_TagFilter
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.TagFilter };
        var tree = MarkdownSyntaxTree.ParseText("<script>alert(1)</script>\n\nSome <b>bold</b> text\n", options);

        // The < of the filtered tags is escaped, the other tags are written as they are
        Assert.Equal("&lt;script>alert(1)&lt;/script>\n<p>Some <b>bold</b> text</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void TagFilterIsNotASanitizer()
    {
        #region MarkdownHtmlRenderer_TagFilterBypass
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.TagFilter };
        var tree = MarkdownSyntaxTree.ParseText("<div>\n<script/src=https://example.com/x.js></script>\n</div>\n", options);

        // As in GitHub, a / after the name is not filtered: browsers read <script/src=...> as a script
        Assert.Equal("<div>\n<script/src=https://example.com/x.js>&lt;/script>\n</div>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void DocumentationExample_EmphasisExtras()
    {
        #region MarkdownSyntaxTree_EmphasisExtras
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.EmphasisExtras };
        var tree = MarkdownSyntaxTree.ParseText("H~2~O, 2^10^, ++added++, ==marked== and ~~removed~~\n", options);

        var styles = tree.GetRoot().DescendantNodes().OfType<MarkdownEmphasisExtraSyntax>().Select(node => node.Style);
        Assert.Equal([MarkdownEmphasisExtraStyle.Subscript, MarkdownEmphasisExtraStyle.Superscript, MarkdownEmphasisExtraStyle.Inserted, MarkdownEmphasisExtraStyle.Marked], styles);
        Assert.Equal("<p>H<sub>2</sub>O, 2<sup>10</sup>, <ins>added</ins>, <mark>marked</mark> and <del>removed</del></p>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void DocumentationExample_HardLineBreaks()
    {
        #region MarkdownSyntaxTree_HardLineBreaks
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.HardLineBreaks };
        var tree = MarkdownSyntaxTree.ParseText("Roses are red,\nViolets are blue.\n", options);

        Assert.Equal("<p>Roses are red,<br />\nViolets are blue.</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void DocumentationExample_YamlFrontMatter()
    {
        #region MarkdownSyntaxTree_YamlFrontMatter
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.YamlFrontMatter };
        var tree = MarkdownSyntaxTree.ParseText("---\ntitle: Hello\ntags: [a, b]\n---\n# Hello\n", options);

        var frontMatter = Assert.IsType<MarkdownFrontMatterSyntax>(tree.GetRoot().Blocks[0]);
        Assert.Equal("title: Hello\ntags: [a, b]\n", frontMatter.Yaml);

        // The renderer does not write the front matter
        Assert.Equal("<h1>Hello</h1>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void DocumentationExample_Mathematics()
    {
        #region MarkdownSyntaxTree_Mathematics
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Mathematics };
        var tree = MarkdownSyntaxTree.ParseText("The energy is $E = mc^2$.\n\n$$\n\\sum_{i=1}^n i\n$$\n", options);

        Assert.Equal("E = mc^2", tree.GetRoot().DescendantNodes().OfType<MarkdownMathInlineSyntax>().Single().Math);
        Assert.Equal("\\sum_{i=1}^n i\n", Assert.IsType<MarkdownMathBlockSyntax>(tree.GetRoot().Blocks[1]).Math);
        Assert.Equal("<p>The energy is <span class=\"math\">\\(E = mc^2\\)</span>.</p>\n<div class=\"math\">\n\\[\n\\sum_{i=1}^n i\n\\]</div>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void DocumentationExample_Abbreviations()
    {
        #region MarkdownSyntaxTree_Abbreviations
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Abbreviations };
        var tree = MarkdownSyntaxTree.ParseText("*[HTML]: Hypertext Markup Language\n\nHTML is the language of the web, not XHTML.\n", options);

        var abbreviation = tree.GetRoot().DescendantNodes().OfType<MarkdownAbbreviationSyntax>().Single();
        Assert.Equal("Hypertext Markup Language", abbreviation.Definition!.Title);

        // The definition is not written, and only whole words are abbreviations
        Assert.Equal("<p><abbr title=\"Hypertext Markup Language\">HTML</abbr> is the language of the web, not XHTML.</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void DocumentationExample_Footnotes()
    {
        #region MarkdownSyntaxTree_Footnotes
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Footnotes };
        var tree = MarkdownSyntaxTree.ParseText("Markdown was created in 2004[^1].\n\n[^1]: By John Gruber.\n", options);

        var reference = tree.GetRoot().DescendantNodes().OfType<MarkdownFootnoteReferenceSyntax>().Single();
        Assert.Equal("By John Gruber.", reference.Definition!.GetPlainText());
        Assert.Equal("<p>Markdown was created in 2004<a id=\"fnref:1\" href=\"#fn:1\" class=\"footnote-ref\"><sup>1</sup></a>.</p>\n<div class=\"footnotes\">\n<hr />\n<ol>\n<li id=\"fn:1\">\n<p>By John Gruber.<a href=\"#fnref:1\" class=\"footnote-back-ref\">&#8617;</a></p>\n</li>\n</ol>\n</div>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void DocumentationExample_GenericAttributes()
    {
        #region MarkdownSyntaxTree_GenericAttributes
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GenericAttributes };
        var tree = MarkdownSyntaxTree.ParseText("# Introduction {#intro .lead}\n\nSee [the docs](https://example.com){title=\"Documentation\" onclick=\"run()\"}.\n\n{.note}\nA note.\n", options);

        var attributes = tree.GetRoot().DescendantNodes().OfType<MarkdownGenericAttributesSyntax>().First();
        Assert.Equal("intro", attributes.Id);
        Assert.Equal(["lead"], attributes.Classes);
        Assert.IsType<MarkdownHeadingSyntax>(attributes.Target);

        // The renderer does not write the event handlers
        Assert.Equal("<h1 id=\"intro\" class=\"lead\">Introduction</h1>\n<p>See <a href=\"https://example.com\" title=\"Documentation\">the docs</a>.</p>\n<p class=\"note\">A note.</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void DocumentationExample_Alerts()
    {
        #region MarkdownSyntaxTree_Alerts
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Alerts };
        var tree = MarkdownSyntaxTree.ParseText("> [!WARNING]\n> Back up your files first.\n", options);

        var alert = Assert.IsType<MarkdownAlertSyntax>(tree.GetRoot().Blocks.Single());
        Assert.Equal("WARNING", alert.AlertKind);

        var html = MarkdownHtmlRenderer.ToHtml(tree);
        Assert.StartsWith("<div class=\"markdown-alert markdown-alert-warning\">\n<p class=\"markdown-alert-title\"><svg", html);
        Assert.EndsWith("</svg>Warning</p>\n<p>Back up your files first.</p>\n</div>\n", html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_Diagrams()
    {
        #region MarkdownSyntaxTree_Diagrams
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Diagrams };
        var tree = MarkdownSyntaxTree.ParseText("```mermaid\ngraph LR\n  A --> B\n```\n\n```nomnoml\n[A]->[B]\n```\n", options);

        Assert.Equal("<pre class=\"mermaid\">graph LR\n  A --> B\n</pre>\n<div class=\"nomnoml\">[A]->[B]\n</div>\n", MarkdownHtmlRenderer.ToHtml(tree));
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
    [InlineData("-\0a\n", "<p>-\uFFFDa</p>\n")]
    [InlineData("1.\0a\n", "<p>1.\uFFFDa</p>\n")]
    [InlineData("- \0a\n", "<ul>\n<li>\uFFFDa</li>\n</ul>\n")]
    [InlineData("<div>\n-->\n</div>\n\n<!-- a\n-->\nb\n", "<div>\n-->\n</div>\n<!-- a\n-->\n<p>b</p>\n")]
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
    [InlineData("<div>\n</STYLE\nx\n", "<div>\n&lt;/STYLE\nx\n")]
    [InlineData("<div>\n<scripts\nx\n", "<div>\n<scripts\nx\n")]
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
    public void DisableHtmlKeepsTheLinks()
    {
        #region MarkdownParseOptions_DisableHtml
        var markdown = "<script>alert(1)</script>\n\nSome <b>bold</b> text and [a link](javascript:alert(1))";

        Assert.Equal("<script>alert(1)</script>\n<p>Some <b>bold</b> text and <a href=\"javascript:alert(1)\">a link</a></p>\n", MarkdownConverter.ToHtml(markdown));

        // The HTML is written as text, but the URLs of the links are not filtered
        var options = new MarkdownParseOptions { DisableHtml = true };
        Assert.Equal("<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>\n<p>Some &lt;b&gt;bold&lt;/b&gt; text and <a href=\"javascript:alert(1)\">a link</a></p>\n", MarkdownConverter.ToHtml(markdown, options));
        #endregion
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
    [InlineData("```mermaid {#d .big title=t}\na\n```\n", "<pre id=\"d\" class=\"big mermaid\" title=\"t\">a\n</pre>\n")]
    [InlineData("{.p}\n```nomnoml\n[a]\n```\n", "<div class=\"nomnoml p\">[a]\n</div>\n")]
    public void RendersTheGenericAttributesOfDiagrams(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.Diagrams | MarkdownExtensions.GenericAttributes });

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

    // The block quotes of a grid table cell are in the table, not in the document
    [Theory]
    [InlineData(false, "<td>\n<blockquote>\n<p>[!NOTE]\na</p>\n</blockquote>\n</td>")]
    [InlineData(true, "<td>\n<div class=\"markdown-alert markdown-alert-note\">\n")]
    public void GridTableCellBlockQuotesAreAlertsOnlyWhenNestedAlertsAreAllowed(bool allowNested, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText("+-----------+\n| > [!NOTE] |\n| > a       |\n+-----------+\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.Alerts | MarkdownExtensions.GridTables, AllowNestedAlerts = allowNested });

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

    // A grid table cell has no footnote definition: its references are to the footnotes of the document, numbered where the table is
    [Theory]
    [InlineData("+-------+\n| a[^1] |\n+-------+\n\n[^1]: note\n", "<td>a<a id=\"fnref:1\" href=\"#fn:1\" class=\"footnote-ref\"><sup>1</sup></a></td>\n</tr>\n</tbody>\n</table>\n<div class=\"footnotes\">\n<hr />\n<ol>\n<li id=\"fn:1\">\n<p>note<a href=\"#fnref:1\" class=\"footnote-back-ref\">&#8617;</a></p>\n</li>\n</ol>\n</div>\n")]
    [InlineData("+---------+\n| y[^b]   |\n+---------+\n\nz[^a] w[^b]\n\n[^a]: A\n[^b]: B\n", "<td>y<a id=\"fnref:1\" href=\"#fn:1\" class=\"footnote-ref\"><sup>1</sup></a></td>\n</tr>\n</tbody>\n</table>\n<p>z<a id=\"fnref:3\" href=\"#fn:2\" class=\"footnote-ref\"><sup>2</sup></a> w<a id=\"fnref:2\" href=\"#fn:1\" class=\"footnote-ref\"><sup>1</sup></a></p>\n<div class=\"footnotes\">\n<hr />\n<ol>\n<li id=\"fn:1\">\n<p>B<a href=\"#fnref:1\" class=\"footnote-back-ref\">&#8617;</a><a href=\"#fnref:2\" class=\"footnote-back-ref\">&#8617;</a></p>\n</li>\n<li id=\"fn:2\">\n<p>A<a href=\"#fnref:3\" class=\"footnote-back-ref\">&#8617;</a></p>\n</li>\n</ol>\n</div>\n")]
    [InlineData("+-----------+\n| [^1]: a   |\n|           |\n| b         |\n+-----------+\n", "<td>\n<p>b</p>\n</td>")]
    [InlineData("+-------------+\n| [^1]: a     |\n|             |\n| b [^1]      |\n+-------------+\n\nc [^2]\n\n[^2]: z\n", "<td>\n<p>b <a href=\"a\">^1</a></p>\n</td>\n</tr>\n</tbody>\n</table>\n<p>c <a id=\"fnref:1\" href=\"#fn:1\" class=\"footnote-ref\"><sup>1</sup></a></p>\n<div class=\"footnotes\">\n<hr />\n<ol>\n<li id=\"fn:1\">\n<p>z<a href=\"#fnref:1\" class=\"footnote-back-ref\">&#8617;</a></p>\n</li>\n</ol>\n</div>\n")]
    public void RendersFootnotesOfGridTableCells(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.Footnotes | MarkdownExtensions.GridTables });

        Assert.Contains(expected, MarkdownHtmlRenderer.ToHtml(tree), StringComparison.Ordinal);
    }

    [Fact]
    public void GridTableCellFootnoteReferenceHasTheDefinitionOfTheDocument()
    {
        var root = MarkdownSyntaxTree.ParseText("+-------+\n| a[^1] |\n+-------+\n\n[^1]: note\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.Footnotes | MarkdownExtensions.GridTables }).GetRoot();
        var content = Assert.IsType<MarkdownGridTableSyntax>(root.Blocks[0]).Rows[0].Cells[0].Content.GetRoot();
        var reference = content.DescendantNodes().OfType<MarkdownFootnoteReferenceSyntax>().Single();

        Assert.Same(root.Blocks[1], reference.Definition);
        Assert.Same(root.Blocks[1], content.GetFootnoteDefinition("^1"));
    }

    [Fact]
    public void RendersTheFootnoteReferencesOfAnEditedGridTableCell()
    {
        var tree = MarkdownSyntaxTree.ParseText("+-------+\n| a[^1] |\n+-------+\n\n[^1]: note\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.Footnotes | MarkdownExtensions.GridTables });
        var content = Assert.IsType<MarkdownGridTableSyntax>(tree.GetRoot().Blocks[0]).Rows[0].Cells[0].Content;
        var edited = (MarkdownSyntaxTree)content.WithChangedText(SourceText.From("b[^1] c[^1]"));

        Assert.Equal("<p>b<a id=\"fnref:2\" href=\"#fn:1\" class=\"footnote-ref\"><sup>1</sup></a> c<a id=\"fnref:3\" href=\"#fn:1\" class=\"footnote-ref\"><sup>1</sup></a></p>\n", MarkdownHtmlRenderer.ToHtml(edited));
        Assert.Equal("<p>a<a id=\"fnref:1\" href=\"#fn:1\" class=\"footnote-ref\"><sup>1</sup></a></p>\n", MarkdownHtmlRenderer.ToHtml(content));
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
    [InlineData("[l](u \"md\"){#i title=t onclick=x data-x=1 hidden}\n", "<p><a href=\"u\" id=\"i\" hidden=\"\" title=\"md\">l</a></p>\n")]
    [InlineData("[l](u){title=t}\n", "<p><a href=\"u\" title=\"t\">l</a></p>\n")]
    [InlineData("![real](x.png){alt=fake}\n", "<p><img src=\"x.png\" alt=\"real\" /></p>\n")]
    [InlineData("![real](x.png \"t\"){ALT=fake Title=u lang=fr}\n", "<p><img src=\"x.png\" lang=\"fr\" alt=\"real\" title=\"t\" /></p>\n")]
    [InlineData("a{#x .b lang=de}{#y .c lang=en lang=fr}\n", "<p id=\"y\" class=\"b c\" lang=\"de\">a</p>\n")]
    [InlineData("a{#y .c lang=en lang=fr}\n", "<p id=\"y\" class=\"c\" lang=\"en\">a</p>\n")]
    [InlineData("a{lang=en LANG=fr title=a title=b title=c title=d title=e title=f title=g title=h title=i}\n", "<p lang=\"en\" title=\"a\">a</p>\n")]
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
    [InlineData("# a {#b}\n# b\n# b\n", "b,b-1,b-2")]
    [InlineData("{#a}\nP\n\n# a\n", "a-1")]
    public void HeadingsHaveIdentifiers(string text, string expected)
    {
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.AutoIdentifiers | MarkdownExtensions.GenericAttributes | MarkdownExtensions.Mathematics | MarkdownExtensions.Footnotes | MarkdownExtensions.Abbreviations };
        var root = MarkdownSyntaxTree.ParseText(text, options).GetRoot();

        Assert.Equal(expected, string.Join(',', root.DescendantNodes().OfType<MarkdownHeadingSyntax>().Select(heading => heading.Identifier)));
    }

    // The headings of the grid table cells get their identifiers in the order of the text, with the ones of the document
    [Theory]
    [InlineData("# h\n\n+-----+\n| # h |\n+-----+\n\n# h\n", "<h1 id=\"h\">h</h1>\n<table>\n<col style=\"width:100%\" />\n<tbody>\n<tr>\n<td><h1 id=\"h-1\">h</h1>\n</td>\n</tr>\n</tbody>\n</table>\n<h1 id=\"h-2\">h</h1>\n")]
    [InlineData("+-----------+\n| # h {#x}  |\n+-----------+\n\n# x\n", "<h1 id=\"x\">h</h1>\n</td>\n</tr>\n</tbody>\n</table>\n<h1 id=\"x-1\">x</h1>\n")]
    public void GridTableCellHeadingIdentifiersAreUniqueInTheDocument(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.AutoIdentifiers | MarkdownExtensions.GenericAttributes | MarkdownExtensions.GridTables });

        Assert.EndsWith(expected, MarkdownHtmlRenderer.ToHtml(tree), StringComparison.Ordinal);
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

        // The default shortcodes, with a few of the default smileys: -> stays as it is
        var smileys = new[] { ":)", ";)" }.ToDictionary(smiley => smiley, smiley => MarkdownEmojiMapping.Default.Smileys[smiley], StringComparer.Ordinal);
        var fewSmileys = new MarkdownEmojiMapping(MarkdownEmojiMapping.Default.Shortcodes, smileys);
        Assert.Equal("<p>\U0001F680 \U0001F603 -&gt; b</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(":rocket: :) -> b\n", options with { EmojiMapping = fewSmileys })));
        #endregion
    }

    [Theory]
    [InlineData("a :) and :angry:\n", "<p>a \U0001F603 and \U0001F620</p>\n")]
    [InlineData("not:) x:angry:x\n", "<p>not:) x:angry:x</p>\n")]
    [InlineData("]:) []:) [a]:)\n", "<p>\U0001F608 [\U0001F608 [a]\U0001F603</p>\n")]
    [InlineData("<3 -> <=> `:)` \\:)\n", "<p>\u2764\uFE0F \u2192 \u21D4 <code>:)</code> :)</p>\n")]
    [InlineData("a :* b *c :* d*\n", "<p>a \U0001F617 b <em>c :</em> d*</p>\n")]
    [InlineData("*:)*\n", "<p><em>\U0001F603</em></p>\n")]
    [InlineData(":thumbsup: :man_with_turban:\n", "<p>\U0001F44D \U0001F473\u200D\u2642\uFE0F</p>\n")]
    [InlineData("a :-* b X-) c :-| d\n", "<p>a \U0001F617 b \U0001F606 c \U0001F610 d</p>\n")]
    [InlineData(":p :-s :Z :@\n", "<p>\U0001F61B \U0001F612 \U0001F612 \U0001F621</p>\n")]
    [InlineData("</3 <- =>\n", "<p>\U0001F494 \u2190 \u21D2</p>\n")]
    [InlineData("Use :param, :Dog and :p.\n", "<p>Use :param, :Dog and \U0001F61B.</p>\n")]
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
    public void EmojiMappingExposesItsShortcodesAndSmileys()
    {
        Assert.Equal("\U0001F680", MarkdownEmojiMapping.Default.Shortcodes[":rocket:"]);
        Assert.Equal("→", MarkdownEmojiMapping.Default.Shortcodes[":custom_arrow_right:"]);
        Assert.Equal("\U0001F603", MarkdownEmojiMapping.Default.Shortcodes[MarkdownEmojiMapping.Default.Smileys[":)"]]);
        Assert.Equal(":custom_arrow_right:", MarkdownEmojiMapping.Default.Smileys["->"]);
        Assert.False(MarkdownEmojiMapping.Default.Shortcodes.ContainsKey(":)"));
        Assert.Empty(MarkdownEmojiMapping.DefaultWithoutSmileys.Smileys);
        Assert.HasCount(MarkdownEmojiMapping.Default.Shortcodes.Count, MarkdownEmojiMapping.DefaultWithoutSmileys.Shortcodes);

        var shortcodes = new Dictionary<string, string> { [":ok:"] = "\u2705" };
        var smileys = new Dictionary<string, string> { ["(y)"] = ":ok:" };
        var mapping = new MarkdownEmojiMapping(shortcodes, smileys);
        shortcodes[":ko:"] = "\u274C";
        smileys["(n)"] = ":ko:";

        Assert.Equal(":ok:", Assert.Single(mapping.Shortcodes).Key);
        Assert.Equal(new KeyValuePair<string, string>("(y)", ":ok:"), Assert.Single(mapping.Smileys));
        Assert.IsNotType<Dictionary<string, string>>(mapping.Shortcodes);
        Assert.IsNotType<Dictionary<string, string>>(mapping.Smileys);
    }

    [Fact]
    public void EmojiMappingSmileysUseTheKeysOfShortcodes()
    {
        // The shortcodes ignore the case: the smileys are for the keys of Shortcodes, so that the mapping can be made again from them
        var shortcodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [":OK:"] = "\u2705", [":yes:"] = "\u2705" };
        var mapping = new MarkdownEmojiMapping(shortcodes, new Dictionary<string, string> { ["(y)"] = ":ok:", ["(Y)"] = ":YES:" });

        Assert.Equal(":OK:", mapping.Smileys["(y)"]);
        Assert.Equal(":yes:", mapping.Smileys["(Y)"]);

        var copy = new MarkdownEmojiMapping(mapping.Shortcodes, mapping.Smileys);
        Assert.True(copy.TryGetEmoji("(y)", out var emoji));
        Assert.Equal("\u2705", emoji);
    }

    [Fact]
    public void DocumentationExample_Figures()
    {
        #region MarkdownSyntaxTree_Figures
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Figures };
        var tree = MarkdownSyntaxTree.ParseText("^^^\n![Logo](logo.png)\n^^^ The *logo*\n", options);

        var figure = Assert.IsType<MarkdownFigureSyntax>(tree.GetRoot().Blocks.Single());
        Assert.Equal("The logo", figure.Blocks.OfType<MarkdownFigureCaptionSyntax>().Single().GetPlainText());
        Assert.Equal("<figure>\n<p><img src=\"logo.png\" alt=\"Logo\" /></p>\n<figcaption>The <em>logo</em></figcaption>\n</figure>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void DocumentationExample_Footers()
    {
        #region MarkdownSyntaxTree_Footers
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Footers };
        var tree = MarkdownSyntaxTree.ParseText("> Simplicity is prerequisite for reliability.\n^^ Edsger W. Dijkstra\n", options);

        Assert.IsType<MarkdownFooterSyntax>(tree.GetRoot().Blocks[1]);
        Assert.Equal("<blockquote>\n<p>Simplicity is prerequisite for reliability.</p>\n</blockquote>\n<footer>Edsger W. Dijkstra</footer>\n", MarkdownHtmlRenderer.ToHtml(tree));
        #endregion
    }

    [Fact]
    public void DocumentationExample_Citations()
    {
        #region MarkdownSyntaxTree_Citations
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Citations };
        var tree = MarkdownSyntaxTree.ParseText("As \"\"The Pragmatic Programmer\"\" says.\n", options);

        var citation = tree.GetRoot().DescendantNodes().OfType<MarkdownEmphasisExtraSyntax>().Single();
        Assert.Equal(MarkdownEmphasisExtraStyle.Citation, citation.Style);
        Assert.Equal("<p>As <cite>The Pragmatic Programmer</cite> says.</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
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

    // As cmark-gfm: once a table has completed its short rows with more than 0x80000 empty cells, its next line is a paragraph
    [Theory]
    [InlineData("x", 525)]
    [InlineData("x|y", 526)]
    public void GfmTableLimitsTheEmptyCellsOfItsShortRows(string row, int expectedRowCount)
    {
        var text = string.Join('|', Enumerable.Repeat("a", 1000)) + "\n" + string.Join('|', Enumerable.Repeat("-", 1000)) + "\n" + string.Concat(Enumerable.Repeat(row + "\n", 600));
        var tree = MarkdownSyntaxTree.ParseText(text, MarkdownParseOptions.GitHubFlavored);

        Assert.Equal(text, tree.GetRoot().ToFullString());
        var blocks = tree.GetRoot().Blocks;
        Assert.HasCount(2, blocks);
        Assert.HasCount(expectedRowCount, Assert.IsType<MarkdownTableSyntax>(blocks[0]).Rows);
        Assert.IsType<MarkdownParagraphSyntax>(blocks[1]);
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
    [InlineData("a | b\n-- | {e=\"f\n\"}--!\n", "<p>a | b\n-- | --!</p>\n")]
    [InlineData("a | b\n-- | {e=\"f\n\"} --\nc | d\n", "<table>\n<thead>\n<tr>\n<th>a</th>\n<th>b</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>c</td>\n<td>d</td>\n</tr>\n</tbody>\n</table>\n")]
    public void RendersTheGenericAttributesOfPipeTables(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.PipeTables | MarkdownExtensions.GenericAttributes });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));

        // The delimiter row of a table, whose attributes can span lines, has a column for each delimiter
        foreach (var table in tree.GetRoot().Blocks.OfType<MarkdownTableSyntax>())
        {
            Assert.HasCount(table.DelimiterRow!.DelimiterTokens.Count, table.Alignments);
        }
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

    [Fact]
    public void PipeTableRowAfterAttributesBeforeALinkThatSpansLines()
    {
        // The link spans the two lines: the attributes of the first line are not in the second row
        const string Text = "{}[|\n]()|";
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.PipeTables | MarkdownExtensions.GenericAttributes, PipeTableOptions = new MarkdownPipeTableOptions { RequireHeaderSeparator = false } };

        var tree = MarkdownSyntaxTree.ParseText(Text, options);

        Assert.Equal(Text, tree.GetRoot().ToFullString());
        Assert.NotEmpty(MarkdownHtmlRenderer.ToHtml(tree));
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

    [Fact]
    public void GridTableCellsHaveTheAttributesOfBootstrapAndOfTheDirectionOfTheText()
    {
        var tree = MarkdownSyntaxTree.ParseText("+--------------+\n| ![a](b.png)  |\n+--------------+\n| # \u05E9\u05DC\u05D5\u05DD     |\n+--------------+\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.GridTables | MarkdownExtensions.Bootstrap | MarkdownExtensions.Globalization });
        var html = MarkdownHtmlRenderer.ToHtml(tree);

        Assert.Contains("<img src=\"b.png\" class=\"img-fluid\" alt=\"a\" />", html, StringComparison.Ordinal);
        Assert.Contains("<h1 dir=\"rtl\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void GridTablesInCellsCountInTheNestingDepth()
    {
        string[] lines = ["a"];
        for (var i = 0; i < 6; i++)
        {
            var width = lines.Max(line => line.Length) + 2;
            var border = "+" + new string('-', width) + "+";
            lines = [border, .. lines.Select(line => "| " + line.PadRight(width - 1) + "|"), border];
        }

        var text = string.Join('\n', lines) + "\n";
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GridTables };
        Assert.Equal(6, CountTables(MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(text, options))));
        Assert.Equal(3, CountTables(MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(text, options with { MaximumNestingDepth = 2 }))));

        static int CountTables(string html) => html.Split("<table>").Length - 1;
    }

    [Theory]
    [InlineData("[r]: /url\t\n\n[r]\n")]
    [InlineData("[r]: /url \"t\"\t \n\n[r]\n")]
    [InlineData("[r]:\t/url\t\n\n[r]\n")]
    public void LinkReferenceDefinitionsEndWithSpacesOrTabs(string text)
    {
        var tree = MarkdownSyntaxTree.ParseText(text);

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.StartsWith("<p><a href=\"/url\"", MarkdownHtmlRenderer.ToHtml(tree), StringComparison.Ordinal);
    }

    [Fact]
    public void GridTableCellEditsKeepTheDefinitionsOfTheTable()
    {
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GridTables };
        var tree = MarkdownSyntaxTree.ParseText("+---------+\n| [x][r]  |\n+---------+\n\n[r]: /url\n", options);
        var content = Assert.IsType<MarkdownGridTableSyntax>(tree.GetRoot().Blocks[0]).Rows[0].Cells[0].Content;
        Assert.Equal("<p><a href=\"/url\">x</a></p>\n", MarkdownHtmlRenderer.ToHtml(content));

        var inserted = content.InsertBlocksAfter(content.GetRoot().Blocks.Single(), "\nb");
        Assert.Equal("<p><a href=\"/url\">x</a></p>\n<p>b</p>\n", MarkdownHtmlRenderer.ToHtml(inserted));

        var changed = (MarkdownSyntaxTree)content.WithChangedText(SourceText.From("[y][r]"));
        Assert.Equal("<p><a href=\"/url\">y</a></p>\n", MarkdownHtmlRenderer.ToHtml(changed));

        var paragraph = MarkdownSyntaxTree.ParseText("[z][r]", options).GetRoot().Blocks.Single();
        Assert.Equal("<p><a href=\"/url\">z</a></p>\n", MarkdownHtmlRenderer.ToHtml((MarkdownSyntaxTree)content.WithRoot(paragraph)));
        Assert.Equal("<p><a href=\"/url\">x</a></p>\n", MarkdownHtmlRenderer.ToHtml((MarkdownSyntaxTree)content.WithRoot(content.GetRoot())));

        var formatted = MarkdownFormatter.Format(content);
        Assert.Equal("<p><a href=\"/url\">x</a></p>\n", MarkdownHtmlRenderer.ToHtml(formatted));
    }

    [Fact]
    public void GridTableCellsHaveNoFrontMatter()
    {
        var tree = MarkdownSyntaxTree.ParseText("+---------+\n| ---     |\n| secret  |\n| ---     |\n| visible |\n+---------+\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.GridTables | MarkdownExtensions.YamlFrontMatter });

        Assert.Contains("<td><hr />\n<h2>secret</h2>\n<p>visible</p>\n</td>", MarkdownHtmlRenderer.ToHtml(tree), StringComparison.Ordinal);
    }

    [Fact]
    public void GridTableCellSpansMapToTheTextOfTheTable()
    {
        #region MarkdownGridTableCell_MapSpan
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GridTables };
        var text = "Intro\n\n+---------------+\n| See [a](/u)   |\n| and [b](/v)   |\n+---------------+\n";
        var tree = MarkdownSyntaxTree.ParseText(text, options);
        var cell = tree.GetRoot().Blocks.OfType<MarkdownGridTableSyntax>().Single().Rows[0].Cells[0];

        // The nodes of the content of the cell are not in the tree, and their spans are in the text of the cell
        Assert.Empty(tree.GetRoot().DescendantNodes().OfType<MarkdownLinkSyntax>());
        var link = cell.Content.GetRoot().DescendantNodes().OfType<MarkdownLinkSyntax>().Last();
        Assert.Equal("[b](/v)", cell.Text.Substring(link.Span.Start, link.Span.Length));

        // MapSpan gives the span of the link in the text of the tree
        var span = cell.MapSpan(link.Span);
        Assert.Equal("[b](/v)", text.Substring(span.Start, span.Length));
        #endregion

        Assert.Equal(cell.Text.Length, cell.Content.GetText().Length);
        Assert.Equal(text.IndexOf("| See", StringComparison.Ordinal) + 1, cell.MapPosition(0));
        Assert.Equal(text.IndexOf("   |\n| and", StringComparison.Ordinal), cell.MapPosition(cell.Text.IndexOf('\n', StringComparison.Ordinal)));
        Assert.Equal(text.IndexOf("| and", StringComparison.Ordinal) + 1, cell.MapPosition(cell.Text.IndexOf('\n', StringComparison.Ordinal) + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => cell.MapPosition(cell.Text.Length + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => cell.MapPosition(-1));
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
    [InlineData("+---+\n| a |\n+===+\n| b |\n+===+\n| c |\n+---+\n", "<table>\n<col style=\"width:100%\" />\n<thead>\n<tr>\n<th>a</th>\n</tr>\n<tr>\n<th>b</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>c</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("+---+---+---+\n| a\n+---+---+---+\n| b | c\n+---+---+---+\n", "<table>\n<col style=\"width:33.33%\" />\n<col style=\"width:33.33%\" />\n<col style=\"width:33.33%\" />\n<tbody>\n<tr>\n<td colspan=\"3\">a</td>\n</tr>\n<tr>\n<td>b</td>\n<td colspan=\"2\">c</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("+---+---+---+\n| a |\n| b |\n+---+\n", "<p>+---+---+---+\n| a |\n| b |\n+---+</p>\n")]
    public void RendersGridTables(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.GridTables });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Theory]
    [InlineData("clip.MOV", "<p><video width=\"500\" height=\"281\" controls=\"\"><source type=\"video/quicktime\" src=\"clip.MOV\"></source></video></p>\n")]
    [InlineData("https://example.com/a/song.m4a?x=1", "<p><audio width=\"500\" controls=\"\"><source type=\"audio/mp4\" src=\"https://example.com/a/song.m4a?x=1\"></source></audio></p>\n")]
    [InlineData("voice.opus", "<p><audio width=\"500\" controls=\"\"><source type=\"audio/ogg\" src=\"voice.opus\"></source></audio></p>\n")]
    [InlineData("old.avi", "<p><img src=\"old.avi\" alt=\"\" /></p>\n")]
    [InlineData("photo.avif", "<p><img src=\"photo.avif\" alt=\"\" /></p>\n")]
    [InlineData("noextension", "<p><img src=\"noextension\" alt=\"\" /></p>\n")]
    public void MediaLinksRenderTheFormatsThatBrowsersPlay(string destination, string html)
    {
        var tree = MarkdownSyntaxTree.ParseText($"![]({destination})\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.MediaLinks });

        Assert.Equal(html, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Fact]
    public void MediaLinksUseTheExtensionsOfTheOptions()
    {
        var tree = MarkdownSyntaxTree.ParseText("![](old.avi) ![](song.mp3)\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.MediaLinks });
        var options = new MarkdownHtmlRendererOptions { MediaLinks = MarkdownMediaLinkOptions.Default with { ExtensionToMimeType = new Dictionary<string, string>(StringComparer.Ordinal) { [".avi"] = "video/x-msvideo" } } };

        Assert.Equal("<p><video width=\"500\" height=\"281\" controls=\"\"><source type=\"video/x-msvideo\" src=\"old.avi\"></source></video> <img src=\"song.mp3\" alt=\"\" /></p>\n", MarkdownHtmlRenderer.ToHtml(tree, options));
    }

    [Theory]
    [InlineData("https://www.youtube.com/shorts/6BUptHVuvyI", "https://www.youtube.com/embed/6BUptHVuvyI", "youtubeshort")]
    [InlineData("https://www.youtube.com/watch/?v=a&t=5&v=b", "https://www.youtube.com/embed/a?start=5", "youtube")]
    [InlineData("https://m.www.youtube.com/WATCH?v=a", "https://www.youtube.com/embed/a", "youtube")]
    [InlineData("https://www.youtube.com/embed/a?start=1", "https://www.youtube.com/embed/a?start=1", "youtube")]
    [InlineData("https://youtu.be/a?t=1", "https://www.youtube.com/embed/a?start=1", "youtube")]
    [InlineData("https://vimeo.com/channels/staff/8607834", "https://player.vimeo.com/video/8607834", "vimeo")]
    [InlineData("https://ok.ru/video/1", "https://ok.ru/videoembed/1", "odnoklassniki")]
    [InlineData("https://music.yandex.ru/album/1/track/2/extra", "https://music.yandex.ru/iframe/#track/2/1/", "yandex")]
    [InlineData("https://www.youtube.com/watch?t=5", null, null)]
    [InlineData("https://www.youtube.com/watchlist?v=a", null, null)]
    [InlineData("https://www.youtube.com.example.org/watch?v=a", null, null)]
    [InlineData("https://youtu.be/", null, null)]
    [InlineData("https://vimeo.com/", null, null)]
    [InlineData("https://music.yandex.ru/album/1/track/", null, null)]
    public void MediaHostsMakeTheUrlsOfTheirIframes(string url, string? iframeUrl, string? iframeClass)
    {
        var host = IMarkdownMediaHost.KnownHosts.FirstOrDefault(host => host.TryHandle(new Uri(url), isSchemaRelative: false, out _));
        string? actual = null;
        _ = host?.TryHandle(new Uri(url), isSchemaRelative: false, out actual);

        Assert.Equal(iframeUrl, actual);
        Assert.Equal(iframeClass, host?.Class);
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
    public void EscapesTheRelOfLinks()
    {
        var options = new MarkdownHtmlRendererOptions { LinkRel = "a\" onclick=\"alert(1)" };

        Assert.Equal("<p><a href=\"u\" rel=\"a&quot; onclick=&quot;alert(1)\">x</a></p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("[x](u)\n"), options));
    }

    // The rel of the options and the target of the autolinks are the ones of the renderer: generic attributes cannot write them again
    [Theory]
    [InlineData("[x](u){target=_blank rel=opener}\n", "<p><a href=\"u\" target=\"_blank\" rel=\"noopener noreferrer\">x</a></p>\n")]
    [InlineData("<https://a.b>{REL=opener}\n", "<p><a href=\"https://a.b\" rel=\"noopener noreferrer\">https://a.b</a></p>\n")]
    [InlineData("<a@b.c>{rel=me}\n", "<p><a href=\"mailto:a@b.c\" rel=\"me\">a@b.c</a></p>\n")]
    public void GenericAttributesDoNotReplaceTheAttributesOfTheRenderer(string text, string expected)
    {
        var parseOptions = new MarkdownParseOptions { Extensions = MarkdownExtensions.GenericAttributes | MarkdownExtensions.MarkdigAutolinks, AutolinkOptions = new MarkdownAutolinkOptions { OpenInNewWindow = true } };
        var options = new MarkdownHtmlRendererOptions { LinkRel = "noopener noreferrer" };

        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(text, parseOptions), options));
    }

    [Fact]
    public void FiltersTheSchemesOfTheUrls()
    {
        #region MarkdownHtmlRendererOptions_AllowedUrlSchemes
        var tree = MarkdownSyntaxTree.ParseText("[a](javascript:alert(1)) [b](https://example.com) [c](/relative) ![d](data:image/png;base64,AA==) <vbscript:x>\n");
        var options = new MarkdownHtmlRendererOptions { AllowedUrlSchemes = ["http", "https", "mailto"] };

        // The URLs whose scheme is not allowed are written empty, the relative URLs are kept
        Assert.Equal("<p><a href=\"\">a</a> <a href=\"https://example.com\">b</a> <a href=\"/relative\">c</a> <img src=\"\" alt=\"d\" /> <a href=\"\">vbscript:x</a></p>\n", MarkdownHtmlRenderer.ToHtml(tree, options));
        #endregion
    }

    [Theory]
    [InlineData("[a](JavaScript:x)\n", "<p><a href=\"\">a</a></p>\n")]
    [InlineData("[a](<java\tscript:x>)\n", "<p><a href=\"java%09script:x\">a</a></p>\n")]
    [InlineData("[a](HTTPS://a.b)\n", "<p><a href=\"HTTPS://a.b\">a</a></p>\n")]
    [InlineData("[a](./javascript:x) [b](#javascript:x) [c](?javascript:x) [d](//a.b/c)\n", "<p><a href=\"./javascript:x\">a</a> <a href=\"#javascript:x\">b</a> <a href=\"?javascript:x\">c</a> <a href=\"//a.b/c\">d</a></p>\n")]
    [InlineData("[a](c:/x) [b](1a:x)\n", "<p><a href=\"\">a</a> <a href=\"1a:x\">b</a></p>\n")]
    [InlineData("<mailto:a@b.c> <a@b.c>\n", "<p><a href=\"mailto:a@b.c\">mailto:a@b.c</a> <a href=\"mailto:a@b.c\">a@b.c</a></p>\n")]
    public void FiltersTheSchemesOfTheUrlsAsBrowsersReadThem(string text, string expected)
    {
        var options = new MarkdownHtmlRendererOptions { AllowedUrlSchemes = ["https", "mailto"] };

        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(text), options));
    }

    [Theory]
    [InlineData("javascript:x", "javascript")]
    [InlineData(" \u0001javascript:x", "javascript")]
    [InlineData("java\tscr\nipt\r:x", "javascript")]
    [InlineData("a+b-c.d:x", "a+b-c.d")]
    [InlineData("/a:b", null)]
    [InlineData("1a:b", null)]
    [InlineData(":b", null)]
    [InlineData("a b:c", null)]
    [InlineData("a", null)]
    [InlineData("", null)]
    public void ReadsTheSchemeOfAUrlAsBrowsersReadIt(string url, string? expected)
    {
        Assert.Equal(expected, MarkdownHtmlRenderer.Writer.TryGetUrlScheme(url, out var scheme) ? scheme.ToString() : null);
    }

    [Fact]
    public void FiltersTheUrlsOfMediaLinks()
    {
        var tree = MarkdownSyntaxTree.ParseText("![](https://youtu.be/x) ![](javascript:a.mp4)\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.MediaLinks });
        var options = new MarkdownHtmlRendererOptions { AllowedUrlSchemes = ["http"] };

        Assert.Equal("<p><iframe src=\"\" class=\"youtube\" width=\"500\" height=\"281\" frameborder=\"0\" allowfullscreen=\"\"></iframe> <video width=\"500\" height=\"281\" controls=\"\"><source type=\"video/mp4\" src=\"\"></source></video></p>\n", MarkdownHtmlRenderer.ToHtml(tree, options));
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
    public void WritesTheIdPrefix()
    {
        #region MarkdownHtmlRendererOptions_IdPrefix
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.AutoIdentifiers | MarkdownExtensions.Footnotes };
        var tree = MarkdownSyntaxTree.ParseText("# Notes\n\nSee[^1].\n\n[^1]: A note.\n", options);

        // Several documents rendered on one page, such as comments, have different ids
        var html = MarkdownHtmlRenderer.ToHtml(tree, new MarkdownHtmlRendererOptions { IdPrefix = "comment-1-" });
        Assert.Equal("<h1 id=\"comment-1-notes\">Notes</h1>\n<p>See<a id=\"comment-1-fnref:1\" href=\"#comment-1-fn:1\" class=\"footnote-ref\"><sup>1</sup></a>.</p>\n<div class=\"footnotes\">\n<hr />\n<ol>\n<li id=\"comment-1-fn:1\">\n<p>A note.<a href=\"#comment-1-fnref:1\" class=\"footnote-back-ref\">&#8617;</a></p>\n</li>\n</ol>\n</div>\n", html);
        #endregion
    }

    [Theory]
    [InlineData("# Title {#custom}\n\n[Title]\n\n[x](#custom)\n", "<h1 id=\"p&quot;-custom\"><a id=\"pragma-line-0\"></a>Title</h1>\n<p id=\"pragma-line-2\"><a href=\"#p%22-custom\">Title</a></p>\n<p id=\"pragma-line-4\"><a href=\"#custom\">x</a></p>\n")]
    [InlineData("# Title\n\n[Title]\n\nText {#t}\n", "<h1 id=\"p&quot;-title\"><a id=\"pragma-line-0\"></a>Title</h1>\n<p id=\"pragma-line-2\"><a href=\"#p%22-title\">Title</a></p>\n<a id=\"pragma-line-4\"></a>\n<p id=\"p&quot;-t\">Text</p>\n")]
    [InlineData("x[^1]\n\n[^1]: note\n", "<p id=\"pragma-line-0\">x<a id=\"p&quot;-fnref:1\" href=\"#p&quot;-fn:1\" class=\"footnote-ref\"><sup>1</sup></a></p>\n<div class=\"footnotes\">\n<hr />\n<ol>\n<li id=\"p&quot;-fn:1\">\n<p id=\"pragma-line-2\">note<a href=\"#p&quot;-fnref:1\" class=\"footnote-back-ref\">&#8617;</a></p>\n</li>\n</ol>\n</div>\n")]
    public void WritesTheIdPrefixOfTheIdsThatTheDocumentSets(string markdown, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(markdown, new MarkdownParseOptions { Extensions = MarkdownExtensions.AutoIdentifiers | MarkdownExtensions.Footnotes | MarkdownExtensions.GenericAttributes });

        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree, new MarkdownHtmlRendererOptions { PragmaLines = true, IdPrefix = "p\"-" }));
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

    // Finding whether a bracket is closed was quadratic in the number of brackets: 160 KB of [ took 40 seconds
    [Fact]
    public void TheDirectionOfManyBracketsIsLinear()
    {
        var tree = MarkdownSyntaxTree.ParseText(new string('[', 200_000) + "مرحبا", new MarkdownParseOptions { Extensions = MarkdownExtensions.Globalization });

        var stopwatch = Stopwatch.StartNew();
        var html = MarkdownHtmlRenderer.ToHtml(tree);

        Assert.StartsWith("<p dir=\"rtl\">[[", html, StringComparison.Ordinal);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Rendering took {stopwatch.Elapsed}");
    }

    [Fact]
    public void TheAdvancedExtensionsAreTheOnesOfMarkdig()
    {
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Advanced };
        var tree = MarkdownSyntaxTree.ParseText("| a |\n|---|\n| www.example.com |\n\n+---+\n| b |\n+---+\n", options);

        Assert.Equal("<table>\n<thead>\n<tr>\n<th>a</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td><a href=\"http://www.example.com\">www.example.com</a></td>\n</tr>\n</tbody>\n</table>\n<table>\n<col style=\"width:100%\" />\n<tbody>\n<tr>\n<td>b</td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Fact]
    public void FormatsADocument()
    {
        #region MarkdownFormatter_Usage
        var tree = MarkdownSyntaxTree.ParseText("""
            Title
            =====
            * one
            *   two

            3) a
            7) b

            Code:

                var x = 1;

            > quote
            continued

            | a | b |
            |-|:-:|
            | long cell | x |
            """, MarkdownParseOptions.GitHubFlavored);

        var formatted = MarkdownFormatter.Format(tree);

        Assert.Equal("""
            # Title

            - one
            - two

            3) a
            4) b

            Code:

            ```
            var x = 1;
            ```

            > quote
            > continued

            | a         | b   |
            | --------- | :-: |
            | long cell | x   |

            """, formatted.GetRoot().ToFullString());
        Assert.Equal(MarkdownHtmlRenderer.ToHtml(tree), MarkdownHtmlRenderer.ToHtml(formatted));
        #endregion
    }

    [Fact]
    public void FormatsADocumentWithTheOptions()
    {
        #region MarkdownFormattingOptions_Usage
        var tree = MarkdownSyntaxTree.ParseText("Title\n=====\n\n- a\n- b\n\n```cs\nvar x = 1;\n```\n\n***\n");
        var options = new MarkdownFormattingOptions
        {
            NewLine = "\r\n",
            BulletListMarker = '*',
            UseAtxHeadings = false,
            UseFencedCodeBlocks = false,
            CodeFenceCharacter = '~',
            ThematicBreakCharacter = '_',
        };

        var text = MarkdownFormatter.FormatText(tree, options);

        Assert.Equal("Title\r\n===\r\n\r\n* a\r\n* b\r\n\r\n~~~cs\r\nvar x = 1;\r\n~~~\r\n\r\n___\r\n", text);
        #endregion
    }

    [Theory]
    [InlineData("a\n    # b\n", "a\n    # b\n")]
    [InlineData("> a\n    # b\n", "> a\n>     # b\n")]
    [InlineData("> a\n===\n", "> a\n>     ===\n")]
    [InlineData("> a\nb\n===\n", "> a\n> b\n>     ===\n")]
    [InlineData("a\nb\n===\n", "a\nb\n===\n")]
    [InlineData("- a\n* b\n", "- a\n\n* b\n")]
    [InlineData("* --\n", "* --\n")]
    [InlineData("* a\n  ***\n", "- a\n  ***\n")]
    [InlineData("a\n***\n", "a\n\n---\n")]
    [InlineData("# a #\n", "# a\n")]
    [InlineData("# a \\# #\n", "# a \\# #\n")]
    [InlineData("a\nb\n---\n", "a\nb\n---\n")]
    [InlineData("````\n```\n````\n", "````\n```\n````\n")]
    [InlineData("~~~ a`b\nx\n~~~\n", "~~~a`b\nx\n~~~\n")]
    [InlineData("~~~ ~a\nx\n~~~\n", "```~a\nx\n```\n")]
    [InlineData("~~~ `a\nx\n~~~\n", "~~~`a\nx\n~~~\n")]
    [InlineData(" -    one\n\n     two\n", "- one\n\n```\n two\n```\n")]
    [InlineData("a  \nb\\\nc\n", "a  \nb\\\nc\n")]
    [InlineData("[a]: /a\n[b]: /b\n", "[a]: /a\n[b]: /b\n")]
    [InlineData("", "")]
    [InlineData("a\r\nb\r\n", "a\r\nb\r\n")]
    public void FormatsTheBlocks(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text);

        var formatted = MarkdownFormatter.FormatText(tree, options: null);

        Assert.Equal(expected, formatted);
        Assert.Equal(MarkdownHtmlRenderer.ToHtml(tree), MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(formatted)));
    }

    [Fact]
    public void FormatterParsesTheFirstBlockOfTheItemsOfNestedLists()
    {
        // The marker of a list is checked with the first block of each item, not with the lists nested in it
        var calls = 0;
        var options = new MarkdownParseOptions { SyntaxExtensions = [new ParseHandlerExtension("count", "\u00A7", (ref MarkdownInlineParseContext context) =>
        {
            calls++;
            return false;
        })] };
        var text = string.Concat(Enumerable.Range(0, 50).Select(i => new string(' ', 2 * i) + "- \u00A7\n"));
        var tree = MarkdownSyntaxTree.ParseText(text, options);
        calls = 0;

        var formatted = MarkdownFormatter.FormatText(tree, options: null);

        Assert.Equal(text, formatted);
        Assert.InRange(calls, 0, 500, "The formatter parses the trigger characters of the text a few times each");
    }

    [Fact]
    public void FormatterAlignsTheTablesWithTheDisplayWidthOfTheCells()
    {
        // A wide character, an emoji and a flag use two columns of a monospace editor, and a combining mark none
        var tree = MarkdownSyntaxTree.ParseText("| a | b |\n|-|-|\n| \u6F22\u5B57 | x |\n| e\u0301 | \U0001F600 |\n| \U0001F1EB\U0001F1F7 | \u2764\uFE0F |\n| a\u200Bb | \uFF21 |\n", MarkdownParseOptions.GitHubFlavored);

        var formatted = MarkdownFormatter.FormatText(tree, options: null);

        Assert.Equal("| a    | b   |\n| ---- | --- |\n| \u6F22\u5B57 | x   |\n| e\u0301    | \U0001F600  |\n| \U0001F1EB\U0001F1F7   | \u2764\uFE0F  |\n| a\u200Bb   | \uFF21  |\n", formatted);
        Assert.Equal(MarkdownHtmlRenderer.ToHtml(tree), MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(formatted, MarkdownParseOptions.GitHubFlavored)));
    }

    [Theory]
    [InlineData("", "", true)]
    [InlineData("a\nb", "a\nb", true)]
    [InlineData("a\r\nb", "a\nb", true)]
    [InlineData("a\rb", "a\r\nb", true)]
    [InlineData("a\r\r\nb", "a\n\nb", true)]
    [InlineData("a\r\n", "a\r", true)]
    [InlineData("\r\n", "\r\r\n", false)]
    [InlineData("a\r\n", "a\n\n", false)]
    [InlineData("a\n", "a", false)]
    [InlineData("a\nb", "a b", false)]
    [InlineData("ab", "a", false)]
    public void ComparesTheHtmlIgnoringLineEndings(string text, string other, bool expected)
    {
        Assert.Equal(expected, MarkdownHtmlRenderer.EqualsIgnoringLineEndings(text, other));
        Assert.Equal(expected, MarkdownHtmlRenderer.EqualsIgnoringLineEndings(other, text));
        Assert.Equal(expected, text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n') == other.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
    }

    [Theory]
    [InlineData("a\nb", "", true)]
    [InlineData("`a`\n*b*", "", true)]
    [InlineData("a\n===", "", false)]
    [InlineData("a\n- b", "", false)]
    [InlineData("a\n> b", "", false)]
    [InlineData("[a]: /url\nb", "", false)]
    [InlineData("[a]: /url", "", false)]
    [InlineData("", "", false)]
    [InlineData("a\n\nb", "", false)]
    [InlineData("a | b\n- | -", "", false)]
    [InlineData("a | b\n--- | ---", "gfm", false)]
    [InlineData("a | b", "gfm", true)]
    [InlineData("a | b\n- | -", "advanced", null)]
    [InlineData("a\nb", "advanced", true)]
    [InlineData("a\n:   b", "advanced", false)]
    [InlineData("a\n: b", "advanced", true)]
    [InlineData("---\na: b\n---", "advanced", false)]
    public void FindsASingleParagraphFromTheBlocks(string text, string options, bool? expected)
    {
        var parseOptions = options switch
        {
            "gfm" => MarkdownParseOptions.GitHubFlavored,
            "advanced" => new MarkdownParseOptions { Extensions = MarkdownExtensions.Advanced },
            _ => MarkdownParseOptions.Default,
        };

        var single = Parsing.BlockParser.IsSingleParagraph(text, parseOptions);

        Assert.Equal(expected, single);
        if (single is not null)
        {
            Assert.Equal(single, MarkdownSyntaxTree.ParseText(text, parseOptions).GetRoot().Blocks is [MarkdownParagraphSyntax]);
        }
    }

    [Theory]
    [InlineData("a *b* c\nd **e**\nf")]
    [InlineData("*a*\n_b_ c")]
    [InlineData("www.example.com\nwww.example.com")]
    [InlineData("$x$ and $y$\n$z$")]
    [InlineData("# a\n{g}\n\"|{.c}|-|\n|:-|")]
    [InlineData("{.c} a | b\n- | -\n{.d} c | d")]
    [InlineData("x\n\ny\n\n| a {.c} | b |\n| - {.e} | -: |\n| c | d {.f} |\n{.g}\n\n{.h} a | b\n{.i} - | -\nc | d")]
    [InlineData("# a {#b}\nc {.d}\n")]
    [InlineData("a\n\n  b\n  c\n\n> d\n> e")]
    [InlineData("<a@b.c>\na@b.c\n:smile: x")]
    public void LineEndingsDoNotChangeTheTree(string text)
    {
        // The inlines of the lines that follow each other with \n are read in the text itself, and the others in a copy
        var options = new MarkdownParseOptions { Extensions = Enum.GetValues<MarkdownExtensions>().Aggregate(MarkdownExtensions.None, (all, extension) => all | extension) };
        foreach (var parseOptions in new[] { MarkdownParseOptions.Default, MarkdownParseOptions.GitHubFlavored, options })
        {
            var lfTree = MarkdownSyntaxTree.ParseText(text, parseOptions);
            var crlfTree = MarkdownSyntaxTree.ParseText(text.Replace("\n", "\r\n", StringComparison.Ordinal), parseOptions);

            Assert.Equal(MarkdownHtmlRenderer.ToHtml(lfTree), MarkdownHtmlRenderer.ToHtml(crlfTree).Replace("\r\n", "\n", StringComparison.Ordinal));
            Assert.Equal(GetKindsAndValues(lfTree), GetKindsAndValues(crlfTree));
        }

        static List<string> GetKindsAndValues(MarkdownSyntaxTree tree) => [.. tree.GetRoot().DescendantTokens().Select(token => $"{token.RawKind} {(token.Value is int value ? value : null)}")];
    }

    [Fact]
    public void RepeatedTokensKeepTheirPositionsAndTrivia()
    {
        // The green tokens of the same kind and text are shared, in a tree and between trees, and the ones with trivia are not
        const string Text = "```cs\ncode\n```\n\n  ```cs\n  code\n  ```\n\n**bold** and **bold**\n";

        foreach (var tree in new[] { MarkdownSyntaxTree.ParseText(Text), MarkdownSyntaxTree.ParseText(Text) })
        {
            Assert.Equal(Text, tree.GetRoot().ToFullString());
            var position = 0;
            foreach (var token in tree.GetRoot().DescendantTokens())
            {
                Assert.Equal(position, token.FullSpan.Start);
                Assert.Equal(Text.Substring(token.FullSpan.Start, token.FullSpan.Length), token.ToFullString());
                position = token.FullSpan.End;
            }

            Assert.Equal(Text.Length, position);
            Assert.Equal(["```", "```", "```", "```"], tree.GetRoot().DescendantTokens().Where(token => token.RawKind == (int)SyntaxKind.CodeFenceToken).Select(token => token.Text));
        }
    }

    [Theory]
    [InlineData("> \n>\f\n``<|<ba", "> \f\n> ``<|<ba\n")]
    [InlineData("aaa\n\n\nbb\n>\u001D\n> bar>\n\f|-|", "aaa\n\nbb\n\n> \u001D\n> bar>\n> \f|-|\n")]
    [InlineData("- foo\n\n\u0012      bar\n a\n  > b\n\f </style> >||\n*", "- foo\n\n\u0012      bar\na\n\n> b\n> \f </style> >||\n\n-\n")]
    public void FormatterKeepsTheWhitespaceOtherThanSpacesInBlockQuotes(string text, string expected)
    {
        // A form feed is whitespace around the cells of a pipe table: on the line of the marker of a quote, or after its space
        var options = new MarkdownParseOptions
        {
            Extensions = Enum.GetValues<MarkdownExtensions>().Aggregate(MarkdownExtensions.None, (all, extension) => all | extension),
            PipeTableOptions = new MarkdownPipeTableOptions { RequireHeaderSeparator = false, UseHeaderForColumnCount = true, InferColumnWidthsFromSeparator = true },
        };
        var tree = MarkdownSyntaxTree.ParseText(text, options);
        var formatted = MarkdownFormatter.Format(tree);

        Assert.Equal(expected, formatted.GetRoot().ToFullString());
        Assert.Equal(expected, MarkdownFormatter.FormatText(formatted, MarkdownFormattingOptions.Default));
        Assert.Equal(MarkdownHtmlRenderer.ToHtml(tree), MarkdownHtmlRenderer.ToHtml(formatted));
    }

    [Theory]
    [InlineData("[^1]: 1.  \n   <!-- --> - L    bar", "[^1]:\n    1.\n\n       <!-- --> - L    bar\n")]
    [InlineData("a[^1]\n\n[^1]: 1.  \n   <!-- --> b\n", "a[^1]\n\n[^1]:\n    1.\n\n       <!-- --> b\n")]
    public void FormatterKeepsTheBlockAfterAnEmptyListItemOutOfIt(string text, string expected)
    {
        // The content of an empty item starts after its marker and one space: the HTML block, indented as much, would be its content
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Footnotes };
        var tree = MarkdownSyntaxTree.ParseText(text, options);
        var formatted = MarkdownFormatter.Format(tree);
        var footnote = formatted.GetRoot().Blocks.OfType<MarkdownFootnoteDefinitionSyntax>().Single();

        Assert.Equal(expected, formatted.GetRoot().ToFullString());
        Assert.Equal(expected, MarkdownFormatter.FormatText(formatted, MarkdownFormattingOptions.Default));
        Assert.Equal(MarkdownHtmlRenderer.ToHtml(tree), MarkdownHtmlRenderer.ToHtml(formatted));
        Assert.IsType<MarkdownHtmlBlockSyntax>(footnote.Blocks[^1]);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void FormatterKeepsTheLinesOfAParagraphWhateverItsLineEndings(string newLine)
    {
        // Alone, the lines are a pipe table, as [ref] is not a link without its definition: they stay as they are in the document
        var options = new MarkdownParseOptions
        {
            Extensions = MarkdownExtensions.PipeTables | MarkdownExtensions.GenericAttributes,
            PipeTableOptions = new MarkdownPipeTableOptions { RequireHeaderSeparator = false },
        };
        var text = $"a *[{newLine}|{{.c}}foo*][ref]{newLine}{newLine}[ref]: /url{newLine}";
        var tree = MarkdownSyntaxTree.ParseText(text, options);

        Assert.Equal(text, MarkdownFormatter.FormatText(tree, new MarkdownFormattingOptions { NewLine = newLine }));
    }

    [Fact]
    public void FormatterReturnsTheTreeOfAFormattedDocument()
    {
        var text = "# Title\n\nSome text\non two lines.\n\n- a\n- b\n";
        var tree = MarkdownSyntaxTree.ParseText(text);

        Assert.Same(tree, MarkdownFormatter.Format(tree));
        Assert.Equal(text, MarkdownFormatter.FormatText(tree, options: null));
    }

    [Fact]
    public void FormatterKeepsTheTextWhenTheHtmlWouldChange()
    {
        // The lazy line |-|-| of the paragraph of the list item would not be lazy with the indentation of the item, and the
        // table after it would be text
        var text = "* a\n|-|-|\n  b\n  |H|I|\n  |-|-|\n";
        var tree = MarkdownSyntaxTree.ParseText(text, MarkdownParseOptions.GitHubFlavored);

        Assert.Equal(text, MarkdownFormatter.FormatText(tree, options: null));
        Assert.Same(tree, MarkdownFormatter.Format(tree));
    }

    [Theory]
    [InlineData('x', '`', '-', "\n")]
    [InlineData('-', 'x', '-', "\n")]
    [InlineData('-', '`', '=', "\n")]
    [InlineData('-', '`', '-', "\n\n")]
    public void FormatterRejectsInvalidOptions(char bulletListMarker, char codeFenceCharacter, char thematicBreakCharacter, string newLine)
    {
        var options = new MarkdownFormattingOptions { BulletListMarker = bulletListMarker, CodeFenceCharacter = codeFenceCharacter, ThematicBreakCharacter = thematicBreakCharacter, NewLine = newLine };

        Assert.Throws<ArgumentException>(() => MarkdownFormatter.Format(MarkdownSyntaxTree.ParseText("a"), options));
    }

    [Fact]
    public void ConvertsText()
    {
        #region MarkdownConverter_Usage
        Assert.Equal("<p>Some <strong>bold</strong> text</p>\n", MarkdownConverter.ToHtml("Some **bold** text"));
        Assert.Equal("<p><del>Old</del> new</p>\n", MarkdownConverter.ToHtml("~~Old~~ new", MarkdownParseOptions.GitHubFlavored));
        Assert.Equal("Some bold text\n", MarkdownConverter.ToPlainText("Some **bold** text"));
        #endregion
    }

    [Fact]
    public void RendersPlainText()
    {
        #region MarkdownPlainTextRenderer_Usage
        var tree = MarkdownSyntaxTree.ParseText("# Title\n\nSome **bold** text and [a link](https://example.com).\n\n- First\n- Second");

        Assert.Equal("Title\nSome bold text and a link.\nFirst\nSecond\n", MarkdownPlainTextRenderer.ToPlainText(tree));
        #endregion
    }

    [Fact]
    public void RendersNodesWithNodeRenderers()
    {
        #region MarkdownHtmlRenderer_NodeRenderers
        var tree = MarkdownSyntaxTree.ParseText("```chart\nbar: 1, 2, 3\n```\n\n```cs\nvar a = 1;\n```\n");
        var options = new MarkdownHtmlRendererOptions { NodeRenderers = [new ChartRenderer()] };

        // The chart is written by the node renderer, the other code block as usual
        Assert.Equal("<canvas class=\"chart\" data-spec=\"bar: 1, 2, 3\n\"></canvas>\n<pre><code class=\"language-cs\">var a = 1;\n</code></pre>\n", MarkdownHtmlRenderer.ToHtml(tree, options));

        // The plain text has the text of the chart, without its HTML
        Assert.Equal("bar: 1, 2, 3\nvar a = 1;\n", MarkdownPlainTextRenderer.ToPlainText(tree, options));
        #endregion
    }

    [Fact]
    public void RendersNodesWithTheDefaultRendering()
    {
        #region MarkdownHtmlRenderer_NodeRenderersDefault
        var parseOptions = new MarkdownParseOptions { Extensions = MarkdownExtensions.PipeTables | MarkdownExtensions.CustomContainers };
        var tree = MarkdownSyntaxTree.ParseText(":::details Show the prices\n| Fruit | Price |\n|---|---|\n| Apple | *1* |\n:::\n", parseOptions);
        var options = new MarkdownHtmlRendererOptions { NodeRenderers = [new DetailsRenderer(), new ScrollableTableRenderer()] };

        Assert.Equal("<details><summary>Show the prices</summary>\n<div class=\"table-scroll\">\n<table>\n<thead>\n<tr>\n<th>Fruit</th>\n<th>Price</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>Apple</td>\n<td><em>1</em></td>\n</tr>\n</tbody>\n</table>\n</div>\n</details>\n", MarkdownHtmlRenderer.ToHtml(tree, options));
        #endregion
    }

    [Theory]
    [InlineData("- [x] done\n- [ ] todo\n\n  more\n")]
    [InlineData("A[^1] and B[^1].\n\n[^1]: The note.\n\n    Its code.\n\n[^2]: Second.\n")]
    [InlineData("# Title {#id .c}\n\n{.d}\n> quote *a* **b** `c` <span>d</span> [e](f \"g\") ![h](i)\n\n1. one\n2. two\n\n| a | b |\n|---|---|\n| 1 | 2 |\n")]
    [InlineData("Term\n:   Definition\n\n:::note\ntext\n:::\n\n> [!NOTE]\n> Alert\n\n$$\nx\n$$\n\n<div>html</div>\n\n---\n")]
    [InlineData("+---+---+\n| a | *b* |\n+===+===+\n| c | d |\n+---+---+\n")]
    [InlineData(":::x\n[a]: /url\nb [a]\n:::\n\n:::\n[^1]: c\n:::\n")]
    [InlineData("^^ This is a footer\n^^ <title>multi-line")]
    public void NodeRenderersThatDeclineOrWriteTheDefaultRenderingChangeNothing(string text)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, AllExtensionsWithoutMarkdigTables);
        foreach (var options in (MarkdownHtmlRendererOptions[])[new(), new() { PragmaLines = true }])
        {
            var html = MarkdownHtmlRenderer.ToHtml(tree, options);
            var plainText = MarkdownPlainTextRenderer.ToPlainText(tree, options);
            foreach (var renderer in (MarkdownHtmlNodeRenderer[])[new DecliningRenderer(), new DefaultRenderer()])
            {
                var withRenderer = options with { NodeRenderers = [renderer] };
                Assert.Equal(html, MarkdownHtmlRenderer.ToHtml(tree, withRenderer));
                Assert.Equal(plainText, MarkdownPlainTextRenderer.ToPlainText(tree, withRenderer));
            }
        }
    }

    [Theory]
    [InlineData("+---+\n| a |\n+---+\n", "<table>\n<col style=\"width:100%\" />\n<tbody>\n<tr>\n<td><div class=\"p\">a</div>\n</td>\n</tr>\n</tbody>\n</table>\n")]
    [InlineData("Term\n:   a\n", "<dl>\n<dt>Term</dt>\n<dd>\n<div class=\"p\">a</div>\n</dd>\n</dl>\n")]
    public void NodeRenderersAreOfferedTheParagraphsOfCellsAndDefinitions(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.GridTables | MarkdownExtensions.DefinitionLists });
        var html = MarkdownHtmlRenderer.ToHtml(tree);
        foreach (var renderer in (MarkdownHtmlNodeRenderer[])[new DecliningRenderer(), new DefaultRenderer()])
        {
            Assert.Equal(html, MarkdownHtmlRenderer.ToHtml(tree, new MarkdownHtmlRendererOptions { NodeRenderers = [renderer] }));
        }

        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree, new MarkdownHtmlRendererOptions { NodeRenderers = [new ParagraphRenderer()] }));
    }

    [Fact]
    public void NodeRenderersThatWriteAfterTheDefaultRenderingEndTheirLine()
    {
        var tree = MarkdownSyntaxTree.ParseText("^^ a\n^^ <title>b\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.Footers });
        var options = new MarkdownHtmlRendererOptions { NodeRenderers = [new CommentAfterParagraphRenderer()] };

        Assert.Equal("<footer>a<!--x-->\n<title>b\n</footer>\n", MarkdownHtmlRenderer.ToHtml(tree, options));
    }

    [Fact]
    public void NodeRenderersAreOfferedEveryBlockAndInline()
    {
        var tree = MarkdownSyntaxTree.ParseText("# a *b*\n\n- c\n- d **e**\n\n> f `g`\n");
        var renderer = new DecliningRenderer();
        _ = MarkdownHtmlRenderer.ToHtml(tree, new MarkdownHtmlRendererOptions { NodeRenderers = [renderer] });

        Assert.Equal(["Heading", "Emphasis", "List", "Paragraph", "Paragraph", "Emphasis", "BlockQuote", "Paragraph", "CodeSpan"], renderer.Offered);
    }

    [Fact]
    public void NodeRenderersWriteTheCheckboxOfTaskListItemsBeforeTheParagraph()
    {
        var tree = MarkdownSyntaxTree.ParseText("- [x] a\n\n- [ ] b\n", MarkdownParseOptions.GitHubFlavored);
        var options = new MarkdownHtmlRendererOptions { NodeRenderers = [new ParagraphRenderer()] };

        Assert.Equal("<ul>\n<li>\n<input checked=\"\" disabled=\"\" type=\"checkbox\"> <div class=\"p\">a</div>\n</li>\n<li>\n<input disabled=\"\" type=\"checkbox\"> <div class=\"p\">b</div>\n</li>\n</ul>\n", MarkdownHtmlRenderer.ToHtml(tree, options));
    }

    [Fact]
    public void NodeRenderersWriteTheBackLinksOfFootnotesAfterTheLastParagraph()
    {
        var tree = MarkdownSyntaxTree.ParseText("A[^1].\n\n[^1]: Note.\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.Footnotes });
        var options = new MarkdownHtmlRendererOptions { NodeRenderers = [new ParagraphRenderer()] };

        Assert.Equal("<div class=\"p\">A<a id=\"fnref:1\" href=\"#fn:1\" class=\"footnote-ref\"><sup>1</sup></a>.</div>\n<div class=\"footnotes\">\n<hr />\n<ol>\n<li id=\"fn:1\">\n<div class=\"p\">Note.</div>\n<p><a href=\"#fnref:1\" class=\"footnote-back-ref\">&#8617;</a></p>\n</li>\n</ol>\n</div>\n", MarkdownHtmlRenderer.ToHtml(tree, options));
    }

    [Fact]
    public void NodeRenderersWriteUrlsAndAttributesAsTheRenderer()
    {
        var tree = MarkdownSyntaxTree.ParseText("Hi @octo-cat {.mention}\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.GenericAttributes });
        var options = new MarkdownHtmlRendererOptions
        {
            BaseUrl = new Uri("https://example.com/"),
            LinkRewriter = url => url + "?ref=md",
            NodeRenderers = [new MentionParagraphRenderer()],
        };

        Assert.Equal("<p class=\"text mention\">Hi <a href=\"https://example.com/users/octo-cat%C3%A9?ref=md\">@octo-cat&lt;&gt;</a></p>\n", MarkdownHtmlRenderer.ToHtml(tree, options));
        Assert.Equal("Hi @octo-cat<>\n", MarkdownPlainTextRenderer.ToPlainText(tree, options));
    }

    [Fact]
    public void ParsesInlineExtensions()
    {
        #region MarkdownSyntaxTree_InlineExtension
        var options = new MarkdownParseOptions { SyntaxExtensions = [new MentionExtension()] };
        var tree = MarkdownSyntaxTree.ParseText("Thanks @octo-cat, and *@hubot*!", options);
        var mention = tree.GetRoot().DescendantNodes().OfType<MarkdownExtensionInlineSyntax>().First();

        Assert.Equal("mention", mention.Name);
        Assert.Equal(["@"], mention.Markers.Select(marker => marker.Text));
        Assert.Equal("octo-cat", mention.Text);

        // By default, the text of the construct is written without its markers
        Assert.Equal("<p>Thanks octo-cat, and <em>hubot</em>!</p>\n", MarkdownHtmlRenderer.ToHtml(tree));

        // A node renderer writes other HTML
        var rendererOptions = new MarkdownHtmlRendererOptions { NodeRenderers = [new MentionRenderer()] };
        Assert.Equal("<p>Thanks <a class=\"mention\" href=\"/users/octo-cat\">@octo-cat</a>, and <em><a class=\"mention\" href=\"/users/hubot\">@hubot</a></em>!</p>\n", MarkdownHtmlRenderer.ToHtml(tree, rendererOptions));

        // An email address is not a mention: the extension reads the character before the @
        Assert.Equal("<p>Mail me@example.com</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("Mail me@example.com", options), rendererOptions));
        #endregion
    }

    [Fact]
    public void ParsesDelimiterExtensions()
    {
        #region MarkdownSyntaxTree_DelimiterExtension
        var options = new MarkdownParseOptions { SyntaxExtensions = [new MarkdownDelimiterExtension("highlight", '%')] };
        var tree = MarkdownSyntaxTree.ParseText("Some %%important *and* urgent%% text, *nested %%both* ways%%.", options);
        var highlight = tree.GetRoot().DescendantNodes().OfType<MarkdownExtensionInlineSyntax>().Single();

        Assert.Equal("highlight", highlight.Name);
        Assert.Equal(["%%", "%%"], highlight.Markers.Select(marker => marker.Text));

        // The runs take part in the emphasis: the second highlight is text, as the second == would be
        var rendererOptions = new MarkdownHtmlRendererOptions { NodeRenderers = [new HighlightRenderer()] };
        Assert.Equal("<p>Some <mark>important <em>and</em> urgent</mark> text, <em>nested %%both</em> ways%%.</p>\n", MarkdownHtmlRenderer.ToHtml(tree, rendererOptions));
        #endregion
    }

    [Theory]
    [InlineData("a ==b== c")]
    [InlineData("*a ==b* c==")]
    [InlineData("==a *b== c*")]
    [InlineData("===a=== ==b ==c== d==")]
    [InlineData("==a **b==** c=")]
    [InlineData("x==y==z and == a == and ==\nb==")]
    public void DelimiterExtensionsMatchAsTheBuiltInExtraEmphasis(string text)
    {
        var marked = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.Marked });
        var extension = MarkdownSyntaxTree.ParseText(text.Replace('=', '%'), new MarkdownParseOptions { SyntaxExtensions = [new MarkdownDelimiterExtension("marked", '%')] });
        var rendererOptions = new MarkdownHtmlRendererOptions { NodeRenderers = [new HighlightRenderer()] };

        Assert.Equal(MarkdownHtmlRenderer.ToHtml(marked), MarkdownHtmlRenderer.ToHtml(extension, rendererOptions).Replace('%', '='));
        Assert.Equal(text.Replace('=', '%'), extension.GetRoot().ToFullString());
    }

    [Fact]
    public void DelimiterExtensionsCanUseTheCharactersOfDisabledExtensions()
    {
        var options = new MarkdownParseOptions { SyntaxExtensions = [new MarkdownDelimiterExtension("sub", '~', minimumCount: 1, maximumCount: 1), new MarkdownDelimiterExtension("sup", '^', minimumCount: 1, maximumCount: 1), new MarkdownDelimiterExtension("quote", '"')] };
        var tree = MarkdownSyntaxTree.ParseText("H~2~O, x^2^ and \"\"a quote\"\"", options);

        Assert.Equal(["sub", "sup", "quote"], tree.GetRoot().DescendantNodes().OfType<MarkdownExtensionInlineSyntax>().Select(node => node.Name));
        Assert.Equal("<p>H2O, x2 and a quote</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Theory]
    [InlineData("a {{*b* [c](d)}} e", "<p>a <span><em>b</em> <a href=\"d\">c</a></span> e</p>\n")]
    [InlineData("[a {{b]}}(c)", "<p>[a <span>b]</span>(c)</p>\n")]
    [InlineData("*a {{b* c}}", "<p>*a <span>b* c</span></p>\n")]
    [InlineData("[a {{b}} c](d)", "<p><a href=\"d\">a <span>b</span> c</a></p>\n")]
    [InlineData("{{a {{b}} c}}", "<p><span>a <span>b</span> c</span></p>\n")]
    [InlineData("{{a", "<p>{{a</p>\n")]
    [InlineData("`{{a}}` \\{{b}}", "<p><code>{{a}}</code> {{b}}</p>\n")]
    [InlineData("> {{a\n> *b*}} c", "<blockquote>\n<p><span>a\n<em>b</em></span> c</p>\n</blockquote>\n")]
    public void InlineExtensionsParseTheirContentOnItsOwn(string text, string html)
    {
        var options = new MarkdownParseOptions { SyntaxExtensions = [new SpanExtension()] };
        var tree = MarkdownSyntaxTree.ParseText(text, options);

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(html, MarkdownHtmlRenderer.ToHtml(tree, new MarkdownHtmlRendererOptions { NodeRenderers = [new SpanRenderer()] }));
    }

    [Fact]
    public void InlineExtensionsAreTriedBeforeTheBuiltInSyntaxes()
    {
        var options = new MarkdownParseOptions { SyntaxExtensions = [new WikiLinkExtension()] };
        var tree = MarkdownSyntaxTree.ParseText("[[Home page]] and [a link](b)", options);

        Assert.Equal("<p>Home page and <a href=\"b\">a link</a></p>\n", MarkdownHtmlRenderer.ToHtml(tree));
        Assert.Equal("Home page and a link\n", MarkdownPlainTextRenderer.ToPlainText(tree));
        Assert.Equal("Home page", tree.GetRoot().DescendantNodes().OfType<MarkdownExtensionInlineSyntax>().Single().GetPlainText());
    }

    [Fact]
    public void InlineExtensionsKeepTheirTextOnSeveralLines()
    {
        var options = new MarkdownParseOptions { SyntaxExtensions = [new WikiLinkExtension()] };
        var tree = MarkdownSyntaxTree.ParseText("- [[Home\n  page]] x\n", options);
        var link = tree.GetRoot().DescendantNodes().OfType<MarkdownExtensionInlineSyntax>().Single();

        Assert.Equal("Home\npage", link.Text);
        Assert.Equal(["Home", "page"], link.Texts.Select(token => token.Text));
        Assert.Equal("- [[Home\n  page]] x\n", tree.GetRoot().ToFullString());
    }

    [Fact]
    public void InlineExtensionsHaveAHeadingIdentifier()
    {
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.AutoIdentifiers, SyntaxExtensions = [new WikiLinkExtension()] };

        Assert.Equal("<h1 id=\"see-home\">See Home</h1>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("# See [[Home]]", options)));
    }

    [Fact]
    public void InlineExtensionsNestedDeeplyAreText()
    {
        var text = new string('{', 200) + "a" + new string('}', 200);
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { SyntaxExtensions = [new SpanExtension()] });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.HasCount(33, tree.GetRoot().DescendantNodes().OfType<MarkdownExtensionInlineSyntax>());
    }

    [Theory]
    [InlineData(">>>> a", "<blockquote>\n<blockquote>\n<blockquote>\n<p>&gt; a</p>\n</blockquote>\n</blockquote>\n</blockquote>\n")]
    [InlineData("- - - a", "<ul>\n<li>\n<ul>\n<li>- a</li>\n</ul>\n</li>\n</ul>\n")]
    [InlineData("*a *b *c *d* c* b* a*", "<p><em>a <em>b <em>c *d* c</em> b</em> a</em></p>\n")]
    [InlineData("![a ![b ![c ![d](u) c](u) b](u) a](u)", "<p><img src=\"u\" alt=\"a b c ![d](u) c b a\" /></p>\n")]
    public void ConstructsNestedInMoreConstructsThanTheLimitAreText(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { MaximumNestingDepth = 2 });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Theory]
    [InlineData("*a **b@c.de** a*", MarkdownExtensions.Autolinks, "<p><em>a <strong>b@c.de</strong> a</em></p>\n")]
    [InlineData("*[HTML]: Hyper\n\n*a **HTML** a*", MarkdownExtensions.Abbreviations, "<p><em>a <strong>HTML</strong> a</em></p>\n")]
    public void EmailAddressesAndAbbreviationsNestedInMoreInlinesThanTheLimitAreText(string text, MarkdownExtensions extensions, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = extensions, MaximumNestingDepth = 1 });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));
        Assert.DoesNotContain(tree.GetRoot().DescendantNodes(), node => node is MarkdownAutolinkSyntax or MarkdownAbbreviationSyntax);
    }

    [Theory]
    [InlineData(1, "<blockquote>\n<ul>\n<li>a\n:   b</li>\n</ul>\n</blockquote>\n")]
    [InlineData(3, "<blockquote>\n<ul>\n<li>\n<dl>\n<dt>a</dt>\n<dd>b</dd>\n</dl>\n</li>\n</ul>\n</blockquote>\n")]
    public void DefinitionOnALazyLineNestedInMoreBlocksThanTheLimitIsText(int limit, string expected)
    {
        // The definition marker is on a line that the list item does not continue: the definition list would go in the item
        var tree = MarkdownSyntaxTree.ParseText("> - a\n:   b\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.DefinitionLists, MaximumNestingDepth = limit });

        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Theory]
    [InlineData(">", " a")]
    [InlineData("- ", "a")]
    [InlineData("> - ", "a")]
    [InlineData("*a ", "b")]
    [InlineData("~~a ", "b")]
    [InlineData("![", "a")]
    public void DeeplyNestedConstructsDoNotExhaustTheStack(string open, string content)
    {
        var close = open switch
        {
            "*a " => " a*",
            "~~a " => " a~~",
            "![" => "](u)",
            _ => "",
        };
        var text = string.Concat(Enumerable.Repeat(open, 50_000)) + content + string.Concat(Enumerable.Repeat(close, 50_000));
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = MarkdownExtensions.Advanced | MarkdownExtensions.GitHubFlavored });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.NotEmpty(MarkdownHtmlRenderer.ToHtml(tree));
        Assert.NotEmpty(MarkdownPlainTextRenderer.ToPlainText(tree));
        Assert.NotEmpty(MarkdownFormatter.FormatText(tree, options: null));
    }

    [Fact]
    public void MaximumNestingDepthReadsDeeperBlocksAsText()
    {
        #region MarkdownParseOptions_MaximumNestingDepth
        var markdown = new string('>', 10_000) + " text";

        // The block quotes nested in more than 2 block quotes are text: the tree is 3 block quotes deep
        var options = new MarkdownParseOptions { MaximumNestingDepth = 2 };
        var tree = MarkdownSyntaxTree.ParseText(markdown, options);
        Assert.HasCount(3, tree.GetRoot().DescendantNodes().OfType<MarkdownBlockQuoteSyntax>());
        Assert.Equal(markdown, tree.GetRoot().ToFullString());
        #endregion
    }

    [Fact]
    public void MaximumNestingDepthMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MarkdownParseOptions { MaximumNestingDepth = 0 });
    }

    [Fact]
    public void MaximumNestingDepthIsAtMost256()
    {
        Assert.Equal(256, new MarkdownParseOptions { MaximumNestingDepth = int.MaxValue }.MaximumNestingDepth);
        Assert.Equal(256, new MarkdownParseOptions { MaximumNestingDepth = 257 }.MaximumNestingDepth);
        Assert.Equal(200, new MarkdownParseOptions { MaximumNestingDepth = 200 }.MaximumNestingDepth);
    }

    // The largest limit keeps the stack within 1 MB in a release build: the stack of a debug build is larger
    [Fact]
    public void LargestMaximumNestingDepthDoesNotOverflowTheStack()
    {
        var text = new string('>', 10_000) + " " + new string('*', 10_000) + "a" + new string('*', 10_000) + "\n";
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Footnotes | MarkdownExtensions.CustomContainers, MaximumNestingDepth = int.MaxValue };
        string? html = null;
        var thread = new Thread(() => html = MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(text, options)), maxStackSize: 4 * 1024 * 1024);
        thread.Start();
        thread.Join();

        // 257 block quotes: the ones nested in more than 256 are text
        Assert.HasCount(258, html!.Split("<blockquote>"));
    }

    [Fact]
    public void InlineExtensionsAreVisited()
    {
        var tree = MarkdownSyntaxTree.ParseText("a [[b]] c", new MarkdownParseOptions { SyntaxExtensions = [new WikiLinkExtension()] });
        var walker = new CountingWalker();
        walker.Visit(tree.GetRoot());

        Assert.Equal(3, walker.Count);
    }

    [Fact]
    public void InlineExtensionsMustCompleteTheirConstructs()
    {
        var options = new MarkdownParseOptions { SyntaxExtensions = [new ParseHandlerExtension("bad", "@", (ref MarkdownInlineParseContext context) => true)] };

        Assert.Throws<InvalidOperationException>(() => MarkdownSyntaxTree.ParseText("a @b", options));
    }

    [Fact]
    public void InlineExtensionsReportValidParts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Parse((ref MarkdownInlineParseContext context) => context.AddInlines(0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Parse((ref MarkdownInlineParseContext context) => context.AddText(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Parse((ref MarkdownInlineParseContext context) => context.AddText(0, 4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Parse((ref MarkdownInlineParseContext context) =>
        {
            context.AddMarker(0, 2);
            context.AddText(1, 1);
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Parse((ref MarkdownInlineParseContext context) =>
        {
            context.AddMarker(0, 2);
            context.Complete(1);
        }));
        Assert.Throws<InvalidOperationException>(() => Parse((ref MarkdownInlineParseContext context) =>
        {
            context.Complete(1);
            context.AddText(1, 1);
        }));

        static void Parse(ParseHandler handler) => MarkdownSyntaxTree.ParseText("@ab", new MarkdownParseOptions { SyntaxExtensions = [new ParseHandlerExtension("bad", "@", (ref MarkdownInlineParseContext context) =>
        {
            handler(ref context);
            return true;
        })] });
    }

    [Fact]
    public void SyntaxExtensionsAreValidated()
    {
        Assert.Throws<ArgumentException>(() => new MentionExtension("", "@"));
        Assert.Throws<ArgumentException>(() => new MentionExtension("a", ""));
        Assert.Throws<ArgumentException>(() => new MentionExtension("a", "@ "));
        Assert.Throws<ArgumentException>("character", () => new MarkdownDelimiterExtension("a", '*'));
        Assert.Throws<ArgumentException>("character", () => new MarkdownDelimiterExtension("a", 'a'));
        Assert.Throws<ArgumentException>("character", () => new MarkdownDelimiterExtension("a", 'é'));
        Assert.Throws<ArgumentOutOfRangeException>("maximumCount", () => new MarkdownDelimiterExtension("a", '%', minimumCount: 2, maximumCount: 1));

        var mention = new MentionExtension();
        Assert.Throws<ArgumentException>("value", () => new MarkdownParseOptions { SyntaxExtensions = [mention, mention] });
        Assert.Throws<ArgumentException>("value", () => new MarkdownParseOptions { SyntaxExtensions = [mention, new MentionExtension()] });
        Assert.Throws<ArgumentException>("value", () => new MarkdownParseOptions { SyntaxExtensions = [new MarkdownDelimiterExtension("a", '%'), new MarkdownDelimiterExtension("b", '%')] });
        Assert.Throws<ArgumentException>("value", () => new MarkdownParseOptions { SyntaxExtensions = [null!] });

        // A delimiter extension cannot use a character of an enabled extension
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Marked, SyntaxExtensions = [new MarkdownDelimiterExtension("a", '=')] };
        Assert.Throws<ArgumentException>("options", () => MarkdownSyntaxTree.ParseText("a", options));
        Assert.Equal("<p>a</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("a", options with { Extensions = MarkdownExtensions.None })));
    }

    [Fact]
    public void FormatterKeepsInlineExtensions()
    {
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.PipeTables, SyntaxExtensions = [new MentionExtension(), new MarkdownDelimiterExtension("highlight", '%')] };
        var tree = MarkdownSyntaxTree.ParseText("* @a %%b%%\n\n|x|y|\n|-|-|\n|@c|%%d%%|\n", options);
        var formatted = MarkdownFormatter.Format(tree);

        Assert.Equal(MarkdownHtmlRenderer.ToHtml(tree), MarkdownHtmlRenderer.ToHtml(formatted));
        Assert.HasCount(2, formatted.GetRoot().DescendantNodes().OfType<MarkdownExtensionInlineSyntax>().Where(node => node.Name == "mention"));
    }

    [Fact]
    public void ParsesFencedBlockExtensions()
    {
        #region MarkdownSyntaxTree_FencedBlockExtension
        var options = new MarkdownParseOptions { SyntaxExtensions = [new AdmonitionExtension()] };
        var tree = MarkdownSyntaxTree.ParseText("!!! warning\nDo not *push* on Friday.\n\n- Really.\n!!!\n", options);
        var admonition = Assert.IsType<MarkdownExtensionBlockSyntax>(tree.GetRoot().Blocks.Single());

        Assert.Equal("admonition", admonition.Name);
        Assert.Equal("warning", admonition.Info);
        Assert.Equal(["!!!", "!!!"], admonition.Markers.Select(marker => marker.Text));

        // By default, the blocks are written without the fences
        Assert.Equal("<p>Do not <em>push</em> on Friday.</p>\n<ul>\n<li>Really.</li>\n</ul>\n", MarkdownHtmlRenderer.ToHtml(tree));

        var rendererOptions = new MarkdownHtmlRendererOptions { NodeRenderers = [new AdmonitionRenderer()] };
        Assert.Equal("<div class=\"admonition warning\">\n<p>Do not <em>push</em> on Friday.</p>\n<ul>\n<li>Really.</li>\n</ul>\n</div>\n", MarkdownHtmlRenderer.ToHtml(tree, rendererOptions));
        #endregion
    }

    [Fact]
    public void ParsesFencedBlockExtensionsOfRawLines()
    {
        var options = new MarkdownParseOptions { SyntaxExtensions = [new RawFenceExtension()] };
        var tree = MarkdownSyntaxTree.ParseText("> %%% chart\n>   bar: <1>\n> *2*\n> %%%\n\nafter\n", options);
        var block = tree.GetRoot().DescendantNodes().OfType<MarkdownExtensionBlockSyntax>().Single();

        Assert.Equal("chart", block.Info);
        Assert.Equal("  bar: <1>\n*2*\n", block.Content);
        Assert.HasCount(2, block.Lines);
        Assert.Equal("> %%% chart\n>   bar: <1>\n> *2*\n> %%%\n\nafter\n", tree.GetRoot().ToFullString());
        Assert.Equal("<blockquote>\n<pre><code>  bar: &lt;1&gt;\n*2*\n</code></pre>\n</blockquote>\n<p>after</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
        Assert.Equal("  bar: <1>\n*2*\nafter\n", MarkdownPlainTextRenderer.ToPlainText(tree));
    }

    [Theory]
    [InlineData("%%%\na\n", "<pre><code>a\n</code></pre>\n")]
    [InlineData("> %%%\n> a\nb\n", "<blockquote>\n<pre><code>a\n</code></pre>\n</blockquote>\n<p>b</p>\n")]
    [InlineData("  %%%\n    a\n %%%\n", "<pre><code>  a\n</code></pre>\n")]
    [InlineData("%%%\n%%% x\n%%%\n", "<pre><code>%%% x\n</code></pre>\n")]
    [InlineData("text\n%%%\na\n%%%\n", "<p>text</p>\n<pre><code>a\n</code></pre>\n")]
    [InlineData("    %%%\n", "<pre><code>%%%\n</code></pre>\n")]
    public void FencedBlockExtensionsEndAsFencedCodeBlocks(string text, string html)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { SyntaxExtensions = [new RawFenceExtension()] });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(html, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Fact]
    public void FencedBlockExtensionsOfRawLinesKeepTheirBlankLinesInDefinitions()
    {
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.DefinitionLists, SyntaxExtensions = [new RawFenceExtension()] };
        var extension = MarkdownSyntaxTree.ParseText("Term\n:   %%%\n    a\n\n    b\n    %%%\n", options);
        var code = MarkdownSyntaxTree.ParseText("Term\n:   ```\n    a\n\n    b\n    ```\n", options);

        // The blank line is a line of the block, not a blank line of the definition
        Assert.Equal(0, code.GetRoot().DescendantNodes().OfType<MarkdownDefinitionItemSyntax>().Single().BlankLineCount);
        Assert.Equal(0, extension.GetRoot().DescendantNodes().OfType<MarkdownDefinitionItemSyntax>().Single().BlankLineCount);
        Assert.Equal(MarkdownHtmlRenderer.ToHtml(code), MarkdownHtmlRenderer.ToHtml(extension));
    }

    [Fact]
    public void InlineExtensionsDoNotSpanTheRowsOfPipeTables()
    {
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.PipeTables, SyntaxExtensions = [new WikiLinkExtension()] };
        var tree = MarkdownSyntaxTree.ParseText("| [[a | b |\n|---|---|\n| c]] | [[d]] |\n", options);

        Assert.Single(tree.GetRoot().Blocks.OfType<MarkdownTableSyntax>());
        Assert.Equal("| [[a | b |\n|---|---|\n| c]] | [[d]] |\n", tree.GetRoot().ToFullString());
    }

    [Fact]
    public void FencedBlockExtensionsCloseTheirBlocks()
    {
        var tree = MarkdownSyntaxTree.ParseText("!!! note\n```\ncode\n!!!\nafter\n", new MarkdownParseOptions { SyntaxExtensions = [new AdmonitionExtension()] });

        Assert.Equal("<pre><code>code\n</code></pre>\n<p>after</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Fact]
    public void FencedBlockExtensionsCanLeaveParagraphs()
    {
        var options = new MarkdownParseOptions { SyntaxExtensions = [new RawFenceExtension { CanInterruptParagraph = false }] };

        Assert.Equal("<p>text\n%%%\na</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("text\n%%%\na\n", options)));
        Assert.Equal("<p>text</p>\n<pre><code>a\n</code></pre>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("text\n\n%%%\na\n", options)));
    }

    [Fact]
    public void FencedBlockExtensionsReadTheirInfoAsInlines()
    {
        var tree = MarkdownSyntaxTree.ParseText("!!! A *warning* \nb\n!!!\n", new MarkdownParseOptions { SyntaxExtensions = [new AdmonitionExtension { ParseInfoAsInlines = true }] });
        var block = Assert.IsType<MarkdownExtensionBlockSyntax>(tree.GetRoot().Blocks.Single());

        Assert.Equal("A *warning*", block.Info);
        Assert.Single(block.Inlines.Select(inline => inline.AsNode()).OfType<MarkdownEmphasisSyntax>());
        Assert.Equal("!!! A *warning* \nb\n!!!\n", tree.GetRoot().ToFullString());
        Assert.Equal("<div class=\"admonition A warning\">\n<p>b</p>\n</div>\n", MarkdownHtmlRenderer.ToHtml(tree, new MarkdownHtmlRendererOptions { NodeRenderers = [new AdmonitionRenderer()] }));
    }

    [Fact]
    public void ParsesParagraphExtensions()
    {
        #region MarkdownSyntaxTree_ParagraphExtension
        var options = new MarkdownParseOptions { SyntaxExtensions = [new TranscriptExtension()] };
        var tree = MarkdownSyntaxTree.ParseText("The meeting went like this:\nAlice: Can we ship on *Friday*?\nBob: Only if the tests pass.\nThen we went for lunch.\n", options);
        var blocks = tree.GetRoot().Blocks;

        // The converter took the lines of the transcript: the lines around them are paragraphs
        Assert.HasCount(3, blocks);
        var transcript = Assert.IsType<MarkdownExtensionBlockSyntax>(blocks[1]);
        Assert.Equal("transcript", transcript.Name);
        Assert.Equal(["Alice", "Bob"], transcript.Texts.Select(speaker => speaker.Text));

        var rendererOptions = new MarkdownHtmlRendererOptions { NodeRenderers = [new TranscriptRenderer()] };
        Assert.Equal("<p>The meeting went like this:</p>\n<dl class=\"transcript\">\n<dt>Alice</dt><dd>Can we ship on <em>Friday</em>?</dd>\n<dt>Bob</dt><dd>Only if the tests pass.</dd>\n</dl>\n<p>Then we went for lunch.</p>\n", MarkdownHtmlRenderer.ToHtml(tree, rendererOptions));
        #endregion
    }

    [Theory]
    [InlineData("Alice: a\nBob: b\n", "<p>Alicea\nBobb</p>\n")]
    [InlineData("x\nAlice: a\n", "<p>x\nAlice: a</p>\n")]
    [InlineData("Alice: a\nBob: b\nx\nCarol: c\nDan: d\ny\n", "<p>Alicea\nBobb</p>\n<p>x</p>\n<p>Carolc\nDand</p>\n<p>y</p>\n")]
    [InlineData("[r]: /u\nAlice: [r]\nBob: b\n", "<p>Alice<a href=\"/u\">r</a>\nBobb</p>\n")]
    [InlineData("Alice: a\nBob: b\n===\n", "<h1>Alice: a\nBob: b</h1>\n")]
    [InlineData("[r]:/u\n===\nAlice: a\nBob: b\n", "<p>===</p>\n<p>Alicea\nBobb</p>\n")]
    [InlineData("> Alice: a\nBob:   *b*  \n", "<blockquote>\n<p>Alicea\nBob<em>b</em></p>\n</blockquote>\n")]
    [InlineData("- Alice: a\n  Bob: b\n", "<ul>\n<li>Alicea\nBobb</li>\n</ul>\n")]
    [InlineData("no speakers\n", "<p>no speakers</p>\n")]
    public void ParagraphExtensionsSplitParagraphs(string text, string html)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { SyntaxExtensions = [new TranscriptExtension()] });

        Assert.Equal(text, tree.GetRoot().ToFullString());
        Assert.Equal(html, MarkdownHtmlRenderer.ToHtml(tree));
    }

    [Fact]
    public void ParagraphExtensionsReportValidParts()
    {
        Assert.Throws<InvalidOperationException>(() => Parse((ref MarkdownParagraphConvertContext context) => context.AddInlines(0, 0, 1)));
        Assert.Throws<InvalidOperationException>(() => Parse((ref MarkdownParagraphConvertContext context) => context.Complete(0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Parse((ref MarkdownParagraphConvertContext context) => context.AddText(0, 0, 4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Parse((ref MarkdownParagraphConvertContext context) => context.AddText(2, 0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Parse((ref MarkdownParagraphConvertContext context) =>
        {
            context.AddText(1, 0, 1);
            context.AddText(0, 0, 1);
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Parse((ref MarkdownParagraphConvertContext context) =>
        {
            context.AddText(1, 0, 1);
            context.Complete(0, 1);
        }));
        Assert.Throws<InvalidOperationException>(() => MarkdownSyntaxTree.ParseText("abc\nd", new MarkdownParseOptions { SyntaxExtensions = [new ConvertHandlerExtension((ref MarkdownParagraphConvertContext context) => true)] }));

        static void Parse(ConvertHandler handler) => MarkdownSyntaxTree.ParseText("abc\nd", new MarkdownParseOptions { SyntaxExtensions = [new ConvertHandlerExtension((ref MarkdownParagraphConvertContext context) =>
        {
            handler(ref context);
            return true;
        })] });
    }

    [Fact]
    public void BlockExtensionsShareTheirStateWithInlineExtensions()
    {
        #region MarkdownSyntaxTree_ExtensionState
        var glossary = new GlossaryExtension();
        var options = new MarkdownParseOptions { SyntaxExtensions = [glossary, new GlossaryTermExtension(glossary)] };
        var tree = MarkdownSyntaxTree.ParseText("Call the [API] from a [link](https://example.com).\n\n%%% glossary\nAPI: Application programming interface\n%%%\n", options);

        // The glossary is read before the inlines, so a term can be used before its definition
        var rendererOptions = new MarkdownHtmlRendererOptions { NodeRenderers = [new GlossaryRenderer()] };
        Assert.Equal("<p>Call the <abbr title=\"Application programming interface\">API</abbr> from a <a href=\"https://example.com\">link</a>.</p>\n", MarkdownHtmlRenderer.ToHtml(tree, rendererOptions));
        #endregion
    }

    [Fact]
    public void ExtensionStatesAreCreatedOncePerDocument()
    {
        var counting = new CountingStateExtension();
        var options = new MarkdownParseOptions { SyntaxExtensions = [counting] };
        _ = MarkdownSyntaxTree.ParseText("a\n\nb\n\n> c", options);
        _ = MarkdownSyntaxTree.ParseText("d", options);

        Assert.Equal(2, counting.StateCount);
        Assert.Equal(4, counting.ParagraphCount);

        var glossary = new GlossaryExtension();
        Assert.Throws<ArgumentException>("extension", () => MarkdownSyntaxTree.ParseText("[A]", new MarkdownParseOptions { SyntaxExtensions = [new GlossaryTermExtension(glossary)] }));
    }

    [Fact]
    public void BlockExtensionsAreVisitedAndFormatted()
    {
        var options = new MarkdownParseOptions { SyntaxExtensions = [new AdmonitionExtension(), new RawFenceExtension(), new TranscriptExtension()] };
        var tree = MarkdownSyntaxTree.ParseText("!!! note\n* a\n!!!\n%%%\n  b\n%%%\nx\nAlice: *c*\nBob: d\n", options);
        var walker = new CountingWalker();
        walker.Visit(tree.GetRoot());
        var formatted = MarkdownFormatter.Format(tree);

        Assert.HasCount(3, tree.GetRoot().DescendantNodes().OfType<MarkdownExtensionBlockSyntax>());

        // The document, the admonition, its list, item and paragraph, the raw block, the paragraph, the transcript and its emphasis
        Assert.Equal(9, walker.Count);
        Assert.Equal(MarkdownHtmlRenderer.ToHtml(tree), MarkdownHtmlRenderer.ToHtml(formatted));
        Assert.Equal(tree.GetRoot().GetPlainText(), formatted.GetRoot().GetPlainText());
    }

    [Fact]
    public void NodeRenderersCanOnlyWriteTheDefaultRenderingOfBlocksAndInlines()
    {
        var tree = MarkdownSyntaxTree.ParseText("| a |\n|---|\n| b |\n", MarkdownParseOptions.GitHubFlavored);
        var options = new MarkdownHtmlRendererOptions { NodeRenderers = [new RowRenderer()] };

        Assert.Throws<ArgumentException>("node", () => MarkdownHtmlRenderer.ToHtml(tree, options));
    }

    [Theory]
    [InlineData("foo bar", "foo bar\n")]
    [InlineData("foo\nbar", "foo\nbar\n")]
    [InlineData("*foo\nbar*", "foo\nbar\n")]
    [InlineData("[foo\nbar](http://example.com)", "foo\nbar\n")]
    [InlineData("[\nfoo](/url)", "foo\n")]
    [InlineData("<http://foo.bar.baz>", "http://foo.bar.baz\n")]
    [InlineData("<http://foo.bar/?a&b>", "http://foo.bar/?a&b\n")]
    [InlineData("# foo bar", "foo bar\n")]
    [InlineData("# foo\nbar", "foo\nbar\n")]
    [InlineData("> foo", "foo\n")]
    [InlineData("> foo\nbar\n> baz", "foo\nbar\nbaz\n")]
    [InlineData("`foo`", "foo\n")]
    [InlineData("`foo\nbar`", "foo bar\n")]
    [InlineData("```\nfoo bar\n```", "foo bar\n")]
    [InlineData("```\nConsole.WriteLine(\"Hello, World!\");\n```", "Console.WriteLine(\"Hello, World!\");\n")]
    [InlineData("- foo\n- bar\n- baz", "foo\nbar\nbaz\n")]
    [InlineData("- foo<baz", "foo<baz\n")]
    [InlineData("- foo&lt;baz", "foo<baz\n")]
    [InlineData("## foo `bar::baz >`", "foo bar::baz >\n")]
    [InlineData("<div>\nx\n</div>\n\nfoo", "foo\n")]
    [InlineData("a <b>c</b>\n\n---\n\n![alt *text*](a.png)", "a c\nalt text\n")]
    [InlineData("a  \nb\\\nc", "a\nb\nc\n")]
    public void RendersThePlainTextOfCommonMark(string text, string expected)
    {
        Assert.Equal(expected, MarkdownPlainTextRenderer.ToPlainText(MarkdownSyntaxTree.ParseText(text)));
    }

    // As Meziantou.Framework.Markdown 1.x, except that the text after a table starts on a new line, and that the characters of
    // SmartyPants and the alerts have no HTML
    [Theory]
    [InlineData(":::\nfoo\n:::", "foo\n", MarkdownExtensions.CustomContainers)]
    [InlineData(":::bar\nfoo\n:::", "foo\n", MarkdownExtensions.CustomContainers | MarkdownExtensions.GenericAttributes)]
    [InlineData("```mermaid\nA --> B & C <x> \"q\"\n```", "A --> B & C <x> \"q\"\n", MarkdownExtensions.Diagrams)]
    [InlineData("| Header1 | Header2 | Header3 |\n|--|--|--|\nt**es**t|value2|value3", "Header1 Header2 Header3 \ntest value2 value3 \n", MarkdownExtensions.PipeTables)]
    [InlineData("| a | b |\n|---|---|\n| c | d |\n\ne", "a b \nc d \ne\n", MarkdownExtensions.Tables)]
    [InlineData("+---+---+\n| a | b |\n+===+===+\n| c | d |\n+---+---+\n\ne", "a b \nc d \ne\n", MarkdownExtensions.GridTables)]
    [InlineData("Term 1\nTerm 2\n:   Definition 1\n:   Definition 2\n\n    More", "Term 1\nTerm 2\nDefinition 1\nDefinition 2\nMore\n", MarkdownExtensions.DefinitionLists)]
    [InlineData("^^^\n![alt](a.png)\n^^^ The *caption*", "alt\nThe caption\n", MarkdownExtensions.Figures)]
    [InlineData("a\n\n^^ The *footer*\n^^ on two lines", "a\nThe footer\non two lines\n", MarkdownExtensions.Footers)]
    [InlineData("a[^1] b[^2]\n\n[^1]: First *note*\n[^2]: Second note", "a1 b2\nFirst note\nSecond note\n", MarkdownExtensions.Footnotes)]
    [InlineData("- [x] done\n- [ ] todo", "[x] done\n[ ] todo\n", MarkdownExtensions.TaskLists)]
    [InlineData("\"Quote\" -- it's <<here>>...", "\u201CQuote\u201D \u2013 it's \u00ABhere\u00BB\u2026\n", MarkdownExtensions.SmartyPants)]
    [InlineData("> [!NOTE]\n> Read this.", "Read this.\n", MarkdownExtensions.Alerts)]
    [InlineData("$$a < b$$ and *HTML*\n\n*[HTML]: Hyper Text", "a < b and HTML\n", MarkdownExtensions.Mathematics | MarkdownExtensions.Abbreviations)]
    [InlineData(":smile: ![](https://youtu.be/x)", "\U0001F604 \n", MarkdownExtensions.Emoji | MarkdownExtensions.MediaLinks)]
    public void RendersThePlainTextOfTheExtensions(string text, string expected, MarkdownExtensions extensions)
    {
        Assert.Equal(expected, MarkdownPlainTextRenderer.ToPlainText(MarkdownSyntaxTree.ParseText(text, new MarkdownParseOptions { Extensions = extensions })));
    }

    [Fact]
    public void EscapesTextWithMarkdownEscaper()
    {
        #region MarkdownEscaper_Usage
        // Escape the text so that Markdown displays it as is: every ASCII punctuation character is escaped
        Assert.Equal(@"\*Note\* \[docs\]\!", MarkdownEscaper.Escape("*Note* [docs]!"));

        // The delimiters of code are longer than the runs of backticks of the content
        Assert.Equal("``a`b``", MarkdownEscaper.CreateCodeSpan("a`b"));
        Assert.Equal("`` `code` ``", MarkdownEscaper.CreateCodeSpan("`code`"));
        Assert.Equal("```csharp\nvar a = 1;\n```\n", MarkdownEscaper.CreateFencedCodeBlock("var a = 1;", "csharp"));

        // Build a link from untrusted parts
        var text = MarkdownEscaper.Escape("Docs [v2]");
        var destination = MarkdownEscaper.EscapeLinkDestination("https://example.com/a b");
        var title = MarkdownEscaper.EscapeLinkTitle("Say \"hi\"");
        Assert.Equal("<https://example.com/a b>", destination);
        Assert.Equal("\"Say \\\"hi\\\"\"", title);

        var html = MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText($"[{text}]({destination} {title})"));
        Assert.Equal("<p><a href=\"https://example.com/a%20b\" title=\"Say &quot;hi&quot;\">Docs [v2]</a></p>\n", html);

        // The label of a reference link
        Assert.Equal(@"see \[1\]", MarkdownEscaper.EscapeLinkLabel("see [1]"));

        // A cell of a pipe table cannot contain a pipe or a line ending
        Assert.Equal(@"a \| b c", MarkdownEscaper.EscapeTableCell("a | b\nc"));
        #endregion
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("*test* **test** ***test*** _test_ ~test~")]
    [InlineData("1. test")]
    [InlineData("- test")]
    [InlineData("> test")]
    [InlineData("# header")]
    [InlineData("---")]
    [InlineData("===")]
    [InlineData("[test](url) ![test](url)")]
    [InlineData("&quot; &#35; &amp")]
    [InlineData("<div> <https://example.com> <a@b.c>")]
    [InlineData("`code` ``code``")]
    [InlineData(@"C:\path\file.txt \\ \")]
    [InlineData("a | b")]
    [InlineData("https://example.com www.example.com")]
    [InlineData("a\n# b\n- c\n> d")]
    [InlineData("é ✓ 😀")]
    [InlineData("    indented text")]
    [InlineData("\ttab")]
    [InlineData("a  \nb")]
    [InlineData("a\t\n  b  ")]
    [InlineData(" a ")]
    [InlineData("a\n \nb")]
    [InlineData("  ")]
    public void EscapedTextIsParsedBackToTheText(string text)
    {
        var escaped = MarkdownEscaper.Escape(text);

        foreach (var options in new[] { MarkdownParseOptions.Default, MarkdownParseOptions.GitHubFlavored, new MarkdownParseOptions { Extensions = MarkdownExtensions.Advanced } })
        {
            var paragraph = Assert.IsType<MarkdownParagraphSyntax>(MarkdownSyntaxTree.ParseText(escaped, options).GetRoot().Blocks.Single());
            Assert.Equal(text, paragraph.GetPlainText());
        }
    }

    [Fact]
    public void EscapeReturnsTheTextWhenThereIsNothingToEscape()
    {
        var text = "plain text";
        Assert.Same(text, MarkdownEscaper.Escape(text));
    }

    [Theory]
    [InlineData("    indented text", "<p>    indented text</p>\n")]
    [InlineData("a\n\n\tb", "<p>a</p>\n<p>\tb</p>\n")]
    [InlineData("a  \nb", "<p>a  \nb</p>\n")]
    public void EscapedTextKeepsTheWhitespaceAroundItsLines(string text, string expected)
    {
        // The parser would strip the whitespace, which would start an indented code block or end the line with a hard line break
        Assert.Equal(expected, MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(MarkdownEscaper.Escape(text))));
    }

    [Theory]
    [InlineData("Thanks \u00A7alice and Tom")]
    [InlineData("\u00B6\u00B6\u00B6 note\nb \u00B6\u00B6\u00B6")]
    [InlineData("xD and :) and (y)")]
    [InlineData("a\u00A7\U0001F600\u00A7")]
    public void EscapedTextIsParsedBackToTheTextWithTheExtensions(string text)
    {
        var options = new MarkdownParseOptions
        {
            Extensions = Enum.GetValues<MarkdownExtensions>().Aggregate(MarkdownExtensions.None, (all, extension) => all | extension),
            EmojiMapping = new MarkdownEmojiMapping(new Dictionary<string, string> { [":laughing:"] = "\U0001F606", [":ok:"] = "\u2705" }, new Dictionary<string, string> { ["xD"] = ":laughing:", ["(y)"] = ":ok:" }),
            SyntaxExtensions = [new MentionExtension("section", "\u00A7"), new MentionExtension("tag", "T"), new PilcrowFenceExtension()],
        };

        var escaped = MarkdownEscaper.Escape(text, options);

        var paragraph = Assert.IsType<MarkdownParagraphSyntax>(MarkdownSyntaxTree.ParseText(escaped, options).GetRoot().Blocks.Single());
        Assert.Equal(text, paragraph.GetPlainText());
        Assert.NotEqual(escaped, MarkdownEscaper.Escape(text));
    }

    [Fact]
    public void EscapeWritesTheTriggerCharactersOfTheExtensionsAsEntities()
    {
        var options = new MarkdownParseOptions { SyntaxExtensions = [new MentionExtension("section", "\u00A7"), new PilcrowFenceExtension()] };

        Assert.Equal("&#167;a \\@b", MarkdownEscaper.Escape("\u00A7a @b", options));

        // The trigger character of a fenced block starts a line
        Assert.Equal("&#182;\u00B6 a \u00B6\n&#182;", MarkdownEscaper.Escape("\u00B6\u00B6 a \u00B6\n\u00B6", options));
        Assert.Same("plain text", MarkdownEscaper.Escape("plain text", options));
        Assert.Throws<ArgumentNullException>("options", () => MarkdownEscaper.Escape("a", options: null!));
    }

    [Theory]
    [InlineData("a \U0001F600", "a &#128512;")]
    [InlineData("a \uD83D", "a \uD83D")]
    [InlineData("\uDE00 \uD83D\uD83D\uDE00", "\uDE00 \uD83D&#128512;")]
    public void EscapeWritesATriggerCharacterOutsideTheBasicMultilingualPlaneAsOneEntity(string text, string expected)
    {
        // An entity cannot write a lone surrogate, which the parser would decode to U+FFFD
        var options = new MarkdownParseOptions { SyntaxExtensions = [new MentionExtension("smile", "\U0001F600")] };

        Assert.Equal(expected, MarkdownEscaper.Escape(text, options));
    }

    [Theory]
    [InlineData("test")]
    [InlineData("te`st")]
    [InlineData("`test")]
    [InlineData("test`")]
    [InlineData("`")]
    [InlineData("```")]
    [InlineData("` ` `")]
    [InlineData("`teste```testr`")]
    [InlineData("`tes````te```testr`")]
    [InlineData(" a ")]
    [InlineData(" a")]
    [InlineData("a ")]
    [InlineData(" ")]
    [InlineData("  ")]
    [InlineData("a | b")]
    [InlineData(@"a\b\`")]
    [InlineData("*a* <b> &amp;")]
    public void CodeSpanIsParsedBackToItsContent(string content)
    {
        var paragraph = Assert.IsType<MarkdownParagraphSyntax>(MarkdownSyntaxTree.ParseText(MarkdownEscaper.CreateCodeSpan(content)).GetRoot().Blocks.Single());

        var code = Assert.IsType<MarkdownCodeSpanSyntax>(paragraph.Inlines.Single().AsNode());
        Assert.Equal(content, code.Code);
    }

    [Fact]
    public void CodeSpanConvertsTheLineEndingsToSpaces()
    {
        var code = MarkdownSyntaxTree.ParseText(MarkdownEscaper.CreateCodeSpan("a\nb\r\nc")).GetRoot().DescendantNodes().OfType<MarkdownCodeSpanSyntax>().Single();

        Assert.Equal("a b c", code.Code);
    }

    [Theory]
    [InlineData("a\n# b")]
    [InlineData("a\n- b")]
    [InlineData("a\r\n> b")]
    [InlineData("a\r<div>")]
    [InlineData("a\n\nb")]
    [InlineData("\na\n")]
    public void CodeSpanStaysInItsParagraph(string content)
    {
        // A line of the content could start a block, so the line endings are written as the spaces the parser makes them
        var paragraph = Assert.IsType<MarkdownParagraphSyntax>(MarkdownSyntaxTree.ParseText("x " + MarkdownEscaper.CreateCodeSpan(content)).GetRoot().Blocks.Single());

        var code = Assert.IsType<MarkdownCodeSpanSyntax>(paragraph.Inlines.Last().AsNode());
        Assert.Equal(content.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' '), code.Code);
    }

    [Fact]
    public void CodeSpanCannotBeEmpty()
    {
        Assert.Throws<ArgumentException>("content", () => MarkdownEscaper.CreateCodeSpan(""));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("var a = 1;", null)]
    [InlineData("var a = 1;", "csharp")]
    [InlineData("var a = 1;\n", "csharp")]
    [InlineData("a\n\nb", "csharp")]
    [InlineData("```\ncode\n```", "md")]
    [InlineData("````\n  ```  \n```````", "md")]
    [InlineData("~~~\n    ```", null)]
    [InlineData("a\r\nb", null)]
    [InlineData("  indented\n\ttab", null)]
    [InlineData("x", "c`s")]
    [InlineData("x", "a&amp;b")]
    [InlineData("x", @"a\*b\")]
    [InlineData("x", "js title=\"a b\"")]
    [InlineData("x", "`js")]
    public void FencedCodeBlockIsParsedBackToItsCode(string code, string? infoString)
    {
        var blocks = MarkdownSyntaxTree.ParseText(MarkdownEscaper.CreateFencedCodeBlock(code, infoString) + "after").GetRoot().Blocks;

        var block = Assert.IsType<MarkdownCodeBlockSyntax>(blocks[0]);
        var expectedCode = code.ReplaceLineEndings("\n");
        if (expectedCode.Length > 0 && expectedCode[^1] != '\n')
        {
            expectedCode += "\n";
        }

        Assert.True(block.IsFenced);
        Assert.Equal(expectedCode, block.Code.ReplaceLineEndings("\n"));
        Assert.Equal(string.IsNullOrWhiteSpace(infoString) ? null : infoString.Trim(), block.Info);
        Assert.Equal("after", Assert.IsType<MarkdownParagraphSyntax>(blocks[1]).GetPlainText());
    }

    [Fact]
    public void FencedCodeBlockInfoStringCannotHaveLineEndings()
    {
        Assert.Throws<ArgumentException>("infoString", () => MarkdownEscaper.CreateFencedCodeBlock("x", "a\nb"));
    }

    [Fact]
    public void FencedCodeBlockInfoStringIsNotReadAsGenericAttributes()
    {
        var fence = MarkdownEscaper.CreateFencedCodeBlock("x", "csharp {title=a}");
        var block = MarkdownSyntaxTree.ParseText(fence, new MarkdownParseOptions { Extensions = MarkdownExtensions.GenericAttributes }).GetRoot().Blocks.OfType<MarkdownCodeBlockSyntax>().Single();

        Assert.Equal("```csharp &#123;title=a}\nx\n```\n", fence);
        Assert.Equal("csharp {title=a}", block.Info);
        Assert.True(block.GetGenericAttributes().IsEmpty);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://example.com/a b")]
    [InlineData("")]
    [InlineData("test)test")]
    [InlineData("a(b)c")]
    [InlineData("a(b")]
    [InlineData("<a>")]
    [InlineData("a<b>c")]
    [InlineData(@"a\b\)")]
    [InlineData("a&amp;b")]
    [InlineData("a\"b'c")]
    [InlineData("a\nb")]
    [InlineData("é ✓")]
    public void LinkDestinationIsParsedBackToTheUrl(string url)
    {
        var link = MarkdownSyntaxTree.ParseText($"[x]({MarkdownEscaper.EscapeLinkDestination(url)})").GetRoot().DescendantNodes().OfType<MarkdownLinkSyntax>().Single();

        Assert.Equal(url, link.Destination);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("")]
    [InlineData("Say \"hi\"")]
    [InlineData("it's (here)")]
    [InlineData("a&amp;b")]
    [InlineData(@"a\b\")]
    [InlineData("a\nb")]
    [InlineData("a\n\nb")]
    [InlineData("a\n \nb")]
    [InlineData("a\n# b\n- c\n> d")]
    public void LinkTitleIsParsedBackToTheTitle(string title)
    {
        var link = MarkdownSyntaxTree.ParseText($"[x](/url {MarkdownEscaper.EscapeLinkTitle(title)})").GetRoot().DescendantNodes().OfType<MarkdownLinkSyntax>().Single();

        Assert.Equal(title, link.Title);
    }

    [Theory]
    [InlineData("label")]
    [InlineData("see [1]")]
    [InlineData(@"a\b")]
    [InlineData(@"a\")]
    [InlineData("]")]
    [InlineData("*a* `b`")]
    [InlineData("a&amp;b")]
    [InlineData("a\n# b")]
    [InlineData("a\r\n- b")]
    [InlineData("a\n\nb")]
    public void LinkLabelMatchesItsDefinition(string label)
    {
        var escaped = MarkdownEscaper.EscapeLinkLabel(label);
        var link = MarkdownSyntaxTree.ParseText($"[x][{escaped}]\n\n[{escaped}]: /url").GetRoot().DescendantNodes().OfType<MarkdownLinkSyntax>().Single();

        Assert.NotNull(link.Definition);
        Assert.Equal("/url", link.Destination);
    }

    [Theory]
    [InlineData("^1", @"\^1")]
    [InlineData(" \t^1", " \t\\^1")]
    [InlineData("\n^1", @" \^1")]
    [InlineData("a^1", "a^1")]
    public void LinkLabelIsNotAFootnote(string label, string expected)
    {
        var escaped = MarkdownEscaper.EscapeLinkLabel(label);
        var root = MarkdownSyntaxTree.ParseText($"[x][{escaped}]\n\n[{escaped}]: /url", new MarkdownParseOptions { Extensions = MarkdownExtensions.Footnotes }).GetRoot();

        Assert.Equal(expected, escaped);
        Assert.Equal("/url", root.DescendantNodes().OfType<MarkdownLinkSyntax>().Single().Destination);
        Assert.Empty(root.DescendantNodes().OfType<MarkdownFootnoteDefinitionSyntax>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n")]
    public void LinkLabelCannotBeBlank(string label)
    {
        Assert.Throws<ArgumentException>(nameof(label), () => MarkdownEscaper.EscapeLinkLabel(label));
    }

    [Fact]
    public void LinkLabelHasAtMost999Characters()
    {
        Assert.HasCount(999, MarkdownEscaper.EscapeLinkLabel(new string('a', 999)));
        Assert.Throws<ArgumentException>("label", () => MarkdownEscaper.EscapeLinkLabel(new string('a', 1000)));
        Assert.Throws<ArgumentException>("label", () => MarkdownEscaper.EscapeLinkLabel(new string('[', 500)));
    }

    [Theory]
    [InlineData("text", "text")]
    [InlineData("a | b", "a | b")]
    [InlineData("a\nb", "a b")]
    [InlineData("a\r\nb\rc", "a b c")]
    [InlineData(@"a\|b", @"a\|b")]
    [InlineData("*a* `b` [c](d)", "*a* `b` [c](d)")]
    [InlineData("---", "---")]
    [InlineData("a\u0085b", "a\u0085b")]
    [InlineData(" a ", " a ")]
    [InlineData("\na", " a")]
    public void TableCellIsParsedBackToTheText(string text, string expected)
    {
        foreach (var extensions in new[] { MarkdownExtensions.PipeTables, MarkdownExtensions.Tables })
        {
            var tree = MarkdownSyntaxTree.ParseText($"| a | b |\n|---|---|\n| {MarkdownEscaper.EscapeTableCell(text)} | z |", new MarkdownParseOptions { Extensions = extensions });

            var table = tree.GetRoot().Blocks.OfType<MarkdownTableSyntax>().Single();
            var row = Assert.Single(table.Rows);
            Assert.HasCount(2, row.Cells);
            Assert.Equal(expected, row.Cells[0].GetPlainText());
        }
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
    public void WalkerVisitsTheGridTableCellsWhenAsked()
    {
        #region MarkdownSyntaxWalker_GridTableCells
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GridTables };
        var tree = MarkdownSyntaxTree.ParseText("[a](/u)\n\n+---------+---------+\n| [b](/v) | [c](/w) |\n+---------+---------+\n", options);

        var collector = new LinkCollector();
        collector.Visit(tree.GetRoot());

        Assert.Equal(["/u", "/v", "/w"], collector.Links.Select(link => link.Destination));
        #endregion

        var walker = new CountingWalker();
        walker.Visit(tree.GetRoot());
        Assert.Equal(tree.GetRoot().DescendantNodesAndSelf().Count(), walker.Count);
    }

    #region MarkdownSyntaxWalker_LinkCollector
    // Finds the links of a document, the ones of the content of its grid table cells included
    private sealed class LinkCollector() : MarkdownSyntaxWalker(SyntaxWalkerDepth.Node, visitGridTableCells: true)
    {
        public List<MarkdownLinkSyntax> Links { get; } = [];

        public override void VisitMarkdownLink(MarkdownLinkSyntax node)
        {
            Links.Add(node);
            base.VisitMarkdownLink(node);
        }
    }
    #endregion

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
    public void ReadmeExample_ReplaceBlock()
    {
        #region MarkdownSyntaxTree_ReplaceBlock
        var tree = MarkdownSyntaxTree.ParseText("> - first\n> - second\n");
        var paragraph = tree.GetRoot().DescendantNodes().OfType<MarkdownParagraphSyntax>().First();

        // The markers of the block quote and the indentation of the list item are added to the new lines
        var updated = tree.ReplaceBlock(paragraph, "Paragraph\n\n```\ncode\n```");

        Assert.Equal("> - Paragraph\n>\n>   ```\n>   code\n>   ```\n> - second\n", updated.GetRoot().ToFullString());
        #endregion
    }

    [Theory]
    [InlineData("a\n\nb\n", 0, "c", "c\n\nb\n")]
    [InlineData("a\n\nb\n", 1, "c\n", "a\n\nc\n")]
    [InlineData("a\r\nb\r\n\r\nc", 0, "x\ny", "x\r\ny\r\n\r\nc")]
    [InlineData("```\ncode\n```\nafter\n", 0, "# t", "# t\nafter\n")]
    [InlineData("- a\n- b\n", 0, "x\n\ny", "- x\n\n  y\n- b\n")]
    public void ReplacesBlocks(string text, int blockIndex, string markdown, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text);
        var block = tree.GetRoot().DescendantNodes().OfType<MarkdownBlockSyntax>().Where(node => node is MarkdownParagraphSyntax or MarkdownCodeBlockSyntax).ToList()[blockIndex];

        Assert.Equal(expected, tree.ReplaceBlock(block, markdown).GetRoot().ToFullString());
    }

    [Fact]
    public void ReadmeExample_WithGenericAttributes()
    {
        #region MarkdownSyntaxTree_WithGenericAttributes
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GenericAttributes | MarkdownExtensions.AutoIdentifiers };
        var tree = MarkdownSyntaxTree.ParseText("# Steps {.lead}\n\n> - Install\n> - Run\n", options);

        // Add a property to the attributes of the heading, which keeps its class, and give the list an id
        var heading = tree.GetRoot().Blocks.OfType<MarkdownHeadingSyntax>().Single();
        var attributes = heading.GetGenericAttributes();
        tree = tree.WithGenericAttributes(heading, attributes with { Properties = [.. attributes.Properties, new("data-level", "1")] });

        var list = tree.GetRoot().DescendantNodes().OfType<MarkdownListSyntax>().Single();
        tree = tree.WithGenericAttributes(list, new MarkdownGenericAttributes("steps", ["compact"], []));

        // The attributes of a list are on the line before it, with the markers of the block quote
        Assert.Equal("# Steps {.lead data-level=1}\n\n> {#steps .compact}\n> - Install\n> - Run\n", tree.GetRoot().ToFullString());
        Assert.Equal("steps", tree.GetRoot().DescendantNodes().OfType<MarkdownListSyntax>().Single().GetGenericAttributes().Id);
        #endregion
    }

    [Fact]
    public void ReadmeExample_HeadingIdentifiers()
    {
        #region MarkdownSyntaxTree_HeadingIdentifiers
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GenericAttributes | MarkdownExtensions.AutoIdentifiers };
        var tree = MarkdownSyntaxTree.ParseText("# Introduction\n\nGetting started {.lead}\n===\n", options);

        // Write the identifier of every heading as its id, so that the links to it keep working when its text changes. Each edit
        // parses the text again: the headings are found again in the new tree.
        var count = tree.GetRoot().DescendantNodes().OfType<MarkdownHeadingSyntax>().Count();
        for (var i = 0; i < count; i++)
        {
            var heading = tree.GetRoot().DescendantNodes().OfType<MarkdownHeadingSyntax>().ElementAt(i);
            tree = tree.WithGenericAttributes(heading, heading.GetGenericAttributes() with { Id = heading.Identifier });
        }

        Assert.Equal("# Introduction {#introduction}\n\nGetting started {#getting-started .lead}\n===\n", tree.GetRoot().ToFullString());
        #endregion
    }

    [Theory]
    [InlineData("# Title\n", "# Title {#x .c data-a=1 title=\"a b\" hidden}\n")]
    [InlineData("## Title {#old .k} ##\n", "## Title {#x .c data-a=1 title=\"a b\" hidden}\n")]
    [InlineData("## Title ##\n", "## Title ## {#x .c data-a=1 title=\"a b\" hidden}\n")]
    [InlineData("Title {.k}\n===\n", "Title {#x .c data-a=1 title=\"a b\" hidden}\n===\n")]
    [InlineData("```cs\ncode\n```\n", "```cs {#x .c data-a=1 title=\"a b\" hidden}\ncode\n```\n")]
    [InlineData("$$\nx\n$$\n", "$$ {#x .c data-a=1 title=\"a b\" hidden}\nx\n$$\n")]
    [InlineData("::: note\nx\n:::\n", "::: note {#x .c data-a=1 title=\"a b\" hidden}\nx\n:::\n")]
    [InlineData("text\r\nmore\r\n", "text\r\nmore {#x .c data-a=1 title=\"a b\" hidden}\r\n")]
    [InlineData("- a\n- b\n", "{#x .c data-a=1 title=\"a b\" hidden}\n- a\n- b\n")]
    [InlineData("{.old}\n- a\n", "{#x .c data-a=1 title=\"a b\" hidden}\n- a\n")]
    [InlineData("> quote\n", "{#x .c data-a=1 title=\"a b\" hidden}\n> quote\n")]
    [InlineData("    code\n", "{#x .c data-a=1 title=\"a b\" hidden}\n\n    code\n")]
    [InlineData("| a |\n|---|\n", "{#x .c data-a=1 title=\"a b\" hidden}\n\n| a |\n|---|\n")]
    [InlineData("+---+\n| a |\n+---+\n", "{#x .c data-a=1 title=\"a b\" hidden}\n+---+\n| a |\n+---+\n")]
    public void WithGenericAttributes(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, AttributeOptions);
        var block = tree.GetRoot().Blocks.Single(block => block is not MarkdownParagraphSyntax || tree.GetRoot().Blocks.Count == 1);
        var attributes = new MarkdownGenericAttributes("x", ["c"], [new("data-a", "1"), new("title", "a b"), new("hidden", null)]);

        var updated = tree.WithGenericAttributes(block, attributes);

        Assert.Equal(expected, updated.GetRoot().ToFullString());
        Assert.Equal(attributes, updated.GetRoot().DescendantNodes().OfType<MarkdownBlockSyntax>().Single(node => node.RawKind == block.RawKind).GetGenericAttributes());
    }

    [Theory]
    [InlineData("para\n- a\n", "para\n\n{#x}\n- a\n")]
    [InlineData("para\n\n---\n", "para\n\n{#x}\n\n---\n")]
    [InlineData("> - a\n>   b\n", "> {#x}\n> - a\n>   b\n")]
    [InlineData("- a\n\n  > q\n", "- a\n\n  {#x}\n  > q\n")]
    public void WithGenericAttributesSeparatesTheLineOfTheAttributes(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, AttributeOptions);
        var block = tree.GetRoot().DescendantNodes().OfType<MarkdownBlockSyntax>().Last(node => node is MarkdownListSyntax or MarkdownThematicBreakSyntax or MarkdownBlockQuoteSyntax);

        Assert.Equal(expected, tree.WithGenericAttributes(block, new MarkdownGenericAttributes("x", [], [])).GetRoot().ToFullString());
    }

    [Theory]
    [InlineData("# a {#x .c}\n", "# a\n")]
    [InlineData("Title {#x}\n===\n", "Title\n===\n")]
    [InlineData("{.c}\n- a\n", "- a\n")]
    [InlineData("> {.c}\n> - a\n", "> - a\n")]
    [InlineData("```cs {.c}\nx\n```\n", "```cs\nx\n```\n")]
    [InlineData("text {.c}\n", "text\n")]
    [InlineData("hello {.x}\nworld\n", "hello\nworld\n")]
    [InlineData("hello {.x}\r\nworld\r\n", "hello\r\nworld\r\n")]
    [InlineData("# a\n", "# a\n")]
    public void WithGenericAttributesRemovesTheAttributes(string text, string expected)
    {
        var tree = MarkdownSyntaxTree.ParseText(text, AttributeOptions);
        var block = tree.GetRoot().DescendantNodes().OfType<MarkdownBlockSyntax>().FirstOrDefault(node => !node.GetGenericAttributes().IsEmpty) ?? tree.GetRoot().Blocks[0];

        var updated = tree.WithGenericAttributes(block, null);

        Assert.Equal(expected, updated.GetRoot().ToFullString());
        Assert.DoesNotContain(updated.GetRoot().DescendantNodes().OfType<MarkdownBlockSyntax>(), node => !node.GetGenericAttributes().IsEmpty);
    }

    [Fact]
    public void GetGenericAttributesMergesTheAttributesOfTheBlock()
    {
        var tree = MarkdownSyntaxTree.ParseText("{#a .b k=1}\n``` cs {#c .d k=2 j}\nx\n```\n", AttributeOptions);

        var attributes = tree.GetRoot().Blocks.OfType<MarkdownCodeBlockSyntax>().Single().GetGenericAttributes();

        // The id of the last attributes wins, the classes of the first line come first, and the first value of a property is kept
        Assert.Equal(new MarkdownGenericAttributes("a", ["d", "b"], [new("k", "2"), new("j", null)]), attributes);
        Assert.Same(MarkdownGenericAttributes.Empty, MarkdownSyntaxTree.ParseText("# a {#x}\n").GetRoot().Blocks[0].GetGenericAttributes());
    }

    [Theory]
    [InlineData("a b", null, null)]
    [InlineData(null, "", null)]
    [InlineData(null, "a}", null)]
    [InlineData(null, null, "1a")]
    [InlineData(null, null, "a b")]
    public void WithInvalidGenericAttributesThrows(string? id, string? cssClass, string? propertyName)
    {
        var tree = MarkdownSyntaxTree.ParseText("# a\n", AttributeOptions);
        var attributes = new MarkdownGenericAttributes(id, cssClass is null ? [] : [cssClass], propertyName is null ? [] : [new(propertyName, "v")]);

        Assert.Throws<ArgumentException>(nameof(attributes), () => tree.WithGenericAttributes(tree.GetRoot().Blocks[0], attributes));
    }

    [Fact]
    public void WithGenericAttributesQuotesTheValues()
    {
        var tree = MarkdownSyntaxTree.ParseText("# a\n", AttributeOptions);
        var attributes = new MarkdownGenericAttributes(null, [], [new("a", "x\"y"), new("b", ""), new("c", "}")]);

        var updated = tree.WithGenericAttributes(tree.GetRoot().Blocks[0], attributes);

        Assert.Equal("# a {a='x\"y' b=\"\" c=\"}\"}\n", updated.GetRoot().ToFullString());
        Assert.Equal(attributes, updated.GetRoot().Blocks[0].GetGenericAttributes());
        Assert.Throws<ArgumentException>("attributes", () => tree.WithGenericAttributes(tree.GetRoot().Blocks[0], new MarkdownGenericAttributes(null, [], [new("a", "'\"")])));
    }

    [Fact]
    public void WithGenericAttributesOnABlockWithoutAttributesThrows()
    {
        var tree = MarkdownSyntaxTree.ParseText("- a\n", AttributeOptions);
        var withoutExtension = MarkdownSyntaxTree.ParseText("# a\n");

        // Markdig gives no attributes to a list item
        Assert.Throws<InvalidOperationException>(() => tree.WithGenericAttributes(tree.GetRoot().DescendantNodes().OfType<MarkdownListItemSyntax>().Single(), new MarkdownGenericAttributes("x", [], [])));
        Assert.Throws<InvalidOperationException>(() => withoutExtension.WithGenericAttributes(withoutExtension.GetRoot().Blocks[0], attributes: null));
    }

    private static readonly MarkdownParseOptions AttributeOptions = new() { Extensions = MarkdownExtensions.GenericAttributes | MarkdownExtensions.Mathematics | MarkdownExtensions.CustomContainers | MarkdownExtensions.PipeTables | MarkdownExtensions.GridTables | MarkdownExtensions.Footnotes };

    [Theory]
    [InlineData("# a #\n", 3, "### a #\n")]
    [InlineData("> ###### a\n", 1, "> # a\n")]
    [InlineData("a\n===\n", 2, "a\n---\n")]
    [InlineData("> a\n> -\n", 1, "> a\n> =\n")]
    public void HeadingWithLevel(string text, int level, string expected)
    {
        var root = MarkdownSyntaxTree.ParseText(text).GetRoot();
        var heading = root.DescendantNodes().OfType<MarkdownHeadingSyntax>().Single();

        var updated = root.ReplaceNode(heading, heading.WithLevel(level)).ToFullString();

        Assert.Equal(expected, updated);
        Assert.Equal(level, MarkdownSyntaxTree.ParseText(updated).GetRoot().DescendantNodes().OfType<MarkdownHeadingSyntax>().Single().Level);
    }

    [Theory]
    [InlineData("# a\n", 0)]
    [InlineData("# a\n", 7)]
    [InlineData("a\n===\n", 3)]
    public void HeadingWithInvalidLevelThrows(string text, int level)
    {
        var heading = MarkdownSyntaxTree.ParseText(text).GetRoot().Blocks.OfType<MarkdownHeadingSyntax>().Single();

        Assert.Throws<ArgumentOutOfRangeException>(nameof(level), () => heading.WithLevel(level));
    }

    [Fact]
    public void ReadmeExample_LinkWithDestination()
    {
        #region MarkdownLinkSyntax_WithDestination
        var tree = MarkdownSyntaxTree.ParseText("See [the docs](http://example.com/docs \"Docs\") and ![logo](logo.png).\n");

        // The destinations are escaped: a space needs angle brackets
        var links = tree.GetRoot().DescendantNodes().OfType<MarkdownLinkSyntax>();
        var root = tree.GetRoot().ReplaceNodes(links, (link, rewritten) => link.IsImage
            ? rewritten.WithDestination("images/my logo.png").WithTitle("Logo")
            : rewritten.WithDestination(link.Destination.Replace("http://", "https://", StringComparison.Ordinal)).WithTitle(null));

        Assert.Equal("See [the docs](https://example.com/docs) and ![logo](<images/my logo.png> \"Logo\").\n", root.ToFullString());
        #endregion
    }

    [Theory]
    [InlineData("[a](b)", "c d", "[a](<c d>)")]
    [InlineData("[a]()", "/u", "[a](/u)")]
    [InlineData("[a](b \"t\")", "(x", "[a](\\(x \"t\")")]
    [InlineData("[a](b)", "p|q", "[a](p\\|q)")]
    [InlineData("> [a](\n> b)", "c", "> [a](\n> c)")]
    public void LinkWithDestination(string text, string destination, string expected)
    {
        var root = MarkdownSyntaxTree.ParseText(text).GetRoot();
        var link = root.DescendantNodes().OfType<MarkdownLinkSyntax>().Single();

        var updated = root.ReplaceNode(link, link.WithDestination(destination)).ToFullString();

        Assert.Equal(expected, updated);
        Assert.Equal(destination, MarkdownSyntaxTree.ParseText(updated).GetRoot().DescendantNodes().OfType<MarkdownLinkSyntax>().Single().Destination);
    }

    [Theory]
    [InlineData("[a](b)", "new \"t\"", "[a](b \"new \\\"t\\\"\")")]
    [InlineData("[a]( b  \"t\" )", null, "[a]( b )")]
    [InlineData("[a]()", "t", "[a](<> \"t\")")]
    [InlineData("[a](b \"x\ny\")", "z", "[a](b \"z\")")]
    [InlineData("> [a](b \"x\n> y\") c", null, "> [a](b) c")]
    [InlineData("[a](b\\ \"t\")", null, "[a](b\\\\)")]
    [InlineData("| a |\n|---|\n| [x](y) |\n", "t|u", "| a |\n|---|\n| [x](y \"t\\|u\") |\n")]
    public void LinkWithTitle(string text, string? title, string expected)
    {
        var root = MarkdownSyntaxTree.ParseText(text, MarkdownParseOptions.GitHubFlavored).GetRoot();
        var link = root.DescendantNodes().OfType<MarkdownLinkSyntax>().Single();

        var updated = root.ReplaceNode(link, link.WithTitle(title)).ToFullString();

        Assert.Equal(expected, updated);
        Assert.Equal(title, MarkdownSyntaxTree.ParseText(updated, MarkdownParseOptions.GitHubFlavored).GetRoot().DescendantNodes().OfType<MarkdownLinkSyntax>().Single().Title);
    }

    [Fact]
    public void ReferenceLinkWithDestinationThrows()
    {
        var link = MarkdownSyntaxTree.ParseText("[a][r]\n\n[r]: /u\n").GetRoot().DescendantNodes().OfType<MarkdownLinkSyntax>().Single();

        Assert.Throws<InvalidOperationException>(() => link.WithDestination("x"));
        Assert.Throws<InvalidOperationException>(() => link.WithTitle("x"));
    }

    [Theory]
    [InlineData("[l]: /u \"t\"\n", "/v", "t", "[l]: /v \"t\"\n")]
    [InlineData("[l]:\n  /u\n  \"t\"\n", "/u", null, "[l]:\n  /u\n")]
    [InlineData("[l]: /u\n", "/u", "t", "[l]: /u \"t\"\n")]
    [InlineData("> [l]: /u\n> \"t\"\n>\n> x", "<v>", "nt", "> [l]: \\<v>\n> \"nt\"\n>\n> x")]
    public void LinkReferenceDefinitionWithDestinationAndTitle(string text, string destination, string? title, string expected)
    {
        var root = MarkdownSyntaxTree.ParseText(text).GetRoot();
        var definition = root.DescendantNodes().OfType<MarkdownLinkReferenceDefinitionSyntax>().Single();

        var updated = root.ReplaceNode(definition, definition.WithDestination(destination).WithTitle(title)).ToFullString();

        Assert.Equal(expected, updated);
        var reparsed = MarkdownSyntaxTree.ParseText(updated).GetRoot().DescendantNodes().OfType<MarkdownLinkReferenceDefinitionSyntax>().Single();
        Assert.Equal(destination, reparsed.Destination);
        Assert.Equal(title, reparsed.Title);
    }

    [Theory]
    [InlineData("```\ncode\n```\n", "cs", "```cs\ncode\n```\n")]
    [InlineData("``` cs\ncode\n```\n", null, "```\ncode\n```\n")]
    [InlineData("```cs title\ncode\n```\n", "a`b", "```a&#96;b\ncode\n```\n")]
    [InlineData("> ~~~\n> x\n> ~~~\n", " py ", "> ~~~py\n> x\n> ~~~\n")]
    [InlineData("```cs\ncode\n```\n", "  ", "```\ncode\n```\n")]
    [InlineData("~~~cs\ncode\n~~~\n\nafter\n", "~", "~~~&#126;\ncode\n~~~\n\nafter\n")]
    [InlineData("~~~\ncode\n~~~\n\nafter\n", "~x", "~~~&#126;x\ncode\n~~~\n\nafter\n")]
    public void CodeBlockWithInfo(string text, string? info, string expected)
    {
        var root = MarkdownSyntaxTree.ParseText(text).GetRoot();
        var codeBlock = root.DescendantNodes().OfType<MarkdownCodeBlockSyntax>().Single();

        var updated = root.ReplaceNode(codeBlock, codeBlock.WithInfo(info)).ToFullString();

        Assert.Equal(expected, updated);
        var reparsed = MarkdownSyntaxTree.ParseText(updated).GetRoot().DescendantNodes().OfType<MarkdownCodeBlockSyntax>().Single();
        Assert.Equal(string.IsNullOrWhiteSpace(info) ? null : info.Trim(), reparsed.Info);
        Assert.Equal(codeBlock.Code, reparsed.Code);
    }

    [Fact]
    public void CodeBlockWithInfoIsNotReadAsGenericAttributes()
    {
        var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GenericAttributes };
        var root = MarkdownSyntaxTree.ParseText("```\ncode\n```\n", options).GetRoot();
        var codeBlock = root.Blocks.OfType<MarkdownCodeBlockSyntax>().Single();

        var updated = root.ReplaceNode(codeBlock, codeBlock.WithInfo("cs {#sample}")).ToFullString();

        Assert.Equal("```cs &#123;#sample}\ncode\n```\n", updated);
        var reparsed = MarkdownSyntaxTree.ParseText(updated, options).GetRoot().Blocks.OfType<MarkdownCodeBlockSyntax>().Single();
        Assert.Equal("cs {#sample}", reparsed.Info);
        Assert.True(reparsed.GetGenericAttributes().IsEmpty);
    }

    [Fact]
    public void CodeBlockWithInvalidInfoThrows()
    {
        var blocks = MarkdownSyntaxTree.ParseText("```\ncode\n```\n\n    indented\n").GetRoot().Blocks.OfType<MarkdownCodeBlockSyntax>().ToList();

        Assert.Throws<ArgumentException>("info", () => blocks[0].WithInfo("a\nb"));
        Assert.Throws<InvalidOperationException>(() => blocks[1].WithInfo("cs"));
    }

    [Fact]
    public void WithMethodsKeepAnnotations()
    {
        var annotation = new SyntaxAnnotation("test");
        var root = MarkdownSyntaxTree.ParseText("# a\n\n[b](c)\n").GetRoot();
        var heading = root.DescendantNodes().OfType<MarkdownHeadingSyntax>().Single();
        var link = root.DescendantNodes().OfType<MarkdownLinkSyntax>().Single();
        root = root.ReplaceNodes(new MarkdownSyntaxNode[] { heading, link }, (_, rewritten) => rewritten.WithAdditionalAnnotations(annotation));

        heading = root.DescendantNodes().OfType<MarkdownHeadingSyntax>().Single();
        link = root.DescendantNodes().OfType<MarkdownLinkSyntax>().Single();

        Assert.True(heading.WithLevel(2).HasAnnotation(annotation));
        Assert.True(link.WithDestination("d").HasAnnotation(annotation));
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

    private static readonly MarkdownParseOptions AllExtensionsWithoutMarkdigTables = new() { Extensions = Enum.GetValues<MarkdownExtensions>().Aggregate(MarkdownExtensions.None, (all, extension) => all | extension) & ~MarkdownExtensions.PipeTables };

    #region MarkdownHtmlNodeRenderer_Chart
    // Writes the code blocks of the language "chart" as a <canvas> that a script draws
    private sealed class ChartRenderer : MarkdownHtmlNodeRenderer
    {
        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
        {
            if (node is not MarkdownCodeBlockSyntax { Info: "chart" } code)
            {
                return false;
            }

            writer.EnsureNewLine();
            writer.WriteRawHtml("<canvas class=\"chart\" data-spec=\"");
            writer.WriteAttributeValue(code.Code);
            writer.WriteRawHtml("\"></canvas>");

            // Plain text has the text of the chart
            if (writer.IsPlainText)
            {
                writer.WriteText(code.Code);
            }

            writer.EnsureNewLine();
            return true;
        }
    }
    #endregion

    #region MarkdownHtmlNodeRenderer_Default
    // Writes the custom containers named "details" as a <details> element, whose arguments are the summary
    private sealed class DetailsRenderer : MarkdownHtmlNodeRenderer
    {
        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
        {
            if (node is not MarkdownCustomContainerSyntax { Name: "details" } container)
            {
                return false;
            }

            writer.EnsureNewLine();
            writer.WriteRawHtml("<details");
            writer.WriteAttributes(container);
            writer.WriteRawHtml("><summary>");
            writer.WriteText(container.Arguments);
            writer.WriteRawHtml("</summary>\n");
            writer.WriteBlocks(container.Blocks);
            writer.EnsureNewLine();
            writer.WriteRawHtml("</details>\n");
            return true;
        }
    }

    // Writes the tables as usual, in a <div> that scrolls
    private sealed class ScrollableTableRenderer : MarkdownHtmlNodeRenderer
    {
        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
        {
            if (node is not MarkdownTableSyntax)
            {
                return false;
            }

            writer.EnsureNewLine();
            writer.WriteRawHtml("<div class=\"table-scroll\">\n");
            writer.WriteDefault(node);
            writer.EnsureNewLine();
            writer.WriteRawHtml("</div>\n");
            return true;
        }
    }
    #endregion

    private sealed class CommentAfterParagraphRenderer : MarkdownHtmlNodeRenderer
    {
        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
        {
            if (node is not MarkdownParagraphSyntax)
            {
                return false;
            }

            writer.WriteDefault(node);
            writer.WriteRawHtml("<!--x-->");
            return true;
        }
    }

    private sealed class DecliningRenderer : MarkdownHtmlNodeRenderer
    {
        public List<string> Offered { get; } = [];

        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
        {
            Offered.Add(node.GetType().Name["Markdown".Length..^"Syntax".Length]);
            return false;
        }
    }

    private sealed class DefaultRenderer : MarkdownHtmlNodeRenderer
    {
        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
        {
            writer.WriteDefault(node);
            return true;
        }
    }

    private sealed class ParagraphRenderer : MarkdownHtmlNodeRenderer
    {
        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
        {
            if (node is not MarkdownParagraphSyntax paragraph)
            {
                return false;
            }

            writer.EnsureNewLine();
            writer.WriteRawHtml("<div class=\"p\">");
            writer.WriteInlines(paragraph.Inlines);
            writer.WriteRawHtml("</div>\n");
            return true;
        }
    }

    // Writes "@name" at the end of a paragraph as a link to the user, with a name that needs escaping
    private sealed class MentionParagraphRenderer : MarkdownHtmlNodeRenderer
    {
        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
        {
            if (node is not MarkdownParagraphSyntax paragraph)
            {
                return false;
            }

            var text = paragraph.GetPlainText().Trim();
            var at = text.IndexOf('@', StringComparison.Ordinal);
            writer.WriteRawHtml("<p");
            writer.WriteAttributes(paragraph, "text");
            writer.WriteRawHtml(">");
            writer.WriteText(text.AsSpan(0, at));
            writer.WriteRawHtml("<a href=\"");
            writer.WriteUrl("users/" + text[(at + 1)..] + "é");
            writer.WriteRawHtml("\">");
            writer.WriteText(text[at..] + "<>");
            writer.WriteRawHtml("</a></p>");
            writer.EnsureNewLine();
            return true;
        }
    }

    private sealed class RowRenderer : MarkdownHtmlNodeRenderer
    {
        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
        {
            if (node is MarkdownTableSyntax table)
            {
                writer.WriteDefault(table.Rows[0]);
                return true;
            }

            return false;
        }
    }

    #region MarkdownInlineExtension_Mention
    // Reads @name, where the name is letters, digits and dashes, unless the @ follows a letter or a digit, as in an email address
    private sealed class MentionExtension(string name = "mention", string triggerCharacters = "@") : MarkdownInlineExtension(name, triggerCharacters)
    {
        public override bool TryParse(ref MarkdownInlineParseContext context)
        {
            if (char.IsLetterOrDigit(context.PreviousCharacter))
            {
                return false;
            }

            var length = 1;
            while (length < context.Text.Length && (char.IsAsciiLetterOrDigit(context.Text[length]) || context.Text[length] == '-'))
            {
                length++;
            }

            if (length == 1)
            {
                return false;
            }

            context.AddMarker(0, 1);
            context.AddText(1, length - 1);
            context.Complete(length);
            return true;
        }
    }

    // Writes the mentions as links to the page of the user
    private sealed class MentionRenderer : MarkdownHtmlNodeRenderer
    {
        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
        {
            if (node is not MarkdownExtensionInlineSyntax { Name: "mention" } mention)
            {
                return false;
            }

            writer.WriteRawHtml("<a class=\"mention\" href=\"");
            writer.WriteUrl("/users/" + mention.Text);
            writer.WriteRawHtml("\">");
            writer.WriteText("@" + mention.Text);
            writer.WriteRawHtml("</a>");
            return true;
        }
    }
    #endregion

    #region MarkdownDelimiterExtension_Highlight
    // Writes the constructs of the delimiter extensions as <mark> elements
    private sealed class HighlightRenderer : MarkdownHtmlNodeRenderer
    {
        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
        {
            if (node is not MarkdownExtensionInlineSyntax highlight || highlight.Markers is not [{ Text: ['%', ..] }, ..])
            {
                return false;
            }

            writer.WriteRawHtml("<mark>");
            writer.WriteInlines(highlight.Inlines);
            writer.WriteRawHtml("</mark>");
            return true;
        }
    }
    #endregion

    // {{content}}, whose content is inlines
    private sealed class SpanExtension() : MarkdownInlineExtension("span", "{")
    {
        public override bool TryParse(ref MarkdownInlineParseContext context)
        {
            var text = context.Text;
            if (!text.StartsWith("{{", StringComparison.Ordinal))
            {
                return false;
            }

            // The closing braces that balance the opening ones
            var depth = 0;
            for (var i = 0; i < text.Length - 1; i++)
            {
                if (text[i] == '{' && text[i + 1] == '{')
                {
                    depth++;
                    i++;
                }
                else if (text[i] == '}' && text[i + 1] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        context.AddMarker(0, 2);
                        if (i > 2)
                        {
                            context.AddInlines(2, i - 2);
                        }

                        context.AddMarker(i, 2);
                        context.Complete(i + 2);
                        return true;
                    }

                    i++;
                }
            }

            return false;
        }
    }

    private sealed class SpanRenderer : MarkdownHtmlNodeRenderer
    {
        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
        {
            if (node is not MarkdownExtensionInlineSyntax { Name: "span" } span)
            {
                return false;
            }

            writer.WriteRawHtml("<span>");
            writer.WriteInlines(span.Inlines);
            writer.WriteRawHtml("</span>");
            return true;
        }
    }

    // [[page]], whose text is the name of a page
    private sealed class WikiLinkExtension() : MarkdownInlineExtension("wiki", "[")
    {
        public override bool TryParse(ref MarkdownInlineParseContext context)
        {
            var text = context.Text;
            var end = text.IndexOf("]]", StringComparison.Ordinal);
            if (!text.StartsWith("[[", StringComparison.Ordinal) || end < 3)
            {
                return false;
            }

            context.AddMarker(0, 2);
            context.AddText(2, end - 2);
            context.AddMarker(end, 2);
            context.Complete(end + 2);
            return true;
        }
    }

    private delegate void ParseHandler(ref MarkdownInlineParseContext context);

    private delegate bool TryParseHandler(ref MarkdownInlineParseContext context);

    private sealed class ParseHandlerExtension(string name, string triggerCharacters, TryParseHandler handler) : MarkdownInlineExtension(name, triggerCharacters)
    {
        public override bool TryParse(ref MarkdownInlineParseContext context) => handler(ref context);
    }

    #region MarkdownFencedBlockExtension_Admonition
    // !!! kind, the blocks of the admonition, then !!!
    private sealed class AdmonitionExtension() : MarkdownFencedBlockExtension("admonition", "!", MarkdownFencedBlockContent.Blocks)
    {
        public override int TryOpen(ReadOnlySpan<char> line) => line.StartsWith("!!!", StringComparison.Ordinal) ? 3 : 0;

        public override int TryClose(ReadOnlySpan<char> line, ReadOnlySpan<char> openingFence) => line.StartsWith(openingFence, StringComparison.Ordinal) ? openingFence.Length : 0;
    }

    // Writes the admonitions as a <div> whose classes are the kind of the admonition
    private sealed class AdmonitionRenderer : MarkdownHtmlNodeRenderer
    {
        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
        {
            if (node is not MarkdownExtensionBlockSyntax { Name: "admonition" } admonition)
            {
                return false;
            }

            writer.EnsureNewLine();
            writer.WriteRawHtml("<div");
            writer.WriteAttributes(admonition, "admonition " + admonition.Info.Replace("*", "", StringComparison.Ordinal));
            writer.WriteRawHtml(">\n");
            writer.WriteBlocks(admonition.Blocks);
            writer.EnsureNewLine();
            writer.WriteRawHtml("</div>\n");
            return true;
        }
    }
    #endregion

    // A fence of pilcrows, which a backslash cannot escape: the blocks are between two lines that start with three of them
    private sealed class PilcrowFenceExtension() : MarkdownFencedBlockExtension("pilcrow", "\u00B6", MarkdownFencedBlockContent.Blocks)
    {
        public override int TryOpen(ReadOnlySpan<char> line) => line.StartsWith("\u00B6\u00B6\u00B6", StringComparison.Ordinal) ? 3 : 0;

        public override int TryClose(ReadOnlySpan<char> line, ReadOnlySpan<char> openingFence) => line.StartsWith(openingFence, StringComparison.Ordinal) ? openingFence.Length : 0;
    }

    // %%% info, raw lines, then %%%
    private sealed class RawFenceExtension() : MarkdownFencedBlockExtension("raw", "%", MarkdownFencedBlockContent.RawLines)
    {
        public override int TryOpen(ReadOnlySpan<char> line) => line.StartsWith("%%%", StringComparison.Ordinal) ? 3 : 0;

        public override int TryClose(ReadOnlySpan<char> line, ReadOnlySpan<char> openingFence) => line.StartsWith(openingFence, StringComparison.Ordinal) ? openingFence.Length : 0;
    }

    #region MarkdownParagraphExtension_Transcript
    // The lines "Speaker: message" of a paragraph: the first run of them is a transcript, whose speakers are text and whose
    // messages are inlines
    private sealed class TranscriptExtension() : MarkdownParagraphExtension("transcript")
    {
        public override bool TryConvert(ref MarkdownParagraphConvertContext context)
        {
            var first = -1;
            var count = 0;
            for (var i = 0; i < context.LineCount; i++)
            {
                var line = context.GetLine(i);
                var colon = line.IndexOf(": ", StringComparison.Ordinal);
                var speaker = colon > 0 ? line[..colon] : [];
                if (speaker.IsEmpty || speaker.ContainsAnyExceptInRange('A', 'z') || speaker.ContainsAnyInRange('[', '`'))
                {
                    if (first >= 0)
                    {
                        break;
                    }

                    continue;
                }

                if (first < 0)
                {
                    first = i;
                }

                context.AddText(i, 0, colon);
                context.AddMarker(i, colon, 1);
                context.AddInlines(i, colon + 2, line.Length - colon - 2);
                count++;
            }

            // A transcript has two lines or more
            if (count < 2)
            {
                return false;
            }

            context.Complete(first, count);
            return true;
        }
    }

    // Writes a transcript as a <dl>: the speakers are the terms, and the messages the definitions
    private sealed class TranscriptRenderer : MarkdownHtmlNodeRenderer
    {
        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
        {
            if (node is not MarkdownExtensionBlockSyntax { Name: "transcript" } transcript)
            {
                return false;
            }

            writer.EnsureNewLine();
            writer.WriteRawHtml("<dl class=\"transcript\">\n");
            var message = new List<SyntaxNodeOrToken>();
            foreach (var child in transcript.ChildNodesAndTokens())
            {
                if (child.IsToken && child.AsToken().RawKind == (int)SyntaxKind.ExtensionTextToken)
                {
                    WriteMessage(writer, message);
                    writer.WriteRawHtml("<dt>");
                    writer.WriteText(child.AsToken().Text);
                    writer.WriteRawHtml("</dt>");
                }
                else if (!(child.IsToken && child.AsToken().RawKind == (int)SyntaxKind.ExtensionMarkerToken))
                {
                    message.Add(child);
                }
            }

            WriteMessage(writer, message);
            writer.WriteRawHtml("</dl>\n");
            return true;
        }

        private static void WriteMessage(MarkdownHtmlWriter writer, List<SyntaxNodeOrToken> message)
        {
            if (message.Count > 0)
            {
                writer.WriteRawHtml("<dd>");
                writer.WriteInlines(message);
                writer.WriteRawHtml("</dd>\n");
                message.Clear();
            }
        }
    }
    #endregion

    #region MarkdownSyntaxExtension_Glossary
    // %%% glossary, then lines "Term: definition": the definitions are recorded in the state of the document
    private sealed class GlossaryExtension() : MarkdownFencedBlockExtension("glossary", "%", MarkdownFencedBlockContent.RawLines)
    {
        public override object? CreateState() => new Dictionary<string, string>(StringComparer.Ordinal);

        public override int TryOpen(ReadOnlySpan<char> line) => line.StartsWith("%%% glossary", StringComparison.Ordinal) ? 3 : 0;

        public override int TryClose(ReadOnlySpan<char> line, ReadOnlySpan<char> openingFence) => line.StartsWith("%%%", StringComparison.Ordinal) ? 3 : 0;

        public override void OnClosed(ref MarkdownFencedBlockContext context)
        {
            var definitions = (Dictionary<string, string>)context.State!;
            for (var i = 0; i < context.LineCount; i++)
            {
                var line = context.GetLine(i);
                if (line.IndexOf(": ", StringComparison.Ordinal) is > 0 and var colon)
                {
                    definitions[line[..colon].ToString()] = line[(colon + 2)..].ToString();
                }
            }
        }
    }

    // [Term], when the glossary defines the term: other brackets are read as usual, such as the ones of a link
    private sealed class GlossaryTermExtension(GlossaryExtension glossary) : MarkdownInlineExtension("glossary-term", "[")
    {
        public override bool TryParse(ref MarkdownInlineParseContext context)
        {
            var end = context.Text.IndexOf(']');
            var definitions = (Dictionary<string, string>)context.GetState(glossary)!;
            if (end < 2 || (end + 1 < context.Text.Length && context.Text[end + 1] is '(' or '[') || !definitions.ContainsKey(context.Text[1..end].ToString()))
            {
                return false;
            }

            context.AddMarker(0, 1);
            context.AddText(1, end - 1);
            context.AddMarker(end, 1);
            context.Complete(end + 1);
            return true;
        }
    }

    // Writes the terms as <abbr> elements, and nothing for the glossary
    private sealed class GlossaryRenderer : MarkdownHtmlNodeRenderer
    {
        public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
        {
            switch (node)
            {
                case MarkdownExtensionBlockSyntax { Name: "glossary" }:
                    return true;

                case MarkdownExtensionInlineSyntax { Name: "glossary-term" } term:
                    var glossary = term.SyntaxTree!.GetRoot().DescendantNodes().OfType<MarkdownExtensionBlockSyntax>().Where(block => block.Name == "glossary");
                    var definition = glossary.SelectMany(block => block.Content.Split('\n')).FirstOrDefault(line => line.StartsWith(term.Text + ": ", StringComparison.Ordinal));
                    writer.WriteRawHtml("<abbr title=\"");
                    writer.WriteAttributeValue(definition is null ? "" : definition[(term.Text.Length + 2)..]);
                    writer.WriteRawHtml("\">");
                    writer.WriteText(term.Text);
                    writer.WriteRawHtml("</abbr>");
                    return true;

                default:
                    return false;
            }
        }
    }
    #endregion

    // Counts the states it creates, and the paragraphs it is offered, through its state
    private sealed class CountingStateExtension() : MarkdownParagraphExtension("counting")
    {
        public int StateCount { get; private set; }

        public int ParagraphCount { get; private set; }

        public override object? CreateState()
        {
            StateCount++;
            return new object();
        }

        public override bool TryConvert(ref MarkdownParagraphConvertContext context)
        {
            Assert.NotNull(context.State);
            ParagraphCount++;
            return false;
        }
    }

    private delegate void ConvertHandler(ref MarkdownParagraphConvertContext context);

    private delegate bool TryConvertHandler(ref MarkdownParagraphConvertContext context);

    private sealed class ConvertHandlerExtension(TryConvertHandler handler) : MarkdownParagraphExtension("convert")
    {
        public override bool TryConvert(ref MarkdownParagraphConvertContext context) => handler(ref context);
    }
}
