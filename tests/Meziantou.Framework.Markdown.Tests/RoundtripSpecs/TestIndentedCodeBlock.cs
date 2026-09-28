using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs;

public class TestIndentedCodeBlock
{
    // A codeblock is indented with 4 spaces. After the 4th space, whitespace is interpreted as content.
    // l = line
    [Theory]
    [InlineData("    l")]
    [InlineData("     l")]
    [InlineData("\tl")]
    [InlineData("\t\tl")]
    [InlineData("\tl1\n    l1")]

    [InlineData("\n    l")]
    [InlineData("\n\n    l")]
    [InlineData("\n    l\n")]
    [InlineData("\n    l\n\n")]
    [InlineData("\n\n    l\n")]
    [InlineData("\n\n    l\n\n")]

    [InlineData("    l\n    l")]
    [InlineData("    l\n    l\n    l")]


    // two newlines are needed for indented codeblock start after paragraph
    [InlineData("p\n\n    l")]
    [InlineData("p\n\n    l\n")]
    [InlineData("p\n\n    l\n\n")]

    [InlineData("p\n\n    l\n    l")]
    [InlineData("p\n\n    l\n     l")]

    [InlineData("    l\n\np\n\n    l")]
    [InlineData("    l    l\n\np\n\n    l    l")]
    public void Test(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("    l\n")]
    [InlineData("    l\r")]
    [InlineData("    l\r\n")]

    [InlineData("    l\n    l")]
    [InlineData("    l\n    l\n")]
    [InlineData("    l\n    l\r")]
    [InlineData("    l\n    l\r\n")]

    [InlineData("    l\r    l")]
    [InlineData("    l\r    l\n")]
    [InlineData("    l\r    l\r")]
    [InlineData("    l\r    l\r\n")]

    [InlineData("    l\r\n    l")]
    [InlineData("    l\r\n    l\n")]
    [InlineData("    l\r\n    l\r")]
    [InlineData("    l\r\n    l\r\n")]
    public void TestNewline(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("    l\n\n    l\n")]
    [InlineData("    l\n\n\n    l\n")]
    public void TestNewlinesInBetweenResultInOneCodeBlock(string value)
    {
        var pipelineBuilder = new MarkdownPipelineBuilder();
        pipelineBuilder.EnableTrackTrivia();
        MarkdownPipeline pipeline = pipelineBuilder.Build();
        var markdownDocument = MarkdownConverter.Parse(value, pipeline);

        Assert.HasCount(1, markdownDocument);
    }

    [Theory]
    [InlineData("    l\n\np")]
    [InlineData("    l\n\n\np")]
    [InlineData("    l\n\n\n\np")]
    public void TestParagraph(string value)
    {
        RoundTrip(value);
    }
}
