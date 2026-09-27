using System.Text.RegularExpressions;

namespace Meziantou.Framework.Markdown.Tests;

public class TestImageAltText
{
    [Theory]
    [InlineData("![](image.jpg)", "")]
    [InlineData("![foo](image.jpg)", "foo")]
    [InlineData("![][1]\n\n[1]: image.jpg", "")]
    [InlineData("![bar][1]\n\n[1]: image.jpg", "bar")]
    [InlineData("![](image.jpg 'title')", "")]
    [InlineData("![foo](image.jpg 'title')", "foo")]
    [InlineData("![][1]\n\n[1]: image.jpg 'title'", "")]
    [InlineData("![bar][1]\n\n[1]: image.jpg 'title'", "bar")]
    public void TestImageHtmlAltText(string markdown, string expectedAltText)
    {
        string html = MarkdownConverter.ToHtml(markdown);
        string actualAltText = Regex.Match(html, "alt=\"(.*?)\"").Groups[1].Value;
        Assert.Equal(expectedAltText, actualAltText);
    }

    [Theory]
    [InlineData("![<a>](i.png)", "<p><img src=\"i.png\" alt=\"&lt;a&gt;\" /></p>")]
    [InlineData("![a <b>c</b> d](i.png)", "<p><img src=\"i.png\" alt=\"a &lt;b&gt;c&lt;/b&gt; d\" /></p>")]
    [InlineData("![<span title=\"x\">y</span>](i.png)", "<p><img src=\"i.png\" alt=\"&lt;span title=&quot;x&quot;&gt;y&lt;/span&gt;\" /></p>")]
    [InlineData("![a <!-- c --> b](i.png)", "<p><img src=\"i.png\" alt=\"a &lt;!-- c --&gt; b\" /></p>")]
    [InlineData("![![<b>](x)](y)", "<p><img src=\"y\" alt=\"&lt;b&gt;\" /></p>")]
    [InlineData("![<b>][r]\n\n[r]: i.png", "<p><img src=\"i.png\" alt=\"&lt;b&gt;\" /></p>")]
    [InlineData("[<b>x</b>](u)", "<p><a href=\"u\"><b>x</b></a></p>")]
    public void TestRawHtmlInAltTextIsWrittenAsText(string markdown, string expected)
    {
        TestParser.TestSpec(markdown, expected);
    }

    [Theory]
    [InlineData("![::a::{#onerror=alert(1)//}](b.png)", "<p><img src=\"b.png\" alt=\"a\" /></p>\n")]
    [InlineData("![x[^1]](b.png)\n\n[^1]: note", "<p><img src=\"b.png\" alt=\"x1\" /></p>\n")]
    [InlineData("![![v](a.mp4)](b.png)", "<p><img src=\"b.png\" alt=\"v\" /></p>\n")]
    public void TestExtensionInlinesInAltTextWriteNoMarkup(string markdown, string expectedPrefix)
    {
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

        string html = MarkdownConverter.ToHtml(markdown, pipeline);
        Assert.StartsWith(expectedPrefix, html);
    }
}
