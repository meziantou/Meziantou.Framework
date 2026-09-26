using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Tests;

internal static class TestRoundtrip
{
    internal static void TestSpec(string markdownText, string expected, string extensions, string context = null)
    {
        RoundTrip(markdownText, context);
    }

    internal static void RoundTrip(string markdown, string context = null)
    {
        var pipelineBuilder = new MarkdownPipelineBuilder();
        pipelineBuilder.EnableTrackTrivia();
        pipelineBuilder.UseYamlFrontMatter();
        MarkdownPipeline pipeline = pipelineBuilder.Build();
        MarkdownDocument markdownDocument = MarkdownConverter.Parse(markdown, pipeline);
        var sw = new StringWriter();
        var nr = new RoundtripRenderer(sw);
        pipeline.Setup(nr);

        nr.Write(markdownDocument);

        var result = sw.ToString();
        TestParser.PrintAssertExpected("", result, markdown, context);
    }
}
