using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Renderers;

namespace Meziantou.Framework.Markdown.Tests;

public class TestRelativeUrlReplacement
{
    [Fact]
    public void ReplacesRelativeLinks()
    {
        TestSpec("https://example.com", "Link: [hello](/relative.jpg)", "https://example.com/relative.jpg");
        TestSpec("https://example.com", "Link: [hello](relative.jpg)", "https://example.com/relative.jpg");
        TestSpec("https://example.com/", "Link: [hello](/relative.jpg?a=b)", "https://example.com/relative.jpg?a=b");
        TestSpec("https://example.com/", "Link: [hello](relative.jpg#x)", "https://example.com/relative.jpg#x");
        TestSpec(null, "Link: [hello](relative.jpg)", "relative.jpg");
        TestSpec(null, "Link: [hello](/relative.jpg)", "/relative.jpg");
        TestSpec("https://example.com", "Link: [hello](/relative.jpg)", "https://example.com/relative.jpg");
    }

    [Fact]
    public void ReplacesRelativeImageSources()
    {
        TestSpec("https://example.com", "Image: ![alt text](/image.jpg)", "https://example.com/image.jpg");
        TestSpec("https://example.com", "Image: ![alt text](image.jpg \"title\")", "https://example.com/image.jpg");
        TestSpec(null, "Image: ![alt text](/image.jpg)", "/image.jpg");
    }

    private static void TestSpec(string? baseUrl, string markdown, string expectedLink)
    {
        var pipeline = new MarkdownPipelineBuilder().Build();

        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        if (baseUrl is not null)
            renderer.BaseUrl = new Uri(baseUrl);
        pipeline.Setup(renderer);

        var document = MarkdownParser.Parse(markdown, pipeline);
        renderer.Render(document);
        writer.Flush();

        Assert.Contains("=\"" + expectedLink + "\"", writer.ToString());
    }
}