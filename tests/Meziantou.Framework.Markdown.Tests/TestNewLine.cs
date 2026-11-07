using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

public class TestNewLine
{
    [Theory]
    [InlineData("a  \nb", "<p>a<br />\nb</p>\n")]
    [InlineData("a\\\nb", "<p>a<br />\nb</p>\n")]
    [InlineData("a `b\nc`", "<p>a <code>b c</code></p>\n")]
    [InlineData("# Text A\nText B\n\n## Text C", "<h1>Text A</h1>\n<p>Text B</p>\n<h2>Text C</h2>\n")]
    public void Test(string value, string expectedHtml)
    {
        Assert.Equal(expectedHtml, MarkdownConverter.ToHtml(value));
        Assert.Equal(expectedHtml, MarkdownConverter.ToHtml(value.Replace("\n", "\r\n", StringComparison.Ordinal)));
    }

    [Fact]
    public void TestEscapeLineBreak()
    {
        var input = "test\\\r\ntest1\r\n";
        var doc = MarkdownConverter.Parse(input);
        var inlines = doc.Descendants<LineBreakInline>().ToList();
        Assert.HasCount(1, inlines, message: "Invalid number of LineBreakInline");
        Assert.True(inlines[0].IsBackslash);
    }

    [Theory]
    [InlineData("> [!NOTE]\n> a\n")]
    [InlineData("> [!NOTE]\r\n> a\r\n")]
    [InlineData("> [!NOTE]\r> a\r")]
    [InlineData("> [!NOTE]  \r\n> a")]
    public void AlertKindIsFollowedByAnyNewLine(string markdown)
    {
        var pipeline = new MarkdownPipelineBuilder().UseAlertBlocks((renderer, kind) => renderer.Write("[").Write(kind).Write("]")).Build();

        Assert.Equal("<div class=\"markdown-alert markdown-alert-note\">\n[NOTE]<p>a</p>\n</div>\n", MarkdownConverter.ToHtml(markdown, pipeline));
    }
}
