using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;
using System.IO;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs.Inlines;

public class TestNullCharacterInline
{
    [Theory]
    [InlineData("\0")]
    [InlineData("\0p")]
    [InlineData("p\0")]
    [InlineData("p\0p")]
    [InlineData("p\0\0p")]
    [InlineData("p�\0p\0�")]
    [InlineData("\u0001\0# foo *bar* \\*baz\\*")]
    [InlineData("# a\0 *b\0*")]
    [InlineData("<![CDATA[\na\0b\n]]>\nokay")]
    [InlineData("a\nb\0c `d\ne\0` [f\0](g\0 \"h\0\")\n> i\0\n> j")]
    [InlineData("```a\0\nb\0\n```\n\n    c\0\n")]
    public void Test(string value)
    {
        RoundTrip(value, value);
    }

    [Fact]
    public void NullCharacterIsWrittenBackWithTheDocumentRendererOfAnExtension()
    {
        const string Markdown = "a\0[^1]\n\n[^1]: b\0\n\nc\0\n";
        var pipeline = new MarkdownPipelineBuilder().UseFootnotes().EnableTrackTrivia().Build();
        var document = MarkdownConverter.Parse(Markdown, pipeline);
        var writer = new StringWriter();
        var renderer = new RoundtripRenderer(writer);
        pipeline.Setup(renderer);

        renderer.Write(document);

        Assert.Equal(Markdown, writer.ToString());
    }

    [Fact]
    public void NullCharacterIsReplacedInHtml()
    {
        var pipeline = new MarkdownPipelineBuilder().EnableTrackTrivia().Build();

        Assert.Equal("<p>a�b</p>\n", MarkdownConverter.ToHtml("a\0b", pipeline));
    }

    [Fact]
    public void NullCharacterIsReplacedAfterAChange()
    {
        // Once the text written differs from the parsed text, the replacement characters are written as they are in the document
        var pipeline = new MarkdownPipelineBuilder().EnableTrackTrivia().Build();
        var document = MarkdownConverter.Parse("a\0b\n\nc\0d\n", pipeline);
        document.RemoveAt(0);
        var writer = new StringWriter();

        new RoundtripRenderer(writer).Write(document);

        Assert.Equal("c�d\n", writer.ToString());
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
