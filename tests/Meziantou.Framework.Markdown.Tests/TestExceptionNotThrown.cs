namespace Meziantou.Framework.Markdown.Tests;

public class TestExceptionNotThrown
{
    [Fact]
    public void DoesNotThrowIndexOutOfRangeException1()
    {
        Assert.DoesNotThrow(() =>
        {
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            MarkdownConverter.ToHtml("+-\n|\n+", pipeline);
        });
    }

    [Fact]
    public void DoesNotThrowIndexOutOfRangeException2()
    {
        Assert.DoesNotThrow(() =>
        {
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            MarkdownConverter.ToHtml("+--\n|\n+0", pipeline);
        });
    }

    [Fact]
    public void DoesNotThrowIndexOutOfRangeException3()
    {
        Assert.DoesNotThrow(() =>
        {
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            MarkdownConverter.ToHtml("+-\n|\n+\n0", pipeline);
        });
    }

    [Fact]
    public void DoesNotThrowIndexOutOfRangeException4()
    {
        Assert.DoesNotThrow(() =>
        {
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            MarkdownConverter.ToHtml("+-\n|\n+0", pipeline);
        });
    }
}
