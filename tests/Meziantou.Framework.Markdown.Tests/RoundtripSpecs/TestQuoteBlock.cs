using Meziantou.Framework.Markdown.Extensions.Footers;
using Meziantou.Framework.Markdown.Extensions.Alerts;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;
using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs;

public class TestQuoteBlock
{
    [Theory]
    [InlineData(">q")]
    [InlineData(" >q")]
    [InlineData("  >q")]
    [InlineData("   >q")]
    [InlineData("> q")]
    [InlineData(" > q")]
    [InlineData("  > q")]
    [InlineData("   > q")]
    [InlineData(">  q")]
    [InlineData(" >  q")]
    [InlineData("  >  q")]
    [InlineData("   >  q")]

    [InlineData(">q\n>q")]
    [InlineData(">q\n >q")]
    [InlineData(">q\n  >q")]
    [InlineData(">q\n   >q")]
    [InlineData(">q\n> q")]
    [InlineData(">q\n > q")]
    [InlineData(">q\n  > q")]
    [InlineData(">q\n   > q")]
    [InlineData(">q\n>  q")]
    [InlineData(">q\n >  q")]
    [InlineData(">q\n  >  q")]
    [InlineData(">q\n   >  q")]

    [InlineData(" >q\n>q")]
    [InlineData(" >q\n >q")]
    [InlineData(" >q\n  >q")]
    [InlineData(" >q\n   >q")]
    [InlineData(" >q\n> q")]
    [InlineData(" >q\n > q")]
    [InlineData(" >q\n  > q")]
    [InlineData(" >q\n   > q")]
    [InlineData(" >q\n>  q")]
    [InlineData(" >q\n >  q")]
    [InlineData(" >q\n  >  q")]
    [InlineData(" >q\n   >  q")]

    [InlineData("  >q\n>q")]
    [InlineData("  >q\n >q")]
    [InlineData("  >q\n  >q")]
    [InlineData("  >q\n   >q")]
    [InlineData("  >q\n> q")]
    [InlineData("  >q\n > q")]
    [InlineData("  >q\n  > q")]
    [InlineData("  >q\n   > q")]
    [InlineData("  >q\n>  q")]
    [InlineData("  >q\n >  q")]
    [InlineData("  >q\n  >  q")]
    [InlineData("  >q\n   >  q")]

    [InlineData("> q\n>q")]
    [InlineData("> q\n >q")]
    [InlineData("> q\n  >q")]
    [InlineData("> q\n   >q")]
    [InlineData("> q\n> q")]
    [InlineData("> q\n > q")]
    [InlineData("> q\n  > q")]
    [InlineData("> q\n   > q")]
    [InlineData("> q\n>  q")]
    [InlineData("> q\n >  q")]
    [InlineData("> q\n  >  q")]
    [InlineData("> q\n   >  q")]

    [InlineData(" > q\n>q")]
    [InlineData(" > q\n >q")]
    [InlineData(" > q\n  >q")]
    [InlineData(" > q\n   >q")]
    [InlineData(" > q\n> q")]
    [InlineData(" > q\n > q")]
    [InlineData(" > q\n  > q")]
    [InlineData(" > q\n   > q")]
    [InlineData(" > q\n>  q")]
    [InlineData(" > q\n >  q")]
    [InlineData(" > q\n  >  q")]
    [InlineData(" > q\n   >  q")]

    [InlineData("  > q\n>q")]
    [InlineData("  > q\n >q")]
    [InlineData("  > q\n  >q")]
    [InlineData("  > q\n   >q")]
    [InlineData("  > q\n> q")]
    [InlineData("  > q\n > q")]
    [InlineData("  > q\n  > q")]
    [InlineData("  > q\n   > q")]
    [InlineData("  > q\n>  q")]
    [InlineData("  > q\n >  q")]
    [InlineData("  > q\n  >  q")]
    [InlineData("  > q\n   >  q")]

    [InlineData("   > q\n>q")]
    [InlineData("   > q\n >q")]
    [InlineData("   > q\n  >q")]
    [InlineData("   > q\n   >q")]
    [InlineData("   > q\n> q")]
    [InlineData("   > q\n > q")]
    [InlineData("   > q\n  > q")]
    [InlineData("   > q\n   > q")]
    [InlineData("   > q\n>  q")]
    [InlineData("   > q\n >  q")]
    [InlineData("   > q\n  >  q")]
    [InlineData("   > q\n   >  q")]

    [InlineData(">  q\n>q")]
    [InlineData(">  q\n >q")]
    [InlineData(">  q\n  >q")]
    [InlineData(">  q\n   >q")]
    [InlineData(">  q\n> q")]
    [InlineData(">  q\n > q")]
    [InlineData(">  q\n  > q")]
    [InlineData(">  q\n   > q")]
    [InlineData(">  q\n>  q")]
    [InlineData(">  q\n >  q")]
    [InlineData(">  q\n  >  q")]
    [InlineData(">  q\n   >  q")]

    [InlineData(" >  q\n>q")]
    [InlineData(" >  q\n >q")]
    [InlineData(" >  q\n  >q")]
    [InlineData(" >  q\n   >q")]
    [InlineData(" >  q\n> q")]
    [InlineData(" >  q\n > q")]
    [InlineData(" >  q\n  > q")]
    [InlineData(" >  q\n   > q")]
    [InlineData(" >  q\n>  q")]
    [InlineData(" >  q\n >  q")]
    [InlineData(" >  q\n  >  q")]
    [InlineData(" >  q\n   >  q")]

    [InlineData("  >  q\n>q")]
    [InlineData("  >  q\n >q")]
    [InlineData("  >  q\n  >q")]
    [InlineData("  >  q\n   >q")]
    [InlineData("  >  q\n> q")]
    [InlineData("  >  q\n > q")]
    [InlineData("  >  q\n  > q")]
    [InlineData("  >  q\n   > q")]
    [InlineData("  >  q\n>  q")]
    [InlineData("  >  q\n >  q")]
    [InlineData("  >  q\n  >  q")]
    [InlineData("  >  q\n   >  q")]

    [InlineData("   >  q\n>q")]
    [InlineData("   >  q\n >q")]
    [InlineData("   >  q\n  >q")]
    [InlineData("   >  q\n   >q")]
    [InlineData("   >  q\n> q")]
    [InlineData("   >  q\n > q")]
    [InlineData("   >  q\n  > q")]
    [InlineData("   >  q\n   > q")]
    [InlineData("   >  q\n>  q")]
    [InlineData("   >  q\n >  q")]
    [InlineData("   >  q\n  >  q")]
    [InlineData("   >  q\n   >  q")]

    [InlineData(">q\n>q\n>q")]
    [InlineData(">q\n>\n>q")]
    [InlineData(">q\np\n>q")]
    [InlineData(">q\n>\n>\n>q")]
    [InlineData(">q\n>\n>\n>\n>q")]
    [InlineData(">q\n>\n>q\n>\n>q")]
    [InlineData("p\n\n> **q**\n>p\n")]

    [InlineData("> q\np\n> q")] // lazy
    [InlineData("> q\n> q\np")] // lazy

    [InlineData(">>q")]
    [InlineData(" >  >   q")]

    [InlineData("> **q**\n>p\n")]
    [InlineData("> **q**")]
    public void Test(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData(">     q")] // 5
    [InlineData(">      q")] // 6
    [InlineData(" >     q")] //5
    [InlineData(" >      q")] //6
    [InlineData(" > \tq")]
    [InlineData(">     q\n>     q")] // 5, 5
    [InlineData(">     q\n>      q")] // 5, 6
    [InlineData(">      q\n>     q")] // 6, 5
    [InlineData(">      q\n>      q")] // 6, 6
    [InlineData(">     q\n\n>     5")] // 5, 5
    public void TestIndentedCodeBlock(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("\n> q")]
    [InlineData("\n> q\n")]
    [InlineData("\n> q\n\n")]
    [InlineData("> q\n\np")]
    [InlineData("p\n\n> q\n\n# h")]

    //https://github.com/lunet-io/markdig/issues/480
    //[TestCase(">\np")]
    //[TestCase(">**b**\n>\n>p\n>\np\n")]
    public void TestParagraph(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("> q\n\n# h\n")]
    public void TestAtxHeader(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData(">- i")]
    [InlineData("> - i")]
    [InlineData(">- i\n>- i")]
    [InlineData(">- >p")]
    [InlineData("> - >p")]
    [InlineData(">- i1\n>- i2\n")]
    [InlineData("> **p** p\n>- i1\n>- i2\n")]
    public void TestUnorderedList(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("> *q*\n>p\n")]
    [InlineData("> *q*")]
    public void TestEmphasis(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("> **q**\n>p\n")]
    [InlineData("> **q**")]
    public void TestStrongEmphasis(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData(">p\n")]
    [InlineData(">p\r")]
    [InlineData(">p\r\n")]

    [InlineData(">p\n>p")]
    [InlineData(">p\r>p")]
    [InlineData(">p\r\n>p")]

    [InlineData(">p\n>p\n")]
    [InlineData(">p\r>p\n")]
    [InlineData(">p\r\n>p\n")]

    [InlineData(">p\n>p\r")]
    [InlineData(">p\r>p\r")]
    [InlineData(">p\r\n>p\r")]

    [InlineData(">p\n>p\r\n")]
    [InlineData(">p\r>p\r\n")]
    [InlineData(">p\r\n>p\r\n")]
    public void TestNewline(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData(">\n>q")]
    [InlineData(">\n>\n>q")]
    [InlineData(">q\n>\n>q")]
    [InlineData(">q\n>\n>\n>q")]
    [InlineData(">q\n> \n>q")]
    [InlineData(">q\n>  \n>q")]
    [InlineData(">q\n>   \n>q")]
    [InlineData(">q\n>\t\n>q")]
    [InlineData(">q\n>\v\n>q")]
    [InlineData(">q\n>\f\n>q")]
    public void TestEmptyLines(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData(">> a\nb\n>> c\n")]
    [InlineData(">> a\n        b\n>> c\n")]
    [InlineData("> a\n    b\n> c\n")]
    [InlineData("> - a\n    b\n> c\n")]
    [InlineData("> a\n> b\n> ===\n")]
    [InlineData("> a\n>   b\n> ===\n")]
    [InlineData("> a\nb\n> ===\n")]
    [InlineData("> a\n    b\n> ---\n")]
    [InlineData(">>> a\nb\n  c\n")]
    [InlineData(">>> a\nb\n>>> c\nd\n")]
    [InlineData(">>> a\r\nb\nc\r\n")]
    [InlineData("> > a\nb\n>\n")]
    [InlineData("> > > a\nb\n>\n>\n")]
    [InlineData("> - > a\nb\n  c\n")]
    public void TestLazyContinuation(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("  >\tq")]
    [InlineData("  >\t")]
    [InlineData("  >\t\n  >\tq")]
    [InlineData("  >\t>\tq")]
    [InlineData("  >\tq\n  >\tq")]
    [InlineData("  > q\n  >\tq")]
    [InlineData("  >\tq\nq")]
    [InlineData("  >\tq\n\tq")]
    [InlineData("  >\t<div>\n  >\t</div>")]
    [InlineData("  >\t```\n  >\tq\n  >\t```")]
    [InlineData("   >\t\tq")]
    public void TestTabConsumedByQuoteMarker(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData(">>\n>\n")]
    [InlineData(">>\n>>\n")]
    [InlineData(">>\n>\n>\n")]
    [InlineData("> >\n> \n")]
    [InlineData("> ***\n>\n")]
    [InlineData("> # h\n>\n")]
    [InlineData("> ```\n> x\n> ```\n>\n")]
    [InlineData(">\ny\n")]
    [InlineData(">\n\ny\n")]
    [InlineData(">\r\ny\r\n")]
    [InlineData(">\ry\r")]
    public void TestLinesWithoutContent(string value)
    {
        RoundTrip(value);
    }

    [Fact]
    public void TestLazyLinesInNestedQuotesAreCounted()
    {
        const int Depth = 100;
        const int LazyLines = 1000;
        var markdown = new string('>', Depth) + " a\n" + string.Concat(Enumerable.Repeat("b\n", LazyLines));
        var document = MarkdownConverter.Parse(markdown, new MarkdownPipelineBuilder().EnableTrackTrivia().Build());

        // The outermost quote records each lazy line, the nested quotes only count them
        var quoteLines = document.Descendants<QuoteBlock>().Sum(quote => quote.QuoteLines.Count);
        Assert.True(quoteLines <= Depth + LazyLines, $"{quoteLines} quote lines");
        RoundTrip(markdown);
    }

    [Theory]
    [InlineData("> [!NOTE]")]
    [InlineData("> [!NOTE]  ")]
    [InlineData("> [!NOTE]\n> a")]
    [InlineData(">[!TIP] \r\n>a\r\n")]
    [InlineData("> [!NOTE]\r> a\r")]
    [InlineData("  > [!WARNING]\n  > a\nlazy\n")]
    [InlineData("> [!NOTE]\n> a\n>\n> b\n\npara\n")]
    [InlineData("> [!NOTE]\n>\n>\n")]
    [InlineData("> [!NOTE]\n> - a\n>   b\n> ```\n> c\n> ```\n")]
    [InlineData("> [!NOTE]\n> > [!TIP]\n> > b\n")]
    [InlineData("x\n\n> [!NOTE]\n> a\n\n\n")]
    public void TestAlert(string value)
    {
        RoundTrip(value, new MarkdownPipelineBuilder().UseAlertBlocks());
    }

    [Theory]
    [InlineData("- > [!NOTE]\n  > a\n")]
    [InlineData("> > [!NOTE]\n> > a\n> b\n")]
    public void TestNestedAlert(string value)
    {
        RoundTrip(value, new MarkdownPipelineBuilder().UseAlertBlocks(allowNestedAlerts: true));
    }

    [Fact]
    public void TestAlertWithoutSource()
    {
        var pipeline = new MarkdownPipelineBuilder().UseAlertBlocks().EnableTrackTrivia().Build();
        var document = MarkdownConverter.Parse("a\n", pipeline);
        var paragraph = document[0];
        document.RemoveAt(0);
        var alert = new AlertBlock(new StringSlice("NOTE"));
        alert.Add(paragraph);
        document.Add(alert);

        var writer = new StringWriter();
        var renderer = new RoundtripRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Write(document);

        Assert.Equal("> [!NOTE]\n> a\n", writer.ToString());
    }

    [Theory]
    [InlineData("^^ This is a footer\n^^ multi-line\n")]
    [InlineData("^^a\n^^b")]
    [InlineData("  ^^  a  \r\n^^\tb\r\n")]
    [InlineData("^^ a\nlazy\n^^ b\n")]
    [InlineData("^^ a\n^^\n^^ b\n\nafter\n")]
    [InlineData("^^ a\n\n\n^^ b\n")]
    [InlineData("^^ a\n^^\n")]
    [InlineData("> ^^ a\n> ^^ b\n")]
    [InlineData("^^ - a\n^^   b\n")]
    [InlineData("p\n^^ a\n")]
    public void TestFooter(string value)
    {
        RoundTrip(value, new MarkdownPipelineBuilder().UseFooters());
    }

    [Fact]
    public void TestFooterWithoutSource()
    {
        var pipeline = new MarkdownPipelineBuilder().UseFooters().EnableTrackTrivia().Build();
        var document = MarkdownConverter.Parse("a\n", pipeline);
        var paragraph = document[0];
        document.RemoveAt(0);
        var footer = new FooterBlock(new FooterBlockParser()) { OpeningCharacter = '^' };
        footer.Add(paragraph);
        document.Add(footer);

        var writer = new StringWriter();
        var renderer = new RoundtripRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Write(document);

        Assert.Equal("^^ a\n", writer.ToString());
    }
}

