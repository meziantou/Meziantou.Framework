using Meziantou.Framework.Markdown.Extensions.AutoIdentifiers;
using Meziantou.Framework.Markdown.Extensions.GenericAttributes;
using Meziantou.Framework.Markdown.Extensions.JiraLinks;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

public class TestCoreApi
{
    [Fact]
    public void ReadmeExample_ToHtml()
    {
        #region MarkdownConverter_ToHtml
        var html = MarkdownConverter.ToHtml("This is a text with some *emphasis*");
        Assert.Equal("<p>This is a text with some <em>emphasis</em></p>\n", html);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Pipeline()
    {
        #region MarkdownConverter_Pipeline
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        var html = MarkdownConverter.ToHtml("| a | b |\n|---|---|\n| 1 | 2 |", pipeline);
        Assert.StartsWith("<table>", html);
        #endregion
    }

    [Fact]
    public void ReadmeExample_PipelineExtensions()
    {
        #region MarkdownConverter_PipelineExtensions
        var pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseTaskLists()
            .UseAutoIdentifiers()
            .Build();

        var html = MarkdownConverter.ToHtml("# Getting started", pipeline);
        Assert.Equal("<h1 id=\"getting-started\">Getting started</h1>\n", html);
        #endregion
    }

    [Fact]
    public void ReadmeExample_GenericAttributesFilter()
    {
        #region MarkdownConverter_GenericAttributesFilter
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseGenericAttributes(name => GenericAttributesExtension.IsSafeAttributeName(name) || name.StartsWith("data-", StringComparison.OrdinalIgnoreCase))
            .Build();

        var html = MarkdownConverter.ToHtml("Text {data-id=1 style=color:red}", pipeline);
        Assert.Equal("<p data-id=\"1\">Text</p>\n", html);
        #endregion
    }

    [Fact]
    public void ReadmeExample_SyntaxTree()
    {
        #region MarkdownConverter_SyntaxTree
        var document = MarkdownConverter.Parse("# Title\n\nSee [the docs](https://example.com).");

        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            Assert.Equal(1, heading.Level);
            Assert.Equal(0, heading.Line);
        }

        foreach (var link in document.Descendants<LinkInline>())
        {
            Assert.Equal("https://example.com", link.Url);
        }
        #endregion
    }

    [Fact]
    public void ReadmeExample_ChangeTree()
    {
        #region MarkdownConverter_ChangeTree
        var markdown = "See [the docs](http://example.com).";
        var pipeline = new MarkdownPipelineBuilder().Build();
        var document = MarkdownConverter.Parse(markdown, pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            link.Url = link.Url?.Replace("http://", "https://", StringComparison.Ordinal);
        }

        var html = document.ToHtml(pipeline);
        Assert.Equal("<p>See <a href=\"https://example.com\">the docs</a>.</p>\n", html);
        #endregion
    }

    [Fact]
    public void ReadmeExample_OtherFormats()
    {
        #region MarkdownConverter_OtherFormats
        // Plain text, without any markup
        var text = MarkdownConverter.ToPlainText("Some **bold** text");
        Assert.Equal("Some bold text\n", text);

        // Normalized Markdown
        var normalized = MarkdownConverter.Normalize("Title\n=====");
        Assert.Equal("# Title", normalized);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Roundtrip()
    {
        #region MarkdownConverter_Roundtrip
        var markdown = "#   Title  \n\n*  item\n";
        var document = MarkdownConverter.Parse(markdown, trackTrivia: true);

        // Change the tree here...

        using var writer = new StringWriter();
        var renderer = new RoundtripRenderer(writer);
        renderer.Write(document);
        var output = writer.ToString();
        Assert.Equal(markdown, output); // Same as markdown when nothing changed
        #endregion
    }

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
    public void DocumentationExample_ToHtmlWriter()
    {
        #region MarkdownConverter_ToHtmlWriter
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        using var writer = new StringWriter();
        var document = MarkdownConverter.ToHtml("# Title\n\nSome text.", writer, pipeline);

        Assert.Equal("<h1 id=\"title\">Title</h1>\n<p>Some text.</p>\n", writer.ToString());
        Assert.HasCount(2, document); // The parsed document is returned too: a heading and a paragraph
        #endregion
    }

    [Fact]
    public void DocumentationExample_Configure()
    {
        #region MarkdownExtensions_Configure
        // For instance, read from the configuration of the application
        var extensions = "pipetables+tasklists+autoidentifiers";

        var pipeline = new MarkdownPipelineBuilder()
            .Configure(extensions)
            .Build();

        var html = MarkdownConverter.ToHtml("- [x] Done", pipeline);
        Assert.Equal("<ul class=\"contains-task-list\">\n<li class=\"task-list-item\"><input disabled=\"disabled\" type=\"checkbox\" checked=\"checked\" /> Done</li>\n</ul>\n", html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_SoftlineBreakAsHardlineBreak()
    {
        #region MarkdownExtensions_UseSoftlineBreakAsHardlineBreak
        var pipeline = new MarkdownPipelineBuilder()
            .UseSoftlineBreakAsHardlineBreak()
            .Build();

        var html = MarkdownConverter.ToHtml("First line\nSecond line", pipeline);
        Assert.Equal("<p>First line<br />\nSecond line</p>\n", html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_NonAsciiNoEscape()
    {
        #region MarkdownExtensions_UseNonAsciiNoEscape
        var markdown = "[Summer](https://example.com/été)";

        Assert.Equal("<p><a href=\"https://example.com/%C3%A9t%C3%A9\">Summer</a></p>\n", MarkdownConverter.ToHtml(markdown));

        var pipeline = new MarkdownPipelineBuilder().UseNonAsciiNoEscape().Build();
        Assert.Equal("<p><a href=\"https://example.com/été\">Summer</a></p>\n", MarkdownConverter.ToHtml(markdown, pipeline));
        #endregion
    }

    [Fact]
    public void DocumentationExample_AlertBlocks()
    {
        #region MarkdownExtensions_UseAlertBlocks
        var pipeline = new MarkdownPipelineBuilder()
            .UseAlertBlocks()
            .Build();

        var html = MarkdownConverter.ToHtml("> [!WARNING]\n> The file is overwritten.", pipeline);
        Assert.StartsWith("<div class=\"markdown-alert markdown-alert-warning\">\n<p class=\"markdown-alert-title\"><svg ", html);
        Assert.EndsWith("Warning</p>\n<p>The file is overwritten.</p>\n</div>\n", html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_AlertBlocksRenderKind()
    {
        #region MarkdownExtensions_UseAlertBlocksRenderKind
        var pipeline = new MarkdownPipelineBuilder()
            .UseAlertBlocks(renderKind: (renderer, kind) =>
            {
                // kind is the text between "[!" and "]", such as "WARNING"
                renderer.Write("<p class=\"alert-title\">").WriteEscape(kind.ToString()).Write("</p>").WriteLine();
            })
            .Build();

        var html = MarkdownConverter.ToHtml("> [!WARNING]\n> The file is overwritten.", pipeline);
        Assert.Equal("<div class=\"markdown-alert markdown-alert-warning\">\n<p class=\"alert-title\">WARNING</p>\n<p>The file is overwritten.</p>\n</div>\n", html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_CustomContainers()
    {
        #region MarkdownExtensions_UseCustomContainers
        var pipeline = new MarkdownPipelineBuilder()
            .UseCustomContainers()
            .UseGenericAttributes() // To give a class to the inline container
            .Build();

        var html = MarkdownConverter.ToHtml(":::warning\nCheck the settings first.\n:::\n\nSome ::highlighted text::{.highlight}.", pipeline);
        Assert.Equal("<div class=\"warning\"><p>Check the settings first.</p>\n</div>\n<p>Some <span class=\"highlight\">highlighted text</span>.</p>\n", html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_DefinitionLists()
    {
        #region MarkdownExtensions_UseDefinitionLists
        var pipeline = new MarkdownPipelineBuilder()
            .UseDefinitionLists()
            .Build();

        var html = MarkdownConverter.ToHtml("CommonMark\n:   A strongly defined specification of Markdown.", pipeline);
        Assert.Equal("<dl>\n<dt>CommonMark</dt>\n<dd>A strongly defined specification of Markdown.</dd>\n</dl>\n", html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_FiguresFootersCitations()
    {
        #region MarkdownExtensions_UseFiguresFootersAndCitations
        var pipeline = new MarkdownPipelineBuilder()
            .UseFigures()
            .UseFooters()
            .UseCitations()
            .Build();

        var figure = MarkdownConverter.ToHtml("^^^\n![The logo](logo.png)\n^^^ The logo of the project", pipeline);
        Assert.Equal("<figure>\n<p><img src=\"logo.png\" alt=\"The logo\" /></p>\n<figcaption>The logo of the project</figcaption>\n</figure>\n", figure);

        var footer = MarkdownConverter.ToHtml("^^ Written by the documentation team", pipeline);
        Assert.Equal("<footer>Written by the documentation team</footer>\n", footer);

        var citation = MarkdownConverter.ToHtml("As described in \"\"The Art of Computer Programming\"\".", pipeline);
        Assert.Equal("<p>As described in <cite>The Art of Computer Programming</cite>.</p>\n", citation);
        #endregion
    }

    [Fact]
    public void DocumentationExample_Footnotes()
    {
        #region MarkdownExtensions_UseFootnotes
        var pipeline = new MarkdownPipelineBuilder()
            .UseFootnotes()
            .Build();

        var html = MarkdownConverter.ToHtml("Markdown was created in 2004[^1].\n\n[^1]: By John Gruber.", pipeline);
        Assert.Equal("""
            <p>Markdown was created in 2004<a id="fnref:1" href="#fn:1" class="footnote-ref"><sup>1</sup></a>.</p>
            <div class="footnotes">
            <hr />
            <ol>
            <li id="fn:1">
            <p>By John Gruber.<a href="#fnref:1" class="footnote-back-ref">&#8617;</a></p>
            </li>
            </ol>
            </div>

            """.ReplaceLineEndings("\n"), html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_TaskLists()
    {
        #region MarkdownExtensions_UseTaskLists
        var pipeline = new MarkdownPipelineBuilder()
            .UseTaskLists()
            .Build();

        var html = MarkdownConverter.ToHtml("- [x] Write the code\n- [ ] Write the documentation", pipeline);
        Assert.Equal("""
            <ul class="contains-task-list">
            <li class="task-list-item"><input disabled="disabled" type="checkbox" checked="checked" /> Write the code</li>
            <li class="task-list-item"><input disabled="disabled" type="checkbox" /> Write the documentation</li>
            </ul>

            """.ReplaceLineEndings("\n"), html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_ListExtras()
    {
        #region MarkdownExtensions_UseListExtras
        var pipeline = new MarkdownPipelineBuilder()
            .UseListExtras()
            .Build();

        Assert.Equal("<ol type=\"a\">\n<li>First</li>\n<li>Second</li>\n</ol>\n", MarkdownConverter.ToHtml("a. First\nb. Second", pipeline));
        Assert.Equal("<ol type=\"i\">\n<li>First</li>\n<li>Second</li>\n</ol>\n", MarkdownConverter.ToHtml("i. First\nii. Second", pipeline));
        #endregion
    }

    [Fact]
    public void DocumentationExample_Mathematics()
    {
        #region MarkdownExtensions_UseMathematics
        var pipeline = new MarkdownPipelineBuilder()
            .UseMathematics()
            .Build();

        var html = MarkdownConverter.ToHtml("The area is $\\pi r^2$:\n\n$$\nA = \\pi r^2\n$$", pipeline);
        Assert.Equal("<p>The area is <span class=\"math\">\\(\\pi r^2\\)</span>:</p>\n<div class=\"math\">\n\\[\nA = \\pi r^2\n\\]</div>\n", html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_Diagrams()
    {
        #region MarkdownExtensions_UseDiagrams
        var pipeline = new MarkdownPipelineBuilder()
            .UseDiagrams()
            .Build();

        var html = MarkdownConverter.ToHtml("```mermaid\ngraph LR\n    Parse --> Render\n```", pipeline);
        Assert.Equal("<pre class=\"mermaid\">graph LR\n    Parse --> Render\n</pre>\n", html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_Abbreviations()
    {
        #region MarkdownExtensions_UseAbbreviations
        var pipeline = new MarkdownPipelineBuilder()
            .UseAbbreviations()
            .Build();

        var html = MarkdownConverter.ToHtml("*[HTML]: Hyper Text Markup Language\n\nThe HTML output.", pipeline);
        Assert.Equal("<p>The <abbr title=\"Hyper Text Markup Language\">HTML</abbr> output.</p>\n", html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_AutoIdentifiers()
    {
        #region MarkdownExtensions_UseAutoIdentifiers
        var pipeline = new MarkdownPipelineBuilder()
            .UseAutoIdentifiers()
            .Build();

        var html = MarkdownConverter.ToHtml("# Getting started\n\n## Notes\n\n## Notes\n\nSee [Getting started].", pipeline);
        Assert.Equal("""
            <h1 id="getting-started">Getting started</h1>
            <h2 id="notes">Notes</h2>
            <h2 id="notes-1">Notes</h2>
            <p>See <a href="#getting-started">Getting started</a>.</p>

            """.ReplaceLineEndings("\n"), html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_AutoIdentifiersGitHub()
    {
        #region MarkdownExtensions_UseAutoIdentifiersGitHub
        var markdown = "# Été 2024!";

        var defaultPipeline = new MarkdownPipelineBuilder().UseAutoIdentifiers().Build();
        Assert.Equal("<h1 id=\"ete-2024\">Été 2024!</h1>\n", MarkdownConverter.ToHtml(markdown, defaultPipeline));

        var gitHubPipeline = new MarkdownPipelineBuilder().UseAutoIdentifiers(AutoIdentifierOptions.GitHub).Build();
        Assert.Equal("<h1 id=\"été-2024\">Été 2024!</h1>\n", MarkdownConverter.ToHtml(markdown, gitHubPipeline));
        #endregion
    }

    [Fact]
    public void DocumentationExample_JiraLinks()
    {
        #region MarkdownExtensions_UseJiraLinks
        var pipeline = new MarkdownPipelineBuilder()
            .UseJiraLinks(new JiraLinkOptions("https://jira.example.com"))
            .Build();

        var html = MarkdownConverter.ToHtml("Fixed by PROJ-123 (see also PROJ-99)", pipeline);
        Assert.Equal("<p>Fixed by <a href=\"https://jira.example.com/browse/PROJ-123\" target=\"_blank\">PROJ-123</a> (see also <a href=\"https://jira.example.com/browse/PROJ-99\" target=\"_blank\">PROJ-99</a>)</p>\n", html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_GridTables()
    {
        #region MarkdownExtensions_UseGridTables
        var pipeline = new MarkdownPipelineBuilder()
            .UseGridTables()
            .Build();

        var html = MarkdownConverter.ToHtml("""
            +--------+--------+
            | Name   | Age    |
            +========+========+
            | Ann    | 32     |
            +--------+--------+
            """, pipeline);
        Assert.Equal("""
            <table>
            <col style="width:50%" />
            <col style="width:50%" />
            <thead>
            <tr>
            <th>Name</th>
            <th>Age</th>
            </tr>
            </thead>
            <tbody>
            <tr>
            <td>Ann</td>
            <td>32</td>
            </tr>
            </tbody>
            </table>

            """.ReplaceLineEndings("\n"), html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_Bootstrap()
    {
        #region MarkdownExtensions_UseBootstrap
        var pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseBootstrap()
            .Build();

        var html = MarkdownConverter.ToHtml("> A quote\n\n![Logo](logo.png)", pipeline);
        Assert.Equal("<blockquote class=\"blockquote\">\n<p>A quote</p>\n</blockquote>\n<p><img src=\"logo.png\" class=\"img-fluid\" alt=\"Logo\" /></p>\n", html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_Globalization()
    {
        #region MarkdownExtensions_UseGlobalization
        var pipeline = new MarkdownPipelineBuilder()
            .UseGlobalization()
            .Build();

        var html = MarkdownConverter.ToHtml("# שלום\n\nHello", pipeline);
        Assert.Equal("<h1 dir=\"rtl\">שלום</h1>\n<p>Hello</p>\n", html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_DisableHtml()
    {
        #region MarkdownExtensions_DisableHtml
        var markdown = "<script>alert(1)</script>\n\nSome <b>bold</b> text";

        Assert.Equal("<script>alert(1)</script>\n<p>Some <b>bold</b> text</p>\n", MarkdownConverter.ToHtml(markdown));

        var pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();
        Assert.Equal("<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>\n<p>Some &lt;b&gt;bold&lt;/b&gt; text</p>\n", MarkdownConverter.ToHtml(markdown, pipeline));
        #endregion
    }

    [Fact]
    public void DocumentationExample_NestingDepth()
    {
        #region MarkdownPipelineBuilder_MaximumNestingDepth
        var pipeline = new MarkdownPipelineBuilder { MaximumNestingDepth = 64 }
            .DisableHtml()
            .Build();

        static string? RenderUntrusted(string markdown, MarkdownPipeline pipeline)
        {
            try
            {
                return MarkdownConverter.ToHtml(markdown, pipeline);
            }
            catch (ArgumentException)
            {
                return null; // The document is nested too deeply
            }
        }

        Assert.Equal("<p>Hello</p>\n", RenderUntrusted("Hello", pipeline));
        Assert.Null(RenderUntrusted(new string('>', 100) + " Hello", pipeline));
        #endregion
    }

    [Fact]
    public void DocumentationExample_DocumentProcessed()
    {
        #region MarkdownPipelineBuilder_DocumentProcessed
        var builder = new MarkdownPipelineBuilder();
        builder.DocumentProcessed += document =>
        {
            // Runs after each parse, before the document is returned or rendered
            foreach (var link in document.Descendants<LinkInline>())
            {
                if (link.IsImage)
                {
                    link.GetAttributes().AddProperty("loading", "lazy");
                }
            }
        };
        var pipeline = builder.Build();

        var html = MarkdownConverter.ToHtml("![Logo](logo.png)", pipeline);
        Assert.Equal("<p><img src=\"logo.png\" loading=\"lazy\" alt=\"Logo\" /></p>\n", html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_BuildTree()
    {
        #region MarkdownDocument_AddAndRemoveBlocks
        var pipeline = new MarkdownPipelineBuilder().Build();
        var document = MarkdownConverter.Parse("# Release notes\n\n<!-- TODO -->\n\nFirst version.", pipeline);

        // Remove the HTML comments
        foreach (var block in document.Descendants<HtmlBlock>().ToList())
        {
            block.Remove();
        }

        // Add a paragraph at the end: "Thanks to **all** the contributors."
        var emphasis = new EmphasisInline { DelimiterChar = '*', DelimiterCount = 2 };
        emphasis.AppendChild(new LiteralInline("all"));
        var paragraph = new ParagraphBlock { Inline = new ContainerInline() };
        paragraph.Inline
            .AppendChild(new LiteralInline("Thanks to "))
            .AppendChild(emphasis)
            .AppendChild(new LiteralInline(" the contributors."));
        document.Add(paragraph);

        var html = document.ToHtml(pipeline);
        Assert.Equal("<h1>Release notes</h1>\n<p>First version.</p>\n<p>Thanks to <strong>all</strong> the contributors.</p>\n", html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_RoundtripChangeLink()
    {
        #region RoundtripRenderer_ChangeLink
        var markdown = "Read   [the  docs](http://example.com 'Docs')  first.\n\n*   item\n";
        var document = MarkdownConverter.Parse(markdown, trackTrivia: true);

        foreach (var link in document.Descendants<LinkInline>())
        {
            // The roundtrip renderer writes the URL as it is in the source (UnescapedUrl): change both
            link.Url = "https://example.com";
            link.UnescapedUrl = new StringSlice(link.Url);
        }

        using var writer = new StringWriter();
        new RoundtripRenderer(writer).Write(document);

        // Only the URL changed: the spaces, the quotes and the list marker are the same
        Assert.Equal("Read   [the  docs](https://example.com 'Docs')  first.\n\n*   item\n", writer.ToString());
        #endregion
    }

    [Fact]
    public void DocumentationExample_RoundtripAttributes()
    {
        #region RoundtripRenderer_ChangeAttributes
        var pipeline = new MarkdownPipelineBuilder()
            .EnableTrackTrivia()
            .UseGenericAttributes()
            .Build();
        var document = MarkdownConverter.Parse("# Introduction\n\nSome text.\n", pipeline);

        document.Descendants<HeadingBlock>().First().GetAttributes().Id = "intro";
        document.Descendants<ParagraphBlock>().First().GetAttributes().AddClass("lead");

        using var writer = new StringWriter();
        var renderer = new RoundtripRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Write(document);

        Assert.Equal("# Introduction {#intro}\n\nSome text. {.lead}\n", writer.ToString());
        #endregion
    }

    [Fact]
    public void DocumentationExample_OptionsBeforeAdvancedExtensions()
    {
        #region MarkdownExtensions_OptionsBeforeAdvancedExtensions
        // UseAdvancedExtensions keeps the extensions that are already enabled, with their options
        var pipeline = new MarkdownPipelineBuilder()
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            .UseAdvancedExtensions()
            .Build();

        var html = MarkdownConverter.ToHtml("# Été", pipeline);
        Assert.Equal("<h1 id=\"été\">Été</h1>\n", html);

        // In the other order, UseAutoIdentifiers does nothing: the extension is already enabled with the default options
        var ignored = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            .Build();
        Assert.Equal("<h1 id=\"ete\">Été</h1>\n", MarkdownConverter.ToHtml("# Été", ignored));
        #endregion
    }

    [Fact]
    public void DocumentationExample_WalkChildren()
    {
        #region ContainerBlock_WalkChildren
        var document = MarkdownConverter.Parse("- First\n- Second *item*");

        // A container block is a list of blocks
        var list = (ListBlock)document[0];
        Assert.False(list.IsOrdered);
        Assert.HasCount(2, list);

        // A leaf block, such as a paragraph, holds the inlines of its text
        var item = (ListItemBlock)list[1];
        var paragraph = (ParagraphBlock)item[0];
        Assert.Same(item, paragraph.Parent);

        // Inlines are linked to their parent and their siblings
        var text = (LiteralInline)paragraph.Inline!.FirstChild!;
        Assert.Equal("Second ", text.ToString());
        var emphasis = (EmphasisInline)text.NextSibling!;
        Assert.Equal("item", emphasis.FirstChild!.ToString());
        #endregion
    }

    [Fact]
    public void DocumentationExample_TableOfContents()
    {
        #region MarkdownDocument_TableOfContents
        var document = MarkdownConverter.Parse("# Guide\n\n## Install *the* package\n\nText\n\n## Use it");

        var outline = new List<string>();
        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            // The text of a heading is in its inlines: concatenate the literals to drop the markup
            var text = string.Concat(heading.Inline!.Descendants<LiteralInline>().Select(literal => literal.ToString()));
            outline.Add(new string(' ', (heading.Level - 1) * 2) + text);
        }

        Assert.Equal("Guide\n  Install the package\n  Use it", string.Join('\n', outline));
        #endregion
    }

    [Fact]
    public void DocumentationExample_DumpTo()
    {
        #region Inline_DumpTo
        var document = MarkdownConverter.Parse("Some *text* here.");
        var paragraph = (ParagraphBlock)document[0];

        // Writes the tree of the inlines, to understand what the parser produced
        using var writer = new StringWriter();
        paragraph.Inline!.DumpTo(writer);

        // One line per inline, indented by its depth
        var lines = writer.ToString().ReplaceLineEndings("\n").Split('\n');
        Assert.Equal("  -> LiteralInline = Some ", lines[1]);
        Assert.StartsWith("  -> EmphasisInline", lines[2]);
        Assert.Equal("    -> LiteralInline = text", lines[3]);
        #endregion
    }
}
