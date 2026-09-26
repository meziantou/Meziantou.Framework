namespace Meziantou.Framework.Markdown.Tests;

public class TestConfigureNewLine
{
    [Theory]
    [InlineData(/* newLineForWriting: */ "\n",   /* markdownText: */ "*1*\n*2*\n",     /* expected: */ "<p><em>1</em>\n<em>2</em></p>\n")]
    [InlineData(/* newLineForWriting: */ "\n",   /* markdownText: */ "*1*\r\n*2*\r\n", /* expected: */ "<p><em>1</em>\n<em>2</em></p>\n")]
    [InlineData(/* newLineForWriting: */ "\r\n", /* markdownText: */ "*1*\n*2*\n",     /* expected: */ "<p><em>1</em>\r\n<em>2</em></p>\r\n")]
    [InlineData(/* newLineForWriting: */ "\r\n", /* markdownText: */ "*1*\r\n*2*\r\n", /* expected: */ "<p><em>1</em>\r\n<em>2</em></p>\r\n")]
    [InlineData(/* newLineForWriting: */ "!!!" , /* markdownText: */ "*1*\n*2*\n",     /* expected: */ "<p><em>1</em>!!!<em>2</em></p>!!!")]
    [InlineData(/* newLineForWriting: */ "!!!" , /* markdownText: */ "*1*\r\n*2*\r\n", /* expected: */ "<p><em>1</em>!!!<em>2</em></p>!!!")]
    public void TestHtmlOutputWhenConfiguringNewLine(string newLineForWriting, string markdownText, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder()
            .ConfigureNewLine(newLineForWriting)
            .Build();

        var actual = MarkdownConverter.ToHtml(markdownText, pipeline);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(/* newLineForWriting: */ "\n",   /* markdownText: */ "*1*\n*2*\n",     /* expected: */ "1\n2\n")]
    [InlineData(/* newLineForWriting: */ "\n",   /* markdownText: */ "*1*\r\n*2*\r\n", /* expected: */ "1\n2\n")]
    [InlineData(/* newLineForWriting: */ "\r\n", /* markdownText: */ "*1*\n*2*\n",     /* expected: */ "1\r\n2\r\n")]
    [InlineData(/* newLineForWriting: */ "\r\n", /* markdownText: */ "*1*\r\n*2*\r\n", /* expected: */ "1\r\n2\r\n")]
    [InlineData(/* newLineForWriting: */ "!!!", /* markdownText: */ "*1*\n*2*\n",     /* expected: */ "1!!!2!!!")]
    [InlineData(/* newLineForWriting: */ "!!!", /* markdownText: */ "*1*\r\n*2*\r\n", /* expected: */ "1!!!2!!!")]
    public void TestPlainOutputWhenConfiguringNewLine(string newLineForWriting, string markdownText, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder()
            .ConfigureNewLine(newLineForWriting)
            .Build();

        var actual = MarkdownConverter.ToPlainText(markdownText, pipeline);
        Assert.Equal(expected, actual);
    }
}