using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

public class TestCoreApi
{
    [Fact]
    public void TestToHtml()
    {
        for (int i = 0; i < 5; i++)
        {
            string html = MarkdownConverter.ToHtml("This is a text with some *emphasis*");
            Assert.Equal("<p>This is a text with some <em>emphasis</em></p>\n", html);

            html = MarkdownConverter.ToHtml("This is a text with a https://link.tld/");
            Assert.NotEqual("<p>This is a text with a <a href=\"https://link.tld/\">https://link.tld/</a></p>\n", html);
        }
    }

    [Fact]
    public void TestToHtmlWithPipeline()
    {
        var pipeline = new MarkdownPipelineBuilder()
            .Build();

        for (int i = 0; i < 5; i++)
        {
            string html = MarkdownConverter.ToHtml("This is a text with some *emphasis*", pipeline);
            Assert.Equal("<p>This is a text with some <em>emphasis</em></p>\n", html);

            html = MarkdownConverter.ToHtml("This is a text with a https://link.tld/", pipeline);
            Assert.NotEqual("<p>This is a text with a <a href=\"https://link.tld/\">https://link.tld/</a></p>\n", html);
        }

        pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        for (int i = 0; i < 5; i++)
        {
            string html = MarkdownConverter.ToHtml("This is a text with a https://link.tld/", pipeline);
            Assert.Equal("<p>This is a text with a <a href=\"https://link.tld/\">https://link.tld/</a></p>\n", html);
        }
    }

    [Fact]
    public void TestToHtmlWithWriter()
    {
        using var writer = new StringWriter();

        for (int i = 0; i < 5; i++)
        {
            _ = MarkdownConverter.ToHtml("This is a text with some *emphasis*", writer);
            string html = writer.ToString();
            Assert.Equal("<p>This is a text with some <em>emphasis</em></p>\n", html);
            writer.GetStringBuilder().Length = 0;
        }

        using var writer2 = new StringWriter();
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        for (int i = 0; i < 5; i++)
        {
            _ = MarkdownConverter.ToHtml("This is a text with a https://link.tld/", writer2, pipeline);
            string html = writer2.ToString();
            Assert.Equal("<p>This is a text with a <a href=\"https://link.tld/\">https://link.tld/</a></p>\n", html);
            writer2.GetStringBuilder().Length = 0;
        }
    }

    [Fact]
    public void TestDocumentToHtmlWithWriter()
    {
        using var writer = new StringWriter();

        for (int i = 0; i < 5; i++)
        {
            MarkdownDocument document = MarkdownConverter.Parse("This is a text with some *emphasis*");
            document.ToHtml(writer);
            string html = writer.ToString();
            Assert.Equal("<p>This is a text with some <em>emphasis</em></p>\n", html);
            writer.GetStringBuilder().Length = 0;
        }

        using var writer2 = new StringWriter();
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        for (int i = 0; i < 5; i++)
        {
            MarkdownDocument document = MarkdownConverter.Parse("This is a text with a https://link.tld/", pipeline);
            document.ToHtml(writer2, pipeline);
            string html = writer2.ToString();
            Assert.Equal("<p>This is a text with a <a href=\"https://link.tld/\">https://link.tld/</a></p>\n", html);
            writer2.GetStringBuilder().Length = 0;
        }
    }

    [Fact]
    public void TestConvert()
    {
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);

        for (int i = 0; i < 5; i++)
        {
            _ = MarkdownConverter.Convert("This is a text with some *emphasis*", renderer);
            string html = writer.ToString();
            Assert.Equal("<p>This is a text with some <em>emphasis</em></p>\n", html);
            writer.GetStringBuilder().Length = 0;
        }

        using var writer2 = new StringWriter();
        renderer = new HtmlRenderer(writer2);
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        for (int i = 0; i < 5; i++)
        {
            _ = MarkdownConverter.Convert("This is a text with a https://link.tld/", renderer, pipeline);
            string html = writer2.ToString();
            Assert.Equal("<p>This is a text with a <a href=\"https://link.tld/\">https://link.tld/</a></p>\n", html);
            writer2.GetStringBuilder().Length = 0;
        }
    }

    [Fact]
    public void TestParse()
    {
        var markdown = "This is a text with some *emphasis*";

        var pipeline = new MarkdownPipelineBuilder()
            .UsePreciseSourceLocation()
            .Build();

        for (int i = 0; i < 5; i++)
        {
            MarkdownDocument document = MarkdownConverter.Parse(markdown, pipeline);

            Assert.Equal(1, document.LineCount);
            Assert.Equal(markdown.Length, document.Span.Length);
            Assert.HasCount(1, document.LineStartIndexes);
            Assert.Equal(0, document.LineStartIndexes[0]);

            Assert.HasCount(1, document);
            var paragraph = document[0] as ParagraphBlock;
            Assert.NotNull(paragraph);
            Assert.Equal(markdown.Length, paragraph.Span.Length);
            var literal = paragraph.Inline!.FirstChild as LiteralInline;
            Assert.NotNull(literal);
            Assert.Equal("This is a text with some ", literal.ToString());
            var emphasis = literal.NextSibling as EmphasisInline;
            Assert.NotNull(emphasis);
            Assert.Equal("*emphasis*".Length, emphasis.Span.Length);
            var emphasisLiteral = emphasis.FirstChild as LiteralInline;
            Assert.NotNull(emphasisLiteral);
            Assert.Equal("emphasis", emphasisLiteral.ToString());
            Assert.Null(emphasisLiteral.NextSibling);
            Assert.Null(emphasis.NextSibling);
        }
    }

    [Fact]
    public void TestNormalize()
    {
        for (int i = 0; i < 5; i++)
        {
            string normalized = MarkdownConverter.Normalize("Heading\n=======");
            Assert.Equal("# Heading", normalized);
        }
    }

    [Fact]
    public void TestNormalizeWithWriter()
    {
        for (int i = 0; i < 5; i++)
        {
            using var writer = new StringWriter();

            _ = MarkdownConverter.Normalize("Heading\n=======", writer);
            string normalized = writer.ToString();
            Assert.Equal("# Heading", normalized);
        }
    }

    [Fact]
    public void TestToPlainText()
    {
        for (int i = 0; i < 5; i++)
        {
            string plainText = MarkdownConverter.ToPlainText("*Hello*, [world](http://example.com)!");
            Assert.Equal("Hello, world!\n", plainText);
        }
    }

    [Fact]
    public void TestToPlainTextWithWriter()
    {
        for (int i = 0; i < 5; i++)
        {
            using var writer = new StringWriter();

            _ = MarkdownConverter.ToPlainText("*Hello*, [world](http://example.com)!", writer);
            string plainText = writer.ToString();
            Assert.Equal("Hello, world!\n", plainText);
        }
    }
}
