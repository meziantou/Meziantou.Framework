using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;
using System.IO;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs;

public class TestExample
{
    [Fact]
    public void Test()
    {
        var markdown = $@"
# Test document
This document contains an unordered list. It uses tabs to indent. This test demonstrates
a method of making the input markdown uniform without altering any other markdown in the
resulting output file.

- item1

>look, ma:
> my space is not normalized!
";
        MarkdownDocument markdownDocument = MarkdownConverter.Parse(markdown, trackTrivia: true);
        var listBlock = (ListBlock)markdownDocument[2];
        var listItem = (ListItemBlock)listBlock[0];
        var paragraph = (ParagraphBlock)listItem[0];
        var containerInline = new ContainerInline();
        containerInline.AppendChild(new LiteralInline(" my own text!"));
        containerInline.AppendChild(new LineBreakInline { NewLine = NewLine.CarriageReturnLineFeed });
        paragraph.Inline = containerInline;

        var sw = new StringWriter();
        var rr = new RoundtripRenderer(sw);
        rr.Write(markdownDocument);
        var outputMarkdown = sw.ToString();
        var expected = $@"
# Test document
This document contains an unordered list. It uses tabs to indent. This test demonstrates
a method of making the input markdown uniform without altering any other markdown in the
resulting output file.

- my own text!

>look, ma:
> my space is not normalized!
";

        expected = expected.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        outputMarkdown = outputMarkdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

        Assert.Equal(expected, outputMarkdown);
    }
}
