using Meziantou.Framework.Markdown.Extensions.CustomContainers;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs;

public class TestFencedCodeBlock
{
    [Theory]
    [InlineData(":::spoiler\nx\n:::")]
    [InlineData(":::\n:::\n")]
    [InlineData("  ::: spoiler  arg  \r\nx\r\n  ::::  \r\n")]
    [InlineData("a\n\n:::a\n- b\n\n  c\n:::\n\nd\n")]
    [InlineData("> :::a\n> b\n> :::\n")]
    [InlineData("::::a\n:::b\nc\n:::\n::::\n")]
    [InlineData(":::a\nunclosed\n")]
    [InlineData("::inline:: text ::x::\n")]
    public void TestCustomContainer(string value)
    {
        RoundTrip(value, new MarkdownPipelineBuilder().UseCustomContainers());
    }

    [Fact]
    public void TestCustomContainerWithoutSource()
    {
        var pipeline = new MarkdownPipelineBuilder().UseCustomContainers().EnableTrackTrivia().Build();
        var document = MarkdownConverter.Parse("a\n", pipeline);
        var paragraph = document[0];
        document.RemoveAt(0);
        var container = new CustomContainer(new CustomContainerParser()) { Info = "note" };
        container.Add(paragraph);
        document.Add(container);

        var writer = new StringWriter();
        var renderer = new RoundtripRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Write(document);

        Assert.Equal(":::note\na\n:::\n", writer.ToString());
    }

    [Theory]
    [InlineData("```\nc\n```")]
    [InlineData("```\nc\n```\n")]
    [InlineData("\n```\nc\n```")]
    [InlineData("\n\n```\nc\n```")]
    [InlineData("```\nc\n```\n\n")]
    [InlineData("\n```\nc\n```\n")]
    [InlineData("\n```\nc\n```\n\n")]
    [InlineData("\n\n```\nc\n```\n")]
    [InlineData("\n\n```\nc\n```\n\n")]

    [InlineData(" ```\nc\n````")]
    [InlineData("```\nc\n````")]
    [InlineData("p\n\n```\nc\n```")]

    [InlineData("```\n c\n```")]
    [InlineData("```\nc \n```")]
    [InlineData("```\n c \n```")]

    [InlineData(" ``` \n c \n ``` ")]
    [InlineData("\t```\t\n\tc\t\n\t```\t")]
    [InlineData("\v```\v\n\vc\v\n\v```\v")]
    [InlineData("\f```\f\n\fc\f\n\f```\f")]
    public void Test(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("~~~ aa ``` ~~~\nfoo\n~~~")]
    [InlineData("~~~ aa ``` ~~~\nfoo\n~~~ ")]
    public void TestTilde(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("```\n c \n```")]
    [InlineData("```\n c \r```")]
    [InlineData("```\n c \r\n```")]
    [InlineData("```\r c \n```")]
    [InlineData("```\r c \r```")]
    [InlineData("```\r c \r\n```")]
    [InlineData("```\r\n c \n```")]
    [InlineData("```\r\n c \r```")]
    [InlineData("```\r\n c \r\n```")]

    [InlineData("```\n c \n```\n")]
    [InlineData("```\n c \r```\n")]
    [InlineData("```\n c \r\n```\n")]
    [InlineData("```\r c \n```\n")]
    [InlineData("```\r c \r```\n")]
    [InlineData("```\r c \r\n```\n")]
    [InlineData("```\r\n c \n```\n")]
    [InlineData("```\r\n c \r```\n")]
    [InlineData("```\r\n c \r\n```\n")]

    [InlineData("```\n c \n```\r")]
    [InlineData("```\n c \r```\r")]
    [InlineData("```\n c \r\n```\r")]
    [InlineData("```\r c \n```\r")]
    [InlineData("```\r c \r```\r")]
    [InlineData("```\r c \r\n```\r")]
    [InlineData("```\r\n c \n```\r")]
    [InlineData("```\r\n c \r```\r")]
    [InlineData("```\r\n c \r\n```\r")]

    [InlineData("```\n c \n```\r\n")]
    [InlineData("```\n c \r```\r\n")]
    [InlineData("```\n c \r\n```\r\n")]
    [InlineData("```\r c \n```\r\n")]
    [InlineData("```\r c \r```\r\n")]
    [InlineData("```\r c \r\n```\r\n")]
    [InlineData("```\r\n c \n```\r\n")]
    [InlineData("```\r\n c \r```\r\n")]
    [InlineData("```\r\n c \r\n```\r\n")]
    public void TestNewline(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("```i a\n```")]
    [InlineData("```i a a2\n```")]
    [InlineData("```i a a2 a3\n```")]
    [InlineData("```i a a2 a3 a4\n```")]

    [InlineData("```i\ta\n```")]
    [InlineData("```i\ta a2\n```")]
    [InlineData("```i\ta a2 a3\n```")]
    [InlineData("```i\ta a2 a3 a4\n```")]

    [InlineData("```i\ta \n```")]
    [InlineData("```i\ta a2 \n```")]
    [InlineData("```i\ta a2 a3 \n```")]
    [InlineData("```i\ta a2 a3 a4 \n```")]
    public void TestInfoArguments(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("```\nc\n``` ")]
    [InlineData("```\nc\n```  \n")]
    [InlineData("~~~\nc\n~~~\t\n")]
    [InlineData("```\nc\n````  \r\n")]
    [InlineData("```\nc\n  ```  \n\na\n")]
    [InlineData("> ```\n> c\n> ```  \n> a\n")]
    [InlineData("- ```\n  c\n  ``` \n- a\n")]
    public void TestSpacesAfterClosingFence(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("$$\nx\n$$")]
    [InlineData("$$\nx\n$$\n")]
    [InlineData("$$  \nx\n$$  \n")]
    [InlineData("  $$\n  x\n  $$\n")]
    [InlineData("$$\r\nx\r\ny\r\n$$\r\n")]
    [InlineData("a\n\n$$\nx\n$$\n\nb\n")]
    [InlineData("> $$\n> x\n> $$\n")]
    [InlineData("- $$\n  x\n  $$\n")]
    [InlineData("$$\nx\n")]
    public void TestMathBlock(string value)
    {
        RoundTrip(value, new MarkdownPipelineBuilder().UseMathematics());
    }
}
