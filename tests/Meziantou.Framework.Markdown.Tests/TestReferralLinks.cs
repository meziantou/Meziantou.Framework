namespace Meziantou.Framework.Markdown.Tests;

public class TestReferralLinks
{
    [Theory]
    [InlineData(new[] { "nofollow" }, "nofollow")]
    [InlineData(new[] { "noopener" }, "noopener")]
    [InlineData(new[] { "nofollow", "noopener"}, "nofollow noopener")]
    public void TestLinksWithCustomRel(string[] rels, string expected)
    {
        var markdown = "[world](http://example.com)";

        var pipeline = new MarkdownPipelineBuilder()
            .UseReferralLinks(rels)
            .Build();
        var html = MarkdownConverter.ToHtml(markdown, pipeline);

        Assert.Contains($"rel=\"{expected}\"", html);
    }

    [Theory]
    [InlineData(new[] { "noopener" }, "noopener")]
    [InlineData(new[] { "nofollow" }, "nofollow")]
    [InlineData(new[] { "nofollow", "noopener" }, "nofollow noopener")]
    public void TestAutoLinksWithCustomRel(string[] rels, string expected)
    {
        var markdown = "http://example.com";

        var pipeline = new MarkdownPipelineBuilder()
            .UseAutoLinks()
            .UseReferralLinks(rels)
            .Build();
        var html = MarkdownConverter.ToHtml(markdown, pipeline);

        Assert.Contains($"rel=\"{expected}\"", html);
    }
}
