using Meziantou.Framework.Markdown.Extensions.Yaml;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Tests;

public class TestYamlFrontMatterExtension
{
    [Theory]
    [MemberData(nameof(TestCases))]
    public void ProperYamlFrontMatterRenderersAdded(string name, IMarkdownObjectRenderer[] objectRenderers, bool hasYamlFrontMatterHtmlRenderer, bool hasYamlFrontMatterRoundtripRenderer)
    {
        var builder = new MarkdownPipelineBuilder();
        builder.Extensions.Add(new YamlFrontMatterExtension());
        var markdownRenderer = new DummyRenderer();
        markdownRenderer.ObjectRenderers.AddRange(objectRenderers);
        builder.Build().Setup(markdownRenderer);
        Assert.Equal(hasYamlFrontMatterHtmlRenderer, markdownRenderer.ObjectRenderers.Contains<YamlFrontMatterHtmlRenderer>(), message: name);
        Assert.Equal(hasYamlFrontMatterRoundtripRenderer, markdownRenderer.ObjectRenderers.Contains<YamlFrontMatterRoundtripRenderer>(), message: name);
    }

    [Fact]
    public void AllowYamlFrontMatterInMiddleOfDocument()
    {
        var pipeline = new MarkdownPipelineBuilder()
            .Use(new YamlFrontMatterExtension { AllowInMiddleOfDocument = true })
            .Build();

        TestParser.TestSpec(
            "This is a text1\n---\nthis: is a frontmatter\n---\nThis is a text2",
            "<p>This is a text1</p>\n<p>This is a text2</p>",
            pipeline);
    }

    [Theory]
    [InlineData("-", "---")]
    [InlineData("--", "---")]
    [InlineData("..", "---")]
    [InlineData("-", "...")]
    [InlineData("--", "...")]
    [InlineData("..", "...")]
    [InlineData(".", "---")]
    [InlineData("-- ", "---")]
    [InlineData("----", "---")]
    [InlineData("---a", "---")]
    [InlineData("...a", "...")]
    public void ClosingFenceAfterPartialFenceLine(string partialFence, string closingFence)
    {
        foreach (var (newLine, name) in new[] { ("\n", "LF"), ("\r\n", "CRLF"), ("\r", "CR") })
        {
            var markdown = string.Join(newLine, "---", "items:", partialFence, closingFence, "Text", "");
            TestParser.TestSpec(markdown, "<p>Text</p>", "yaml", context: $"New line: {name}");
        }
    }

    [Theory]
    [InlineData("---\n---\nText\n")]
    [InlineData("--- \n---\nText\n")]
    [InlineData("---\r\n---\r\nText\r\n")]
    [InlineData("---\n...\nText\n")]
    public void EmptyFrontMatter(string markdown)
    {
        TestParser.TestSpec(markdown, "<p>Text</p>", "yaml");
    }

    [Theory]
    [InlineData("---\nitems:\n-\nText\n")]
    [InlineData("---\nitems:\n-\n----\nText\n")]
    [InlineData("---\nitems:\n-\n --- \nText\n")]
    [InlineData("---\nitems:\n..\n...a\nText\n")]
    [InlineData("---\nitems:\n--")]
    public void NoClosingFence(string markdown)
    {
        var document = MarkdownConverter.Parse(markdown, new MarkdownPipelineBuilder().UseYamlFrontMatter().Build());

        Assert.Empty(document.Descendants<YamlFrontMatterBlock>());
    }

    public static TheoryData<string, IMarkdownObjectRenderer[], bool, bool> TestCases() => new()
    {
        { "No ObjectRenderers", [], false, false },
        { "Html CodeBlock", [new Renderers.Html.CodeBlockRenderer()], true, false },
        { "Roundtrip CodeBlock", [new Renderers.Roundtrip.CodeBlockRenderer()], false, true },
        { "Html/Roundtrip CodeBlock", [new Renderers.Html.CodeBlockRenderer(), new Renderers.Roundtrip.CodeBlockRenderer()], true, true },
        { "Html/Roundtrip CodeBlock, Yaml Html", [new Renderers.Html.CodeBlockRenderer(), new Renderers.Roundtrip.CodeBlockRenderer(), new YamlFrontMatterHtmlRenderer()], true, true },
        { "Html/Roundtrip CodeBlock, Yaml Roundtrip", [new Renderers.Html.CodeBlockRenderer(), new Renderers.Roundtrip.CodeBlockRenderer(), new YamlFrontMatterRoundtripRenderer()], true, true },
    };

    private sealed class DummyRenderer : IMarkdownRenderer
    {
        public DummyRenderer()
        {
            ObjectRenderers = new ObjectRendererCollection();
        }

#pragma warning disable CS0067 // ObjectWriteBefore/ObjectWriteAfter is never used
        public event Action<IMarkdownRenderer, MarkdownObject>? ObjectWriteBefore;
        public event Action<IMarkdownRenderer, MarkdownObject>? ObjectWriteAfter;
#pragma warning restore CS0067

        public ObjectRendererCollection ObjectRenderers { get; }
        public object Render(MarkdownObject markdownObject)
        {
            return null!;
        }
    }

    [Theory]
    [InlineData("---\nkey1: value1\nkey2: value2\n---\n\n# Content\n")]
    [InlineData("---\nkey1: value1\nkey2: value2\nkey3: value3\nkey4: value4\nkey5: value5\nkey6: value6\nkey7: value7\nkey8: value8\n---\n\n# Content\n")]
    public void FrontMatterBlockLinesCharIterator(string value)
    {
        var builder = new MarkdownPipelineBuilder();
        builder.Extensions.Add(new YamlFrontMatterExtension());
        var markdownDocument = MarkdownConverter.Parse(value, builder.Build());

        var yamlBlocks = markdownDocument.Descendants<YamlFrontMatterBlock>();
        Assert.NotEmpty(yamlBlocks);

        foreach (var yamlBlock in yamlBlocks)
        {
            var iterator = yamlBlock.Lines.ToCharIterator();
            while(iterator.CurrentChar != '\0')
            {
                iterator.NextChar();
            }
        }

        // No exception parsing and iterating through YAML front matter block lines
    }

}
