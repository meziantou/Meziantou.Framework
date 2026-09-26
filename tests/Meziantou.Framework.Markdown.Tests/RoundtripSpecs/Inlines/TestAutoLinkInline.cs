using System.IO;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;
using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs.Inlines;

public class TestAutoLinkInline
{
    [Theory]
    [InlineData("<http://a>")]
    [InlineData(" <http://a>")]
    [InlineData("<http://a> ")]
    [InlineData(" <http://a> ")]
    [InlineData("<example@example.com>")]
    [InlineData(" <example@example.com>")]
    [InlineData("<example@example.com> ")]
    [InlineData(" <example@example.com> ")]
    [InlineData("p http://a p")]
    public void Test(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("http://example.com/", "[http://example.com/](http://example.com/)")]
    [InlineData("www.example.com", "[www.example.com](http://www.example.com)")]
    [InlineData("mailto:user@example.com", "[user@example.com](mailto:user@example.com)")]
    public void AutoLinksKeepUrlWhenRoundTripped(string markdown, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder()
            .DisableHtml()
            .UseAutoLinks()
            .EnableTrackTrivia()
            .Build();
        MarkdownDocument markdownDocument = MarkdownConverter.Parse(markdown, pipeline);
        var sw = new StringWriter();
        var rr = new RoundtripRenderer(sw);

        rr.Write(markdownDocument);

        Assert.Equal(expected, sw.ToString());
    }
}
