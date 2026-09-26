using System.Diagnostics;
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
    [InlineData("[x](https://evil.example\uFF0F@good.example/path)", "<p><a href=\"https://evil.example%EF%BC%8F@good.example/path\">x</a></p>")]
    [InlineData("[x](https://evil.example\uFF03@good.example/path)", "<p><a href=\"https://evil.example%EF%BC%83@good.example/path\">x</a></p>")]
    [InlineData("[x](https://evil.example\uFF1F@good.example/path)", "<p><a href=\"https://evil.example%EF%BC%9F@good.example/path\">x</a></p>")]
    [InlineData("<https://evil.example\uFF0F@good.example/path>", "<p><a href=\"https://evil.example%EF%BC%8F@good.example/path\">https://evil.example\uFF0F@good.example/path</a></p>")]
    [InlineData("[x](http://\u00FCnicode.com:8080/a)", "<p><a href=\"http://xn--nicode-2ya.com:8080/a\">x</a></p>")]
    [InlineData("[x](http://\u00FC:p@\u00FCnicode.com/a)", "<p><a href=\"http://%C3%BC:p@xn--nicode-2ya.com/a\">x</a></p>")]
    [InlineData("[x](http://\u00FCnicode.com\\a)", "<p><a href=\"http://xn--nicode-2ya.com%5Ca\">x</a></p>")]
    public void IdnMappingOnlyAppliesToTheHost(string markdown, string expected)
    {
        TestParser.TestSpec(markdown, expected);
    }

    [Fact]
    public void IdnMappingOfTheHostCannotIntroduceADelimiter()
    {
        // With ICU, U+FF0F is mapped to '/', so the host is percent-encoded instead; without ICU, it is converted to
        // punycode. Either way, the browser must not see a '/' in the middle of the host.
        var html = MarkdownConverter.ToHtml("[x](https://evil.example\uFF0F.good.example/path)");

        Assert.DoesNotContain("evil.example/", html);
        Assert.Contains(".good.example/path\">x</a>", html);
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

    [Theory]
    [InlineData("[a](", "")]
    [InlineData("![a](", "")]
    [InlineData("[a](()", "")]
    [InlineData("[a]( ", "")]
    [InlineData("[a]([a](<(>", "")]
    [InlineData("[a](b (", "")]
    [InlineData("[a](b (", ")x")]
    public void UnclosedInlineLinksDoNotTakeQuadraticTime(string pattern, string suffix)
    {
        // Each link opener used to scan the rest of the paragraph again, which took minutes for this input
        var markdown = string.Concat(Enumerable.Repeat(pattern, 50_000)) + suffix;
        MarkdownPipeline[] pipelines =
        [
            new MarkdownPipelineBuilder().Build(),
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build(),
            new MarkdownPipelineBuilder().EnableTrackTrivia().Build(),
        ];

        foreach (var pipeline in pipelines)
        {
            var stopwatch = Stopwatch.StartNew();
            _ = MarkdownConverter.ToHtml(markdown, pipeline);
            stopwatch.Stop();

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), message: $"Rendering took {stopwatch.Elapsed}");
        }
    }

    [Theory]
    [InlineData("[a]([a]([a](", "<p>[a]([a]([a](</p>")]
    [InlineData("![a]([a](", "<p>![a]([a](</p>")]
    [InlineData("[a](()[a](()", "<p>[a](()[a](()</p>")]
    [InlineData("[a]([a]([a](b )", "<p>[a]([a](<a href=\"b\">a</a></p>")]
    [InlineData("[a]([a]([a](x)", "<p>[a]([a](<a href=\"x\">a</a></p>")]
    [InlineData("[a]([a](<(> )", "<p>[a](<a href=\"(\">a</a></p>")]
    [InlineData("[a]( [a]( [a](b )", "<p>[a]( [a]( <a href=\"b\">a</a></p>")]
    [InlineData("[a](b ([a](b ([a](b (", "<p>[a](b ([a](b ([a](b (</p>")]
    [InlineData("[a](b ( [a](b (c) x", "<p>[a](b ( [a](b (c) x</p>")]
    [InlineData("[a](b (x) y [a](b (c))", "<p>[a](b (x) y <a href=\"b\" title=\"c\">a</a></p>")]
    public void UnclosedInlineLinks(string markdown, string expected)
    {
        TestParser.TestSpec(markdown, expected);
        TestParser.TestSpec(markdown, expected, new MarkdownPipelineBuilder().EnableTrackTrivia().Build());
        TestRoundtrip.RoundTrip(markdown);
    }

    [Theory]
    [InlineData("[", 3000)]
    [InlineData("[", 9000)]
    [InlineData("![", 5000)]
    public void GlobalizationDoesNotOverflowTheStackOnDeepInlines(string pattern, int count)
    {
        var markdown = string.Concat(Enumerable.Repeat(pattern, count)) + "a";
        var pipeline = new MarkdownPipelineBuilder().UseGlobalization().Build();

        // 1 MB is the default stack size of thread-pool threads on Windows; a stack overflow kills the test process
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                MarkdownConverter.ToHtml(markdown, pipeline);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        }, maxStackSize: 1024 * 1024);
        thread.Start();
        thread.Join();

        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Contains("depth limit", argumentException.Message);
    }

    [Theory]
    [InlineData("[a]", "<a ")]
    [InlineData("![a]", "<img ")]
    [InlineData("[t][a]", "<a ")]
    public void ReferenceExpansionIsBounded(string reference, string expectedTag)
    {
        // Each use copies the 20,001 characters of the URL. Like cmark, the total is limited to max(100,000, input length).
        var markdown = "[a]: /" + new string('x', 20_000) + "\n\n" + string.Concat(Enumerable.Repeat(reference, 10_000));

        var html = MarkdownConverter.ToHtml(markdown);

        Assert.Equal(4, CountOccurrences(html, expectedTag));
        // Without the limit, the output is about 200 MB
        Assert.HasCountLessThan(markdown.Length + 200_000, html);
    }

    [Fact]
    public void ShortReferencesAreAllExpanded()
    {
        var markdown = "[a]: /url \"title\"\n\n" + string.Concat(Enumerable.Repeat("see [a] ", 10_000));

        var html = MarkdownConverter.ToHtml(markdown);

        Assert.Equal(10_000, CountOccurrences(html, "<a href=\"/url\" title=\"title\">a</a>"));
    }

    [Fact]
    public void AbbreviationExpansionIsBounded()
    {
        var markdown = "*[A]: " + new string('x', 20_000) + "\n\n" + string.Concat(Enumerable.Repeat("A ", 10_000));
        var pipeline = new MarkdownPipelineBuilder().UseAbbreviations().Build();

        var html = MarkdownConverter.ToHtml(markdown, pipeline);

        Assert.Equal(5, CountOccurrences(html, "<abbr "));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    [Theory]
    [InlineData("*x* ", "<em>x</em>", "")]
    [InlineData("_x_ ", "<em>x</em>", "")]
    [InlineData("**x** ", "<strong>x</strong>", "")]
    [InlineData("a* ", "a*", "")]
    [InlineData("~~x~~ ", "<del>x</del>", "emphasisextras")]
    [InlineData("*x* ", "<em>x</em>", "advanced")]
    public void ManyEmphasisDelimitersInAParagraphDoNotExceedTheDepthLimit(string item, string expectedItem, string extensions)
    {
        const int Count = 8000;
        var pipeline = new MarkdownPipelineBuilder().Configure(extensions).Build();

        var html = MarkdownConverter.ToHtml(string.Concat(Enumerable.Repeat(item, Count)), pipeline);

        Assert.Equal("<p>" + string.Join(' ', Enumerable.Repeat(expectedItem, Count)) + "</p>\n", html);
    }

    [Fact]
    public void ManyEmphasisDelimitersInAParagraphAreParsedInLinearTime()
    {
        // Each inline used to walk the chain of all the unresolved delimiters before it
        const int Count = 100_000;
        var markdown = string.Concat(Enumerable.Repeat("*x* ", Count));

        var stopwatch = Stopwatch.StartNew();
        var html = MarkdownConverter.ToHtml(markdown);
        stopwatch.Stop();

        Assert.Equal("<p>" + string.Join(' ', Enumerable.Repeat("<em>x</em>", Count)) + "</p>\n", html);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Parsing took {stopwatch.Elapsed}");
    }

    [Fact]
    public void GuardsAgainstHighlyNestedEmphasis()
    {
        const int Count = 11 * 1024;
        var markdown = string.Concat(Enumerable.Repeat("**a ", Count)) + string.Concat(Enumerable.Repeat("b** ", Count));

        Exception e = Assert.Throws<ArgumentException>(() => MarkdownConverter.Parse(markdown));
        Assert.Contains("depth limit", e.Message);
    }

    [Theory]
    [InlineData("*x* ] ", 32_000, "", 0, "", "<em>x</em> ] ", "")]
    [InlineData("*x* ", 5_000, "] ", 100_000, "", "<em>x</em> ", "] ")]
    [InlineData("*a ", 50_000, "b_ ", 50_000, "", "*a ", "b_ ")]
    [InlineData("*x* [l](u) ", 16_000, "", 0, "", "<em>x</em> <a href=\"u\">l</a> ", "")]
    [InlineData("*x* [a] ", 16_000, "", 0, "", "<em>x</em> [a] ", "")]
    [InlineData("*x* www.a.com ", 16_000, "", 0, "autolinks", "<em>x</em> <a href=\"http://www.a.com\">www.a.com</a> ", "")]
    [InlineData("www.a.com ", 16_000, "", 0, "autolinks", "<a href=\"http://www.a.com\">www.a.com</a> ", "")]
    [InlineData("*x* ab cd{.cls} ", 16_000, "", 0, "attributes", null, null)]
    [InlineData("*x* ab cd{.cls} ] www.a.com ", 16_000, "", 0, "advanced", null, null)]
    public void ManyDelimitersInAParagraphAreParsedInLinearTime(string item, int count, string suffixItem, int suffixCount, string extensions, string? expectedItem, string? expectedSuffixItem)
    {
        // Each of these inputs used to walk up all the unresolved delimiters for each item
        var markdown = string.Concat(Enumerable.Repeat(item, count)) + string.Concat(Enumerable.Repeat(suffixItem, suffixCount));
        var pipeline = new MarkdownPipelineBuilder().Configure(extensions).Build();

        var stopwatch = Stopwatch.StartNew();
        var html = MarkdownConverter.ToHtml(markdown, pipeline);
        stopwatch.Stop();

        if (expectedItem is not null && expectedSuffixItem is not null)
        {
            var expected = string.Concat(Enumerable.Repeat(expectedItem, count)) + string.Concat(Enumerable.Repeat(expectedSuffixItem, suffixCount));
            Assert.Equal("<p>" + expected.TrimEnd() + "</p>\n", html);
        }

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Rendering took {stopwatch.Elapsed}");
    }

    [Theory]
    [InlineData("[a[")]
    [InlineData("[a][")]
    [InlineData("[a *x* ")]
    [InlineData("[*x* ")]
    public void DeeplyNestedLinkDelimitersAreRejectedQuickly(string item)
    {
        var markdown = string.Concat(Enumerable.Repeat(item, 100_000));

        var stopwatch = Stopwatch.StartNew();
        Exception e = Assert.Throws<ArgumentException>(() => MarkdownConverter.Parse(markdown));
        stopwatch.Stop();

        Assert.Contains("depth limit", e.Message);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Parsing took {stopwatch.Elapsed}");
    }

    [Fact]
    public void NestedLinkDelimitersBelowManyEmphasisDelimitersAreRejectedQuickly()
    {
        // The emphasis delimiters make the chain of open containers as deep as it was rejected before. Nested link delimiters
        // below them are still rejected: removing them one at a time costs quadratic time.
        var markdown = string.Concat(Enumerable.Repeat("*x* ", 8000)) + new string('[', 10_000) + new string(']', 10_000);

        var stopwatch = Stopwatch.StartNew();
        Exception e = Assert.Throws<ArgumentException>(() => MarkdownConverter.Parse(markdown));
        stopwatch.Stop();

        Assert.Contains("depth limit", e.Message);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Parsing took {stopwatch.Elapsed}");
    }

    [Theory]
    [InlineData("autolinks", "<a href=\"x\">www.a.com</a> www.b.com", "<p><a href=\"x\">www.a.com</a> <a href=\"http://www.b.com\">www.b.com</a></p>\n")]
    [InlineData("autolinks", "*<a href=\"x\">* www.a.com", "<p><em><a href=\"x\"></em> www.a.com</p>\n")]
    [InlineData("autolinks", "<abbr>www.a.com</abbr> www.b.com", "<p><abbr>www.a.com</abbr> <a href=\"http://www.b.com\">www.b.com</a></p>\n")]
    [InlineData("autolinks", "**www.a.com** _www.b.com_", "<p><strong><a href=\"http://www.a.com\">www.a.com</a></strong> <em><a href=\"http://www.b.com\">www.b.com</a></em></p>\n")]
    [InlineData("autolinks", "[www.a.com](u) [x www.b.com", "<p><a href=\"u\">www.a.com</a> [x www.b.com</p>\n")]
    [InlineData("autolinks", "*[x* www.a.com", "<p><em>[x</em> www.a.com</p>\n")]
    [InlineData("autolinks", "<a>*y* [z](u) www.a.com", "<p><a><em>y</em> <a href=\"u\">z</a> www.a.com</p>\n")]
    [InlineData("attributes", "*x* *ab cd{.cls}* e", "<p class=\"cls\"><em>x</em> <em>ab cd</em> e</p>\n")]
    [InlineData("", "[a [b](u) c](v)", "<p>[a <a href=\"u\">b</a> c](v)</p>\n")]
    [InlineData("", "![a [b](u) c](v)", "<p><img src=\"v\" alt=\"a b c\" /></p>\n")]
    [InlineData("", "[a *[b](u)* c](v) [d](w)", "<p>[a <em><a href=\"u\">b</a></em> c](v) <a href=\"w\">d</a></p>\n")]
    [InlineData("", "[x [a][r] y](v)\n\n[r]: /r", "<p>[x <a href=\"/r\">a</a> y](v)</p>\n")]
    [InlineData("", "[a [b [c](u) d] e](v)", "<p>[a [b <a href=\"u\">c</a> d] e](v)</p>\n")]
    [InlineData("", "[[*x* *y* ]] [*z*](u)", "<p>[[<em>x</em> <em>y</em> ]] <a href=\"u\"><em>z</em></a></p>\n")]
    public void ParsersSeeTheUnresolvedDelimitersAroundTheCurrentInline(string extensions, string markdown, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder().Configure(extensions).Build();

        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown, pipeline));
    }

    [Theory]
    [InlineData("# Example\n\n", "<h1 id=\"example-39999\">Example</h1>\n")]
    [InlineData("#\n\n", "<h1 id=\"section-39999\"></h1>\n")]
    public void DuplicateHeadingIdentifiersAreGeneratedInLinearTime(string heading, string expectedLastHeading)
    {
        var markdown = string.Concat(Enumerable.Repeat(heading, 40_000));
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        var stopwatch = Stopwatch.StartNew();
        var html = MarkdownConverter.ToHtml(markdown, pipeline);
        stopwatch.Stop();

        Assert.EndsWith(expectedLastHeading, html);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Rendering took {stopwatch.Elapsed}");
    }

    [Fact]
    public void DuplicateHeadingIdentifiersSkipTheIdentifiersOfOtherHeadings()
    {
        var pipeline = new MarkdownPipelineBuilder().UseAutoIdentifiers().Build();

        var html = MarkdownConverter.ToHtml("# a\n# a-1\n# a\n# a-3\n# a\n# a\n", pipeline);

        Assert.Equal("<h1 id=\"a\">a</h1>\n<h1 id=\"a-1\">a-1</h1>\n<h1 id=\"a-2\">a</h1>\n<h1 id=\"a-3\">a-3</h1>\n<h1 id=\"a-4\">a</h1>\n<h1 id=\"a-5\">a</h1>\n", html);
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
