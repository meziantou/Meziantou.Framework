using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers.Inlines;
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

    [Fact]
    public void FindBlockAtPositionReturnsTheDeepestBlock()
    {
        // Paragraph 0-0, paragraph 3-4, list 7-13 with items 7-9 and 11-13 containing paragraphs 9-9 and 13-13. A block
        // contains the position just after its last character.
        var document = MarkdownConverter.Parse("a\n\nbb\n\n- c\n- d");
        string?[] expected =
        [
            null,
            "ParagraphBlock 0-0", "ParagraphBlock 0-0", "MarkdownDocument 0-13",
            "ParagraphBlock 3-4", "ParagraphBlock 3-4", "ParagraphBlock 3-4", "MarkdownDocument 0-13",
            "ListItemBlock 7-9", "ListItemBlock 7-9", "ParagraphBlock 9-9", "ParagraphBlock 9-9",
            "ListItemBlock 11-13", "ListItemBlock 11-13", "ParagraphBlock 13-13", "ParagraphBlock 13-13",
            null,
        ];

        for (var position = -1; position < expected.Length - 1; position++)
        {
            var block = document.FindBlockAtPosition(position);
            Assert.Equal(expected[position + 1], block is null ? null : block.GetType().Name + " " + block.Span.ToString(), message: $"Position {position}");
        }
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 0)]
    [InlineData(6, -1)]
    public void CompareToPositionIncludesThePositionAfterTheBlock(int position, int expected)
    {
        var paragraph = MarkdownConverter.Parse("a\n\nbb\n")[1];

        Assert.Equal(new SourceSpan(3, 4), paragraph.Span);
        Assert.Equal(expected, paragraph.CompareToPosition(position));
        Assert.Equal(expected == 0, paragraph.ContainsPosition(position));
    }

    [Fact]
    public void SourceSpanEquality()
    {
        var span = new SourceSpan(1, 3);

        Assert.Equal("1-3", span.ToString());
        Assert.Equal(3, span.Length);
        Assert.False(span.IsEmpty);
        Assert.True(SourceSpan.Empty.IsEmpty);
        Assert.Equal(0, SourceSpan.Empty.Length);
        Assert.True(span.Equals((object)new SourceSpan(1, 3)));
        Assert.False(span.Equals((object)new SourceSpan(1, 4)));
        Assert.False(span.Equals("1-3"));
        Assert.True(span == new SourceSpan(1, 3));
        Assert.True(span != new SourceSpan(0, 3));
        Assert.Equal(new SourceSpan(1, 3).GetHashCode(), span.GetHashCode());
        Assert.NotEqual(new SourceSpan(3, 1).GetHashCode(), span.GetHashCode());
        Assert.Equal(new SourceSpan(3, 5), span.MoveForward(2));
        Assert.Equal(new SourceSpan(1, 3), span);
    }

    [Fact]
    public void ToPositionText()
    {
        var paragraph = MarkdownConverter.Parse("a\n\n  bb\n")[1];

        Assert.Equal("2, 2, 5-6", paragraph.ToPositionText());
    }

    [Fact]
    public void DescendantsOfAContainerBlock()
    {
        var document = MarkdownConverter.Parse("a *b*\n\n- c");
        var paragraph = (ParagraphBlock)document[0];
        var list = (ListBlock)document[1];
        var listItem = (ListItemBlock)list[0];
        var itemParagraph = (ParagraphBlock)listItem[0];
        var a = paragraph.Inline!.FirstChild!;
        var emphasis = (EmphasisInline)a.NextSibling!;
        var b = emphasis.FirstChild!;
        var c = itemParagraph.Inline!.FirstChild!;

        MarkdownObject[] blocks = [paragraph, list, listItem, itemParagraph];
        MarkdownObject[] all = [paragraph, a, emphasis, b, list, listItem, itemParagraph, c];
        Assert.Equal(all, document.Descendants().ToArray());
        Assert.Equal(all, document.Descendants<MarkdownObject>().ToArray());
        Assert.Equal(blocks, document.Descendants<Block>().ToArray());
        Assert.Equal(blocks, ((MarkdownObject)document).Descendants<Block>().ToArray());
        Assert.Equal(new MarkdownObject[] { paragraph, itemParagraph }, document.Descendants<ParagraphBlock>().ToArray());
        Assert.Equal(new MarkdownObject[] { list, listItem }, ((MarkdownObject)document).Descendants<ContainerBlock>().ToArray());
        Assert.Equal(new MarkdownObject[] { a, emphasis, b, c }, ((MarkdownObject)document).Descendants<Inline>().ToArray());
        Assert.Equal(new MarkdownObject[] { a, b, c }, ((MarkdownObject)document).Descendants<LiteralInline>().ToArray());
        Assert.Empty(new MarkdownDocument().Descendants<Block>());
        Assert.Empty(((MarkdownObject)new MarkdownDocument()).Descendants<Inline>());
    }

    [Fact]
    public void DescendantsOfALeafBlock()
    {
        var paragraph = (ParagraphBlock)MarkdownConverter.Parse("a *b*")[0];
        var a = paragraph.Inline!.FirstChild!;
        var emphasis = (EmphasisInline)a.NextSibling!;
        var b = emphasis.FirstChild!;

        Assert.Equal(new MarkdownObject[] { a, emphasis, b }, paragraph.Descendants().ToArray());
        Assert.Equal(new MarkdownObject[] { a, emphasis, b }, paragraph.Descendants<MarkdownObject>().ToArray());
        Assert.Equal(new MarkdownObject[] { a, emphasis, b }, paragraph.Descendants<Inline>().ToArray());
        Assert.Equal(new MarkdownObject[] { a, b }, paragraph.Descendants<LiteralInline>().ToArray());
        Assert.Empty(paragraph.Descendants<Block>());
        Assert.Empty(paragraph.Descendants<ParagraphBlock>());
        Assert.Equal(new Inline[] { a, emphasis, b }, paragraph.Inline.Descendants<Inline>().ToArray());
        Assert.Equal(new Inline[] { b }, emphasis.Descendants<LiteralInline>().ToArray());
        Assert.Empty(((MarkdownObject)new ParagraphBlock()).Descendants<Inline>());
    }

    [Fact]
    public void EmphasisDelimiterInlineKeepsItsContent()
    {
        var delimiter = new EmphasisDelimiterInline(new EmphasisInlineParser(), new EmphasisDescriptor('*', 1, 2, enableWithinWord: true), new StringSlice("**"))
        {
            DelimiterCount = 2,
        };

        Assert.Equal('*', delimiter.DelimiterChar);
        Assert.Equal("**", delimiter.AsLiteralInline().Content.ToString());
    }
}
