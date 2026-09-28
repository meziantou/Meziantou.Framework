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
}