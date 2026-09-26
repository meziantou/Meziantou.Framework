using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;
using System.IO;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs.Inlines;

public class TestNullCharacterInline
{
    [Theory]
    [InlineData("\0", "\uFFFD")]
    [InlineData("\0p", "\uFFFDp")]
    [InlineData("p\0", "p\uFFFD")]
    [InlineData("p\0p", "p\uFFFDp")]
    [InlineData("p\0\0p", "p\uFFFD\uFFFDp")] // I promise you, this was not intentional
    public void Test(string value, string expected)
    {
        RoundTrip(value, expected);
    }

    // this method is copied intentionally to ensure all other tests
    // do not unintentionally use the expected parameter
    private static void RoundTrip(string markdown, string expected)
    {
        var pipelineBuilder = new MarkdownPipelineBuilder();
        pipelineBuilder.EnableTrackTrivia();
        MarkdownPipeline pipeline = pipelineBuilder.Build();
        MarkdownDocument markdownDocument = MarkdownConverter.Parse(markdown, pipeline);
        var sw = new StringWriter();
        var rr = new RoundtripRenderer(sw);

        rr.Write(markdownDocument);

        Assert.Equal(expected, sw.ToString());
    }
}
