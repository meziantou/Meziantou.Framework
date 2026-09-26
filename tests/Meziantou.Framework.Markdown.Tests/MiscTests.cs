using System.Text.RegularExpressions;

using Meziantou.Framework.Markdown.Extensions.AutoLinks;
using Meziantou.Framework.Markdown.Extensions.Tables;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Tests;

public class MiscTests
{
    [Fact]
    public void LinkWithInvalidNonAsciiDomainNameIsIgnored()
    {
        // Url from https://github.com/lunet-io/markdig/issues/438
        _ = MarkdownConverter.ToHtml("[minulém díle](http://V%20minulém%20díle%20jsme%20nainstalovali%20SQL%20Server,%20který%20je%20nutný%20pro%20běh%20Configuration%20Manageru.%20Dnes%20nás%20čeká%20instalace%20WSUS,%20což%20je%20produkt,%20jež%20je%20možné%20používat%20i%20jako%20samostatnou%20funkci%20ve%20Windows%20Serveru,%20který%20se%20stará%20o%20stažení%20a%20instalaci%20aktualizací%20z%20Microsoft%20Update%20na%20klientské%20počítače.%20Stejně%20jako%20v%20předchozích%20dílech,%20tak%20i%20v%20tomto%20si%20ukážeme%20obě%20varianty%20instalace%20–%20a%20to%20jak%20instalaci%20z%20PowerShellu,%20tak%20instalaci%20pomocí%20GUI.) ");

        // Valid IDN
        TestParser.TestSpec("[foo](http://ünicode.com)", "<p><a href=\"http://xn--nicode-2ya.com\">foo</a></p>");
        TestParser.TestSpec("[foo](http://ünicode.ünicode.com)", "<p><a href=\"http://xn--nicode-2ya.xn--nicode-2ya.com\">foo</a></p>");

        // Invalid IDN
        TestParser.TestSpec("[foo](http://ünicode..com)", "<p><a href=\"http://%C3%BCnicode..com\">foo</a></p>");
    }

    [Theory]
    [InlineData("link [foo [bar]]")] // https://spec.commonmark.org/0.29/#example-508
    [InlineData("link [foo][bar]")]
    [InlineData("link [][foo][bar][]")]
    [InlineData("link [][foo][bar][[]]")]
    [InlineData("link [foo] [bar]")]
    [InlineData("link [[foo] [] [bar] [[abc]def]]")]
    [InlineData("[]")]
    [InlineData("[ ]")]
    [InlineData("[bar][]")]
    [InlineData("[bar][ foo]")]
    [InlineData("[bar][foo ][]")]
    [InlineData("[bar][fo[ ]o ][][]")]
    [InlineData("[a]b[c[d[e]f]g]h")]
    [InlineData("a[b[c[d]e]f[g]h]i foo [j]k[l[m]n]o")]
    [InlineData("a[b[c[d]e]f[g]h]i[] [][foo][bar][] foo [j]k[l[m]n]o")]
    [InlineData("a[b[c[d]e]f[g]h]i foo [j]k[l[m]n]o[][]")]
    public void LinkTextMayContainBalancedBrackets(string linkText)
    {
        string markdown = $"[{linkText}](/uri)";
        string expected = $@"<p><a href=""/uri"">{linkText}</a></p>";

        TestParser.TestSpec(markdown, expected);

        // Make the link text unbalanced
        foreach (var bracketIndex in linkText
            .Select((c, i) => (Char: c, Index: i))
            .Where(t => t.Char == '[' || t.Char == ']')
            .Select(t => t.Index))
        {
            string brokenLinkText = linkText.Remove(bracketIndex, 1);

            markdown = $"[{brokenLinkText}](/uri)";
            expected = $@"<p><a href=""/uri"">{brokenLinkText}</a></p>";

            string actual = MarkdownConverter.ToHtml(markdown);
            Assert.NotEqual(expected, actual);
        }
    }

    [Theory]
    [InlineData('[', 9 * 1024, true, false)]
    [InlineData('[', 11 * 1024, true, true)]
    [InlineData('[', 100, false, false)]
    [InlineData('[', 150, false, true)]
    [InlineData('>', 100, true, false)]
    [InlineData('>', 150, true, true)]
    public void GuardsAgainstHighlyNestedNodes(char c, int count, bool parseOnly, bool shouldThrow)
    {
        var markdown = new string(c, count);
        Action test = parseOnly ? () => MarkdownConverter.Parse(markdown) : () => MarkdownConverter.ToHtml(markdown);

        if (shouldThrow)
        {
            Exception e = Assert.Throws<ArgumentException>(test);
            Assert.Contains("depth limit", e.Message);
        }
        else
        {
            test();
        }
    }

    [Theory]
    [InlineData("*[<svg/onload=alert(1)>]: x\n\nhello <svg/onload=alert(1)>", "<p>hello <abbr title=\"x\">&lt;svg/onload=alert(1)&gt;</abbr></p>\n")]
    [InlineData("*[R&D]: Research and Development\n\nR&D team", "<p><abbr title=\"Research and Development\">R&amp;D</abbr> team</p>\n")]
    public void AbbreviationLabelIsEscaped(string markdown, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder().UseAbbreviations().DisableHtml().Build();

        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown, pipeline));
    }

    [Theory]
    [InlineData("+-----+\nsome text\n", "<p>+-----+\nsome text</p>\n")]
    [InlineData("+---+\n+---+\n", "<p>+---+\n+---+</p>\n")]
    [InlineData("Total\n+----+\n+----+\n\nNext paragraph\n", "<p>Total</p>\n<p>+----+\n+----+</p>\n<p>Next paragraph</p>\n")]
    [InlineData("+----+\n\n\ntext\n", "<p>+----+</p>\n<p>text</p>\n")]
    public void InvalidGridTableIsRenderedAsParagraph(string markdown, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder().UseGridTables().Build();

        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown, pipeline));
    }

    [Fact]
    public void InvalidGridTableParagraphHasTheTablePosition()
    {
        var pipeline = new MarkdownPipelineBuilder().UseGridTables().UsePreciseSourceLocation().Build();
        var document = MarkdownConverter.Parse("a\n\n+-----+\nsome text\n", pipeline);

        var paragraph = Assert.IsType<ParagraphBlock>(document[1]);
        Assert.Equal(2, paragraph.Line);
        Assert.Equal(3, paragraph.Span.Start);
        Assert.Equal(19, paragraph.Span.End);
    }

    [Fact]
    public void PooledRendererIsRestoredAfterAnExceptionInCustomWriter()
    {
        const string Markdown = "- a\n- ![alt *x*](i.png)\n\n> - q\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\nend";
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();
        var expected = MarkdownConverter.ToHtml(Markdown, pipeline);

        for (var length = 0; length < expected.Length; length++)
        {
            Assert.Throws<IOException>(() => MarkdownConverter.ToHtml(Markdown, new ThrowingWriter(length), pipeline));

            using var writer = new StringWriter();
            MarkdownConverter.ToHtml(Markdown, writer, pipeline);
            Assert.Equal(expected, writer.ToString(), message: $"Output after a failure at character {length}");
        }
    }

    [Fact]
    public void PooledRendererIsRestoredAfterAnExceptionInRenderer()
    {
        var deepList = string.Concat(Enumerable.Range(0, 200).Select(i => new string(' ', i * 2) + "- a\n"));
        var deepPipeline = new MarkdownPipelineBuilder { MaximumNestingDepth = 1000 }.Build();
        var document = MarkdownConverter.Parse(deepList, deepPipeline);

        var pipeline = new MarkdownPipelineBuilder().Build();
        Assert.Throws<ArgumentException>(() => document.ToHtml(pipeline));

        Assert.Equal("<p>hello</p>\n<blockquote>\n<p>quote</p>\n</blockquote>\n", MarkdownConverter.ToHtml("hello\n\n> quote", pipeline));
    }

    private sealed class ThrowingWriter(int maxLength) : StringWriter
    {
        private int _length;

        public override void Write(char value)
        {
            if (_length++ >= maxLength)
                throw new IOException("The writer is closed");

            base.Write(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            for (var i = 0; i < count; i++)
            {
                Write(buffer[index + i]);
            }
        }

        public override void Write(ReadOnlySpan<char> buffer)
        {
            foreach (var c in buffer)
            {
                Write(c);
            }
        }

        public override void Write(string? value)
        {
            Write(value.AsSpan());
        }
    }

    [Fact]
    public void MaximumNestingDepthCanBeRaisedForDeepListExtras()
    {
        var markdown = "Krankenhaus\nD. " + string.Join(" ", Enumerable.Repeat("M.", 160));
        var pipeline = new MarkdownPipelineBuilder
        {
            MaximumNestingDepth = 512,
        }.UseListExtras().Build();

        Assert.DoesNotThrow(() => MarkdownConverter.ToHtml(markdown, pipeline));
    }

    [Fact]
    public void MaximumNestingDepthCanBeLowered()
    {
        var pipeline = new MarkdownPipelineBuilder
        {
            MaximumNestingDepth = 16,
        }.Build();

        Exception e = Assert.Throws<ArgumentException>(() => MarkdownConverter.ToHtml(new string('>', 20), pipeline));
        Assert.Contains("depth limit", e.Message);
    }

    [Fact]
    public void IsIssue356Corrected()
    {
        string input = @"https://foo.bar/path/\#m4mv5W0GYKZpGvfA.97";
        string expected = @"<p><a href=""https://foo.bar/path/%5C#m4mv5W0GYKZpGvfA.97"">https://foo.bar/path/\#m4mv5W0GYKZpGvfA.97</a></p>";

        TestParser.TestSpec($"<{input}>", expected);
        TestParser.TestSpec(input, expected, "autolinks|advanced");
    }

    [Fact]
    public void IsIssue365Corrected()
    {
        // The scheme must be escaped too...
        string input = "![image](\"onclick=\"alert&amp;#40;'click'&amp;#41;\"://)";
        string expected = "<p><img src=\"%22onclick=%22alert&amp;#40;%27click%27&amp;#41;%22://\" alt=\"image\" /></p>";

        TestParser.TestSpec(input, expected);
    }

    [Fact]
    public void TestAltTextIsCorrectlyEscaped()
    {
        TestParser.TestSpec(
            @"![This is image alt text with quotation ' and double quotation ""hello"" world](girl.png)",
            @"<p><img src=""girl.png"" alt=""This is image alt text with quotation ' and double quotation &quot;hello&quot; world"" /></p>");
    }

    [Fact]
    public void TestFixHang()
    {
        var input = File.ReadAllText(Path.Combine(TestParser.TestsDirectory, "hang.md"));
        _ = MarkdownConverter.ToHtml(input);
    }

    [Fact]
    public void TestInvalidHtmlEntity()
    {
        var input = "9&ddr;&*&ddr;&de��__";
        TestParser.TestSpec(input, "<p>9&amp;ddr;&amp;*&amp;ddr;&amp;de��__</p>");
    }

    [Fact]
    public void TestInvalidCharacterHandling()
    {
        var input = File.ReadAllText(Path.Combine(TestParser.TestsDirectory, "ArgumentOutOfRangeException.md"));
        _ = MarkdownConverter.ToHtml(input);
    }

    [Fact]
    public void TestInvalidCodeEscape()
    {
        var input = "```**Header**	";
        _ = MarkdownConverter.ToHtml(input);
    }

    [Fact]
    public void TestEmphasisAndHtmlEntity()
    {
        var markdownText = "*Unlimited-Fun&#174;*&#174;";
        TestParser.TestSpec(markdownText, "<p><em>Unlimited-Fun®</em>®</p>");
    }

    [Fact]
    public void TestThematicInsideCodeBlockInsideList()
    {
        var input = @"1. In the :

   ```
   Id                                   DisplayName         Description
   --                                   -----------         -----------
   62375ab9-6b52-47ed-826b-58e47e0e304b Group.Unified       ...
   ```";
        TestParser.TestSpec(input, @"<ol>
<li><p>In the :</p>
<pre><code>Id                                   DisplayName         Description
--                                   -----------         -----------
62375ab9-6b52-47ed-826b-58e47e0e304b Group.Unified       ...
</code></pre></li>
</ol>");
    }

    [Fact]
    public void VisualizeMathExpressions()
    {
        string math = @"Math expressions

$\frac{n!}{k!(n-k)!} = \binom{n}{k}$

$$\frac{n!}{k!(n-k)!} = \binom{n}{k}$$

$$
\frac{n!}{k!(n-k)!} = \binom{n}{k}
$$

<div class=""math"">
\begin{align}
\sqrt{37} & = \sqrt{\frac{73^2-1}{12^2}} \\
 & = \sqrt{\frac{73^2}{12^2}\cdot\frac{73^2-1}{73^2}} \\
 & = \sqrt{\frac{73^2}{12^2}}\sqrt{\frac{73^2-1}{73^2}} \\
 & = \frac{73}{12}\sqrt{1 - \frac{1}{73^2}} \\
 & \approx \frac{73}{12}\left(1 - \frac{1}{2\cdot73^2}\right)
\end{align}
</div>
";
        //Console.WriteLine("Math Expressions:\n");
        var pl = new MarkdownPipelineBuilder().UseMathematics().Build(); // UseEmphasisExtras(EmphasisExtraOptions.Subscript).Build()
        _ = MarkdownConverter.ToHtml(math, pl);
        //Console.WriteLine(html);
    }

    [Fact]
    public void InlineMathExpression()
    {
        string math = @"Math expressions

$\frac{n!}{k!(n-k)!} = \binom{n}{k}$
";
        var pl = new MarkdownPipelineBuilder().UseMathematics().Build(); // UseEmphasisExtras(EmphasisExtraOptions.Subscript).Build()

        var html = MarkdownConverter.ToHtml(math, pl);

        var test1 = html.Contains("<p><span class=\"math\">\\(", StringComparison.Ordinal);
        var test2 = html.Contains("\\)</span></p>", StringComparison.Ordinal);
        if (!test1 || !test2)
        {
            Console.WriteLine(html);
        }

        Assert.True(test1, message: "Leading bracket missing");
        Assert.True(test2, message: "Trailing bracket missing");
    }

    [Fact]
    public void BlockMathExpression()
    {
        string math = @"Math expressions

$$
\frac{n!}{k!(n-k)!} = \binom{n}{k}
$$
";
        var pl = new MarkdownPipelineBuilder().UseMathematics().Build(); // UseEmphasisExtras(EmphasisExtraOptions.Subscript).Build()

        var html = MarkdownConverter.ToHtml(math, pl);
        var test1 = html.Contains("<div class=\"math\">\n\\[", StringComparison.Ordinal);
        var test2 = html.Contains("\\]</div>", StringComparison.Ordinal);
        if (!test1 || !test2)
        {
            Console.WriteLine(html);
        }

        Assert.True(test1, message: "Leading bracket missing");
        Assert.True(test2, message: "Trailing bracket missing");
    }

    [Fact]
    public void CanDisableParsingHeadings()
    {
        var noHeadingsPipeline = new MarkdownPipelineBuilder().DisableHeadings().Build();

        TestParser.TestSpec("Foo\n===", "<h1>Foo</h1>");
        TestParser.TestSpec("Foo\n===", "<p>Foo\n===</p>", noHeadingsPipeline);

        TestParser.TestSpec("# Heading 1", "<h1>Heading 1</h1>");
        TestParser.TestSpec("# Heading 1", "<p># Heading 1</p>", noHeadingsPipeline);

        // Does not also disable link reference definitions
        TestParser.TestSpec("[Foo]\n\n[Foo]: bar", "<p><a href=\"bar\">Foo</a></p>");
        TestParser.TestSpec("[Foo]\n\n[Foo]: bar", "<p><a href=\"bar\">Foo</a></p>", noHeadingsPipeline);
    }

    [Fact]
    public void CanOpenAutoLinksInNewWindow()
    {
        var pipeline = new MarkdownPipelineBuilder().UseAutoLinks().Build();
        var newWindowPipeline = new MarkdownPipelineBuilder().UseAutoLinks(new AutoLinkOptions() { OpenInNewWindow = true }).Build();

        TestParser.TestSpec("www.foo.bar", "<p><a href=\"http://www.foo.bar\">www.foo.bar</a></p>", pipeline);
        TestParser.TestSpec("www.foo.bar", "<p><a href=\"http://www.foo.bar\" target=\"_blank\">www.foo.bar</a></p>", newWindowPipeline);
    }

    [Fact]
    public void CanUseHttpsPrefixForWWWAutoLinks()
    {
        var pipeline = new MarkdownPipelineBuilder().UseAutoLinks().Build();
        var httpsPipeline = new MarkdownPipelineBuilder().UseAutoLinks(new AutoLinkOptions() { UseHttpsForWWWLinks = true }).Build();

        TestParser.TestSpec("www.foo.bar", "<p><a href=\"http://www.foo.bar\">www.foo.bar</a></p>", pipeline);
        TestParser.TestSpec("www.foo.bar", "<p><a href=\"https://www.foo.bar\">www.foo.bar</a></p>", httpsPipeline);
    }

    [Fact]
    public void RootInlineHasCorrectSourceSpan()
    {
        var pipeline = new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build();
        pipeline.TrackTrivia = true;

        var document = MarkdownConverter.Parse("0123456789\n", pipeline);

        var expectedSourceSpan = new SourceSpan(0, 10);
        Assert.Equal(expectedSourceSpan, ((LeafBlock)document.LastChild!).Inline!.Span);
    }

    [Fact]
    public void RootInlineInTableCellHasCorrectSourceSpan()
    {
        var pipeline = new MarkdownPipelineBuilder().UsePreciseSourceLocation().UseAdvancedExtensions().Build();
        pipeline.TrackTrivia = true;

        var document = MarkdownConverter.Parse("| a | b |\n| --- | --- |\n| <span id=\"dest\"></span><span id=\"DEST\"></span>*dest*<br/> | \\[in\\] The address of the result of the operation.<br/> |", pipeline);

        var paragraph = (ParagraphBlock)((TableCell)((TableRow)((Table)document.LastChild!).LastChild!).First()).LastChild!;
        Assert.Equal(paragraph.Inline!.Span.Start, paragraph.Inline.FirstChild!.Span.Start);
        Assert.Equal(paragraph.Inline.Span.End, paragraph.Inline.LastChild!.Span.End);
    }

    [Fact]
    public void TestGridTableShortLine()
    {
        var input = @"
+--+
|  |
+-";

        var expected = @"<table>
<col style=""width:100%"" />
<tbody>
<tr>
<td></td>
</tr>
</tbody>
</table>
";
        TestParser.TestSpec(input, expected, new MarkdownPipelineBuilder().UseGridTables().Build());
    }

    [Fact]
    public void TestDefinitionListInListItemWithBlankLine()
    {
        var input = "\n- \n\n  term\n  :   definition\n";

        var expected = @"<ul>
<li>
<dl>
<dt>term</dt>
<dd>definition</dd>
</dl>
</li>
</ul>
";
        TestParser.TestSpec(input, expected, new MarkdownPipelineBuilder().UseDefinitionLists().Build());
    }

    [Fact]
    public void TestAlertWithinAlertOrNestedBlock()
    {
        var input = @"
>[!NOTE]
[!NOTE]
The second one is not a note.

>>[!NOTE]
Also not a note.
";

        var expected = @"<div class=""markdown-alert markdown-alert-note"">
<p class=""markdown-alert-title""><svg viewBox=""0 0 16 16"" version=""1.1"" width=""16"" height=""16"" aria-hidden=""true""><path d=""M0 8a8 8 0 1 1 16 0A8 8 0 0 1 0 8Zm8-6.5a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13ZM6.5 7.75A.75.75 0 0 1 7.25 7h1a.75.75 0 0 1 .75.75v2.75h.25a.75.75 0 0 1 0 1.5h-2a.75.75 0 0 1 0-1.5h.25v-2h-.25a.75.75 0 0 1-.75-.75ZM8 6a1 1 0 1 1 0-2 1 1 0 0 1 0 2Z""></path></svg>Note</p>
<p>[!NOTE]
The second one is not a note.</p>
</div>
<blockquote>
<blockquote>
<p>[!NOTE]
Also not a note.</p>
</blockquote>
</blockquote>
";
        TestParser.TestSpec(input, expected, new MarkdownPipelineBuilder().UseAlertBlocks().Build());
    }

    [Fact]
    public void TestNestedAlertInsideBlockquote()
    {
        // >>[!NOTE] should become a blockquote wrapping an alert when AllowNestedAlerts is enabled
        var input = @">>[!NOTE]
Also a note.
";

        var expected = @"<blockquote>
<div class=""markdown-alert markdown-alert-note"">
<p class=""markdown-alert-title""><svg viewBox=""0 0 16 16"" version=""1.1"" width=""16"" height=""16"" aria-hidden=""true""><path d=""M0 8a8 8 0 1 1 16 0A8 8 0 0 1 0 8Zm8-6.5a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13ZM6.5 7.75A.75.75 0 0 1 7.25 7h1a.75.75 0 0 1 .75.75v2.75h.25a.75.75 0 0 1 0 1.5h-2a.75.75 0 0 1 0-1.5h.25v-2h-.25a.75.75 0 0 1-.75-.75ZM8 6a1 1 0 1 1 0-2 1 1 0 0 1 0 2Z""></path></svg>Note</p>
<p>Also a note.</p>
</div>
</blockquote>
";
        TestParser.TestSpec(input, expected, new MarkdownPipelineBuilder().UseAlertBlocks(allowNestedAlerts: true).Build());
    }

    [Fact]
    public void TestNestedAlertInsideAlert()
    {
        // An alert inside another alert is never recognized, even with AllowNestedAlerts
        var input = @">[!NOTE]
> >[!TIP]
> > A tip inside a note
";

        var expected = @"<div class=""markdown-alert markdown-alert-note"">
<p class=""markdown-alert-title""><svg viewBox=""0 0 16 16"" version=""1.1"" width=""16"" height=""16"" aria-hidden=""true""><path d=""M0 8a8 8 0 1 1 16 0A8 8 0 0 1 0 8Zm8-6.5a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13ZM6.5 7.75A.75.75 0 0 1 7.25 7h1a.75.75 0 0 1 .75.75v2.75h.25a.75.75 0 0 1 0 1.5h-2a.75.75 0 0 1 0-1.5h.25v-2h-.25a.75.75 0 0 1-.75-.75ZM8 6a1 1 0 1 1 0-2 1 1 0 0 1 0 2Z""></path></svg>Note</p>
<p></p>
<blockquote>
<p>[!TIP]
A tip inside a note</p>
</blockquote>
</div>
";
        TestParser.TestSpec(input, expected, new MarkdownPipelineBuilder().UseAlertBlocks(allowNestedAlerts: true).Build());
    }

    [Fact]
    public void TestAlertInsideListItem()
    {
        // Alerts inside list items require AllowNestedAlerts
        var input = @"- > [!NOTE]
  > A note inside a list item
";

        var expected = @"<ul>
<li>
<div class=""markdown-alert markdown-alert-note"">
<p class=""markdown-alert-title""><svg viewBox=""0 0 16 16"" version=""1.1"" width=""16"" height=""16"" aria-hidden=""true""><path d=""M0 8a8 8 0 1 1 16 0A8 8 0 0 1 0 8Zm8-6.5a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13ZM6.5 7.75A.75.75 0 0 1 7.25 7h1a.75.75 0 0 1 .75.75v2.75h.25a.75.75 0 0 1 0 1.5h-2a.75.75 0 0 1 0-1.5h.25v-2h-.25a.75.75 0 0 1-.75-.75ZM8 6a1 1 0 1 1 0-2 1 1 0 0 1 0 2Z""></path></svg>Note</p>
<p>A note inside a list item</p>
</div>
</li>
</ul>
";
        TestParser.TestSpec(input, expected, new MarkdownPipelineBuilder().UseAlertBlocks(allowNestedAlerts: true).Build());
    }

    [Fact]
    public void TestAlertInsideNestedListItem()
    {
        // Alert inside a nested list item (list item indented under another list item) requires AllowNestedAlerts
        var input = @"- list item 1
- list item 2
  - > [!NOTE]
    > A note inside a nested list item
";

        var expected = @"<ul>
<li>list item 1</li>
<li>list item 2
<ul>
<li>
<div class=""markdown-alert markdown-alert-note"">
<p class=""markdown-alert-title""><svg viewBox=""0 0 16 16"" version=""1.1"" width=""16"" height=""16"" aria-hidden=""true""><path d=""M0 8a8 8 0 1 1 16 0A8 8 0 0 1 0 8Zm8-6.5a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13ZM6.5 7.75A.75.75 0 0 1 7.25 7h1a.75.75 0 0 1 .75.75v2.75h.25a.75.75 0 0 1 0 1.5h-2a.75.75 0 0 1 0-1.5h.25v-2h-.25a.75.75 0 0 1-.75-.75ZM8 6a1 1 0 1 1 0-2 1 1 0 0 1 0 2Z""></path></svg>Note</p>
<p>A note inside a nested list item</p>
</div>
</li>
</ul>
</li>
</ul>
";
        TestParser.TestSpec(input, expected, new MarkdownPipelineBuilder().UseAlertBlocks(allowNestedAlerts: true).Build());
    }

    [Fact]
    public void TestIssue887ListAfterNestedBlockQuote()
    {
        var input = @"> >* _Unordered 1_ QL2 level 1 no space sep
> >  Continued 1
> > * Unordered 2 level 1
> > 1. **Ordered 1** QL2 level 1
> >    Continued 1
> > > Not continued QL3
> > 2. Ordered 2 QL2 level 1
> > Continued 2a
> >
> >    Continued 2b
> >
> >    Continued 2c
> >
> >    3. Ordered 3 QL2 level 2
> >       Continued 3a
> >
> >       Continued 3b
";

        var expected = @"<blockquote>
<blockquote>
<ul>
<li><em>Unordered 1</em> QL2 level 1 no space sep
Continued 1</li>
<li>Unordered 2 level 1</li>
</ul>
<ol>
<li><strong>Ordered 1</strong> QL2 level 1
Continued 1</li>
</ol>
<blockquote>
<p>Not continued QL3</p>
</blockquote>
<ol start=""2"">
<li><p>Ordered 2 QL2 level 1
Continued 2a</p>
<p>Continued 2b</p>
<p>Continued 2c</p>
<ol start=""3"">
<li><p>Ordered 3 QL2 level 2
Continued 3a</p>
<p>Continued 3b</p>
</li>
</ol>
</li>
</ol>
</blockquote>
</blockquote>";

        TestParser.TestSpec(input, expected);
    }

    [Fact]
    public void TestIssue845ListItemBlankLine()
    {
        TestParser.TestSpec("-\n\n  foo",@"
<ul>
<li></li>
</ul>
<p>foo</p>");
        TestParser.TestSpec("-\n-\n\n  foo",@"
<ul>
<li></li>
<li></li>
</ul>
<p>foo</p>");
        TestParser.TestSpec("-\n\n-\n\n  foo",@"
<ul>
<li></li>
<li></li>
</ul>
<p>foo</p>");
    }
}
