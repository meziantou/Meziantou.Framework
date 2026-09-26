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
}
