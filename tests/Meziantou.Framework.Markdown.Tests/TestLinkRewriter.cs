using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Renderers;

namespace Meziantou.Framework.Markdown.Tests;

public class TestLinkRewriter
{
    [Fact]
    public void ReplacesRelativeLinks()
    {
        TestSpec(s => "abc" + s, "Link: [hello](/relative.jpg)", "abc/relative.jpg");
        TestSpec(s => s + "xyz", "Link: [hello](relative.jpg)", "relative.jpgxyz");
        TestSpec(null, "Link: [hello](relative.jpg)", "relative.jpg");
        TestSpec(null, "Link: [hello](/relative.jpg)", "/relative.jpg");
    }

    [Fact]
    public void ReplacesRelativeImageSources()
    {
        TestSpec(s => "abc" + s, "Image: ![alt text](/image.jpg)", "abc/image.jpg");
        TestSpec(s => "abc" + s, "Image: ![alt text](image.jpg \"title\")", "abcimage.jpg");
        TestSpec(null, "Image: ![alt text](/image.jpg)", "/image.jpg");
    }

    private static void TestSpec(Func<string,string>? linkRewriter, string markdown, string expectedLink)
    {
        var pipeline = new MarkdownPipelineBuilder().Build();

        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        renderer.LinkRewriter = linkRewriter;
        pipeline.Setup(renderer);

        var document = MarkdownParser.Parse(markdown, pipeline);
        renderer.Render(document);
        writer.Flush();

        Assert.Contains("=\"" + expectedLink + "\"", writer.ToString());
    }

    [Fact]
    public void DocumentationExample_BaseUrlAndLinkRewriter()
    {
        #region HtmlRenderer_BaseUrlAndLinkRewriter
        var pipeline = new MarkdownPipelineBuilder().Build();
        var document = MarkdownConverter.Parse("[Next page](next.md) ![Logo](logo.png)", pipeline);

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer)
        {
            // Resolves the relative URLs of links and images
            BaseUrl = new Uri("https://example.com/docs/"),

            // Called for every URL of a link or an image, after BaseUrl is applied
            LinkRewriter = url => url.EndsWith(".md", StringComparison.Ordinal) ? url[..^3] + ".html" : url,
        };
        pipeline.Setup(renderer); // Adds the renderers of the extensions of the pipeline
        renderer.Render(document);

        Assert.Equal("<p><a href=\"https://example.com/docs/next.html\">Next page</a> <img src=\"https://example.com/docs/logo.png\" alt=\"Logo\" /></p>\n", writer.ToString());
        #endregion
    }
}
