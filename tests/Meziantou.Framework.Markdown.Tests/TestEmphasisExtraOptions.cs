using Meziantou.Framework.Markdown.Extensions.EmphasisExtras;

namespace Meziantou.Framework.Markdown.Tests;

public class TestEmphasisExtraOptions
{
    [Fact]
    public void OnlyStrikethrough_Single()
    {
        TestParser.TestSpec("~foo~", "<p>~foo~</p>", new MarkdownPipelineBuilder().UseEmphasisExtras(EmphasisExtraOptions.Strikethrough).Build());
    }

    [Fact]
    public void OnlyStrikethrough_Double()
    {
        TestParser.TestSpec("~~foo~~", "<p><del>foo</del></p>", new MarkdownPipelineBuilder().UseEmphasisExtras(EmphasisExtraOptions.Strikethrough).Build());
    }

    [Fact]
    public void OnlySubscript_Single()
    {
        TestParser.TestSpec("~foo~", "<p><sub>foo</sub></p>", new MarkdownPipelineBuilder().UseEmphasisExtras(EmphasisExtraOptions.Subscript).Build());
    }

    [Fact]
    public void OnlySubscript_Double()
    {
        TestParser.TestSpec("~~foo~~", "<p><sub><sub>foo</sub></sub></p>", new MarkdownPipelineBuilder().UseEmphasisExtras(EmphasisExtraOptions.Subscript).Build());
    }

    [Fact]
    public void SubscriptAndStrikethrough_Single()
    {
        TestParser.TestSpec("~foo~", "<p><sub>foo</sub></p>", new MarkdownPipelineBuilder().UseEmphasisExtras(EmphasisExtraOptions.Strikethrough | EmphasisExtraOptions.Subscript).Build());
    }

    [Fact]
    public void SubscriptAndStrikethrough_Double()
    {
        TestParser.TestSpec("~~foo~~", "<p><del>foo</del></p>", new MarkdownPipelineBuilder().UseEmphasisExtras(EmphasisExtraOptions.Strikethrough | EmphasisExtraOptions.Subscript).Build());
    }

    [Fact]
    public void DocumentationExample_EmphasisExtras()
    {
        #region MarkdownExtensions_UseEmphasisExtras
        var pipeline = new MarkdownPipelineBuilder()
            .UseEmphasisExtras()
            .Build();

        var html = MarkdownConverter.ToHtml("~~old~~ H~2~O x^2^ ++inserted++ ==marked==", pipeline);
        Assert.Equal("<p><del>old</del> H<sub>2</sub>O x<sup>2</sup> <ins>inserted</ins> <mark>marked</mark></p>\n", html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_EmphasisExtrasOptions()
    {
        #region EmphasisExtraOptions_Strikethrough
        // Only strikethrough: a single ~ stays a literal character
        var pipeline = new MarkdownPipelineBuilder()
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
            .Build();

        var html = MarkdownConverter.ToHtml("~~old~~ about ~20 items", pipeline);
        Assert.Equal("<p><del>old</del> about ~20 items</p>\n", html);
        #endregion
    }
}
