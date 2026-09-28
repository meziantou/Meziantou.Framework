// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Text;

using Meziantou.Framework.Markdown.Extensions.Footnotes;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

/// <summary>
/// Test the precise source location of all Markdown elements, including extensions
/// </summary>

public class TestSourcePosition
{
    [Fact]
    public void TestParagraph()
    {
        Check("0123456789", @"
paragraph    ( 0, 0)  0-9
literal      ( 0, 0)  0-9
");
    }

    [Fact]
    public void TestParagraphAndNewLine()
    {
        Check("0123456789\n0123456789", @"
paragraph    ( 0, 0)  0-20
literal      ( 0, 0)  0-9
linebreak    ( 0,10) 10-10
literal      ( 1, 0) 11-20
");

        Check("0123456789\r\n0123456789", @"
paragraph    ( 0, 0)  0-21
literal      ( 0, 0)  0-9
linebreak    ( 0,10) 10-10
literal      ( 1, 0) 12-21
");
    }

    [Fact]
    public void TestParagraphNewLineAndSpaces()
    {
        //     0123 45678
        Check("012\n  345", @"
paragraph    ( 0, 0)  0-8
literal      ( 0, 0)  0-2
linebreak    ( 0, 3)  3-3
literal      ( 1, 2)  6-8
");
    }

    [Fact]
    public void TestParagraph2()
    {
        Check("0123456789\n\n0123456789", @"
paragraph    ( 0, 0)  0-9
literal      ( 0, 0)  0-9
paragraph    ( 2, 0) 12-21
literal      ( 2, 0) 12-21
");
    }

    [Fact]
    public void TestParagraphWithEndNewLine()
    {
        Check("0123456789\n", @"
paragraph    ( 0, 0)  0-10
literal      ( 0, 0)  0-9
linebreak    ( 0,10) 10-10
", trackTrivia: true);

        Check("0123456789\r", @"
paragraph    ( 0, 0)  0-10
literal      ( 0, 0)  0-9
linebreak    ( 0,10) 10-10
", trackTrivia: true);

        Check("0123456789\r\n", @"
paragraph    ( 0, 0)  0-11
literal      ( 0, 0)  0-9
linebreak    ( 0,10) 10-11
", trackTrivia: true);
    }


    [Fact]
    public void TestMultipleParagraphsWithEndNewLine()
    {
        Check("0123456789\n\n0123456789\n\n", @"
paragraph    ( 0, 0)  0-10
literal      ( 0, 0)  0-9
linebreak    ( 0,10) 10-10
paragraph    ( 2, 0) 12-22
literal      ( 2, 0) 12-21
linebreak    ( 2,10) 22-22
", trackTrivia: true);

        Check("0123456789\r\r0123456789\r\r", @"
paragraph    ( 0, 0)  0-10
literal      ( 0, 0)  0-9
linebreak    ( 0,10) 10-10
paragraph    ( 2, 0) 12-22
literal      ( 2, 0) 12-21
linebreak    ( 2,10) 22-22
", trackTrivia: true);

        Check("0123456789\r\n\r\n0123456789\r\n\r\n", @"
paragraph    ( 0, 0)  0-11
literal      ( 0, 0)  0-9
linebreak    ( 0,10) 10-11
paragraph    ( 2, 0) 14-25
literal      ( 2, 0) 14-23
linebreak    ( 2,10) 24-25
", trackTrivia: true);
    }

    [Fact]
    public void TestEmphasis()
    {
        Check("012**3456789**", @"
paragraph    ( 0, 0)  0-13
literal      ( 0, 0)  0-2
emphasis     ( 0, 3)  3-13
literal      ( 0, 5)  5-11
");
    }

    [Fact]
    public void TestEmphasis2()
    {
        //     01234567
        Check("01*2**3*", @"
paragraph    ( 0, 0)  0-7
literal      ( 0, 0)  0-1
emphasis     ( 0, 2)  2-7
literal      ( 0, 3)  3-3
literal      ( 0, 4)  4-5
literal      ( 0, 6)  6-6
");
    }

    [Fact]
    public void TestEmphasis3()
    {
        //     0123456789
        Check("01**2***3*", @"
paragraph    ( 0, 0)  0-9
literal      ( 0, 0)  0-1
emphasis     ( 0, 2)  2-6
literal      ( 0, 4)  4-4
emphasis     ( 0, 7)  7-9
literal      ( 0, 8)  8-8
");
    }

    [Fact]
    public void TestEmphasis4()
    {
        Check("**foo*", @"
paragraph    ( 0, 0)  0-5
literal      ( 0, 0)  0-0
emphasis     ( 0, 1)  1-5
literal      ( 0, 2)  2-4
");
    }

    [Fact]
    public void TestEmphasisFalse()
    {
        Check("0123456789**0123", @"
paragraph    ( 0, 0)  0-15
literal      ( 0, 0)  0-9
literal      ( 0,10) 10-11
literal      ( 0,12) 12-15
");
    }

    [Fact]
    public void TestHeading()
    {
        //     012345
        Check("# 2345", @"
heading      ( 0, 0)  0-5
literal      ( 0, 2)  2-5
");
    }

    [Fact]
    public void TestSetextHeading()
    {
        //     01 2 34 5
        Check("A\n\nB\n-", @"
paragraph    ( 0, 0)  0-0
literal      ( 0, 0)  0-0
heading      ( 3, 0)  3-5
literal      ( 3, 0)  3-3");
    }

    [Fact]
    public void TestHeadingWithEmphasis()
    {
        //     0123456789
        Check("# 23**45**", @"
heading      ( 0, 0)  0-9
literal      ( 0, 2)  2-3
emphasis     ( 0, 4)  4-9
literal      ( 0, 6)  6-7
");
    }

    [Fact]
    public void TestFootnoteLinkReferenceDefinition()
    {
        //                             01 2 345678
        var footnote = MarkdownConverter.Parse("0\n\n [^1]:", new MarkdownPipelineBuilder().UsePreciseSourceLocation().UseFootnotes().Build()).Descendants<FootnoteLinkReferenceDefinition>().FirstOrDefault();
        Assert.NotNull(footnote);

        Assert.Equal(2, footnote.Line);
        Assert.Equal(new SourceSpan(4, 7), footnote.Span);
        Assert.Equal(new SourceSpan(5, 6), footnote.LabelSpan);
    }

    [Fact]
    public void TestLinkReferenceDefinition1()
    {
        //                         0         1
        //                         0123456789012345
        var link = MarkdownConverter.Parse("[234]: /56 'yo' ", new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build()).Descendants<LinkReferenceDefinition>().FirstOrDefault();
        Assert.NotNull(link);

        Assert.Equal(0, link.Line);
        Assert.Equal(new SourceSpan(0, 14), link.Span);
        Assert.Equal(new SourceSpan(1, 3), link.LabelSpan);
        Assert.Equal(new SourceSpan(7, 9), link.UrlSpan);
        Assert.Equal(new SourceSpan(11, 14), link.TitleSpan);
    }

    [Fact]
    public void TestLinkReferenceDefinitionFollowedByATitleAndText()
    {
        //                                      0          1
        //                                      0123456789 0 12 34567
        var document = MarkdownConverter.Parse("[a]: /u  \n\"t\" junk", new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build());
        var link = document.Descendants<LinkReferenceDefinition>().FirstOrDefault();
        Assert.NotNull(link);

        Assert.Null(link.Title);
        Assert.Equal(new SourceSpan(0, 6), link.Span);
        Assert.Equal(new SourceSpan(5, 6), link.UrlSpan);
        Assert.Equal(SourceSpan.Empty, link.TitleSpan);

        // The paragraph starts with the title, not with an empty line
        var inline = document.Descendants<ParagraphBlock>().Single().Inline;
        Assert.NotNull(inline);
        Assert.IsType<LiteralInline>(inline.FirstChild);
        Assert.Equal(10, inline.FirstChild.Span.Start);
        Assert.Equal(17, inline.LastChild!.Span.End);
    }

    [Fact]
    public void TestLinkReferenceDefinition2()
    {
        //                         0          1
        //                         01 2 34567890123456789
        var link = MarkdownConverter.Parse("0\n\n [234]: /56 'yo' ", new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build()).Descendants<LinkReferenceDefinition>().FirstOrDefault();
        Assert.NotNull(link);

        Assert.Equal(2, link.Line);
        Assert.Equal(new SourceSpan(4, 18), link.Span);
        Assert.Equal(new SourceSpan(5, 7), link.LabelSpan);
        Assert.Equal(new SourceSpan(11, 13), link.UrlSpan);
        Assert.Equal(new SourceSpan(15, 18), link.TitleSpan);
    }

    [Fact]
    public void TestCodeSpan()
    {
        //     012345678
        Check("0123`456`", @"
paragraph    ( 0, 0)  0-8
literal      ( 0, 0)  0-3
code         ( 0, 4)  4-8
");
    }

    [Fact]
    public void TestLink()
    {
        //     0123456789
        Check("012[45](#)", @"
paragraph    ( 0, 0)  0-9
literal      ( 0, 0)  0-2
link         ( 0, 3)  3-9
literal      ( 0, 4)  4-5
");
    }

    [Fact]
    public void TestLinkParts1()
    {
        //                         0           1
        //                         01 2 3456789012345
        var link = MarkdownConverter.Parse("0\n\n01 [234](/56)", new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build()).Descendants<LinkInline>().FirstOrDefault();
        Assert.NotNull(link);

        Assert.Equal(new SourceSpan(7, 9), link.LabelSpan);
        Assert.Equal(new SourceSpan(12, 14), link.UrlSpan);
        Assert.Equal(SourceSpan.Empty, link.TitleSpan);
    }

    [Fact]
    public void TestLinkParts2()
    {
        //                         0           1
        //                         01 2 34567890123456789
        var link = MarkdownConverter.Parse("0\n\n01 [234](/56 'yo')", new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build()).Descendants<LinkInline>().FirstOrDefault();
        Assert.NotNull(link);

        Assert.Equal(new SourceSpan(7, 9), link.LabelSpan);
        Assert.Equal(new SourceSpan(12, 14), link.UrlSpan);
        Assert.Equal(new SourceSpan(16, 19), link.TitleSpan);
    }


    [Fact]
    public void TestLinkParts3()
    {
        //                         0           1
        //                         01 2 3456789012345
        var link = MarkdownConverter.Parse("0\n\n01![234](/56)", new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build()).Descendants<LinkInline>().FirstOrDefault();
        Assert.NotNull(link);

        Assert.Equal(new SourceSpan(5, 15), link.Span);
        Assert.Equal(new SourceSpan(7, 9), link.LabelSpan);
        Assert.Equal(new SourceSpan(12, 14), link.UrlSpan);
        Assert.Equal(SourceSpan.Empty, link.TitleSpan);
    }

    [Fact]
    public void TestUnresolvedReferenceLink()
    {
        Check("a\n[b][c] d", @"
paragraph    ( 0, 0)  0-9
literal      ( 0, 0)  0-0
linebreak    ( 0, 1)  1-1
literal      ( 1, 0)  2-2
literal      ( 1, 1)  3-4
literal      ( 1, 3)  5-5
literal      ( 1, 4)  6-9
");
    }

    [Fact]
    public void TestAutolinkInline()
    {
        //     0123456789ABCD
        Check("01<http://yes>", @"
paragraph    ( 0, 0)  0-13
literal      ( 0, 0)  0-1
autolink     ( 0, 2)  2-13
");
    }

    [Fact]
    public void TestFencedCodeBlock()
    {
        //     012 3456 78 9ABC
        Check("01\n```\n3\n```\n", @"
paragraph    ( 0, 0)  0-1
literal      ( 0, 0)  0-1
fencedcode   ( 1, 0)  3-11
");
    }

    [Fact]
    public void TestHtmlBlock()
    {
        //     012345 67 89ABCDE F 0
        Check("<div>\n0\n</div>\n\n1", @"
html         ( 0, 0)  0-13
paragraph    ( 4, 0) 16-16
literal      ( 4, 0) 16-16
");
    }

    [Fact]
    public void TestHtmlBlock1()
    {
        //     0           1
        //     01 2 345678901 23
        Check("0\n\n<!--A-->\n1\n", @"
paragraph    ( 0, 0)  0-0
literal      ( 0, 0)  0-0
html         ( 2, 0)  3-10
paragraph    ( 3, 0) 12-12
literal      ( 3, 0) 12-12
");
    }

    [Fact]
    public void TestHtmlComment()
    {
        //     0         1          2
        //     012345678901 234567890 1234
        Check("# 012345678\n<!--0-->\n123\n", @"
heading      ( 0, 0)  0-10
literal      ( 0, 2)  2-10
html         ( 1, 0) 12-19
paragraph    ( 2, 0) 21-23
literal      ( 2, 0) 21-23
");
    }

    [Fact]
    public void TestHtmlInline()
    {
        //     0123456789
        Check("01<b>4</b>", @"
paragraph    ( 0, 0)  0-9
literal      ( 0, 0)  0-1
html         ( 0, 2)  2-4
literal      ( 0, 5)  5-5
html         ( 0, 6)  6-9
");
    }

    [Fact]
    public void TestHtmlInline1()
    {
        //     0
        //     0123456789
        Check("0<!--A-->1", @"
paragraph    ( 0, 0)  0-9
literal      ( 0, 0)  0-0
html         ( 0, 1)  1-8
literal      ( 0, 9)  9-9
");
    }

    [Fact]
    public void TestThematicBreak()
    {
        //     0123 4567
        Check("---\n---\n", @"
thematicbreak ( 0, 0)  0-2
thematicbreak ( 1, 0)  4-6
");
    }

    [Fact]
    public void TestQuoteBlock()
    {
        //     0123456
        Check("> 2345\n", @"
quote        ( 0, 0)  0-5
paragraph    ( 0, 2)  2-5
literal      ( 0, 2)  2-5
");
    }

    [Fact]
    public void TestQuoteBlockWithLines()
    {
        //     01234 56789A
        Check("> 01\n>  23\n", @"
quote        ( 0, 0)  0-9
paragraph    ( 0, 2)  2-9
literal      ( 0, 2)  2-3
linebreak    ( 0, 4)  4-4
literal      ( 1, 3)  8-9
");
    }

    [Fact]
    public void TestQuoteBlockWithLazyContinuation()
    {
        //     01234 56
        Check("> 01\n23\n", @"
quote        ( 0, 0)  0-6
paragraph    ( 0, 2)  2-6
literal      ( 0, 2)  2-3
linebreak    ( 0, 4)  4-4
literal      ( 1, 0)  5-6
");
    }

    [Fact]
    public void TestListBlock()
    {
        //     0123 4567
        Check("- 0\n- 1\n", @"
list         ( 0, 0)  0-6
listitem     ( 0, 0)  0-2
paragraph    ( 0, 2)  2-2
literal      ( 0, 2)  2-2
listitem     ( 1, 0)  4-6
paragraph    ( 1, 2)  6-6
literal      ( 1, 2)  6-6
");
    }

    [Fact]
    public void TestNestedListBlocksWithBlankLines()
    {
        // Each blank line updates the span end of the open list items, which must reach their ancestors
        Check("- a\n  - b\n\n    c\n\n\n- d\n* e\n\n\n", @"
list         ( 0, 0)  0-21
listitem     ( 0, 0)  0-17
paragraph    ( 0, 2)  2-2
literal      ( 0, 2)  2-2
list         ( 1, 2)  6-17
listitem     ( 1, 2)  6-17
paragraph    ( 1, 4)  8-8
literal      ( 1, 4)  8-8
paragraph    ( 3, 4) 15-15
literal      ( 3, 4) 15-15
listitem     ( 6, 0) 19-21
paragraph    ( 6, 2) 21-21
literal      ( 6, 2) 21-21
list         ( 7, 0) 23-27
listitem     ( 7, 0) 23-27
paragraph    ( 7, 2) 25-25
literal      ( 7, 2) 25-25
");
    }

    [Fact]
    public void TestNestedQuoteAndListBlocksWithBlankLines()
    {
        Check("> - > a\n>\n>   > b\n\n> ```js\n>\n10. ![i](/u)\n", @"
quote        ( 0, 0)  0-16
list         ( 0, 2)  2-16
listitem     ( 0, 2)  2-16
quote        ( 0, 4)  4-6
paragraph    ( 0, 6)  6-6
literal      ( 0, 6)  6-6
quote        ( 2, 4) 14-16
paragraph    ( 2, 6) 16-16
literal      ( 2, 6) 16-16
quote        ( 4, 0) 19-27
fencedcode   ( 4, 2) 21-24
attributes   ( 0, 0)  0--1
list         ( 6, 0) 29-40
listitem     ( 6, 0) 29-40
paragraph    ( 6, 4) 33-40
link         ( 6, 4) 33-40
literal      ( 6, 6) 35-35
");
    }

    [Theory]
    [InlineData("123456789. ok\n", 12)]
    [InlineData("- foo\n  - bar\n    - baz\n      - boo\n", 34)]
    [InlineData("> 1. > Blockquote\ncontinued here.\n", 32)]
    [InlineData("- a\n  - b\n\n    c\n\n\n- d\n* e\n\n\n", 27)]
    public void TestDocumentSpanIncludesNestedBlocks(string text, int expectedEnd)
    {
        // The span end of a block is propagated to all its ancestors that end before it, up to the document
        var document = MarkdownConverter.Parse(text, new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build());

        Assert.Equal(new SourceSpan(0, expectedEnd), document.Span);
    }

    [Fact]
    public void TestListBlock2()
    {
        string test = @"
1. Foo
9. Bar
5. Foo
6. Bar
987123. FooBar";
        test = test.Replace("\r\n", "\n", StringComparison.Ordinal);
        var list = MarkdownConverter.Parse(test, new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build()).Descendants<ListBlock>().FirstOrDefault();
        Assert.NotNull(list);

        Assert.Equal(1, list.Line);
        Assert.True(list.IsOrdered);
        List<ListItemBlock> items = list.Cast<ListItemBlock>().ToList();
        Assert.HasCount(5, items);

        // Test orders
        Assert.Equal(1, items[0].Order);
        Assert.Equal(9, items[1].Order);
        Assert.Equal(5, items[2].Order);
        Assert.Equal(6, items[3].Order);
        Assert.Equal(987123, items[4].Order);

        // Test positions
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(i + 1, items[i].Line);
            Assert.Equal(1 + (i * 7), items[i].Span.Start);
            Assert.Equal(6, items[i].Span.Length);
        }
        Assert.Equal(5, items[4].Line);
        Assert.Equal(new SourceSpan(29, 42), items[4].Span);
    }

    [Fact]
    public void TestEscapeInline()
    {
        //      0123
        Check(@"\-\)", @"
paragraph    ( 0, 0)  0-3
literal      ( 0, 0)  0-1
literal      ( 0, 2)  2-3
");
    }

    [Fact]
    public void TestHtmlEntityInline()
    {
        //     01 23456789
        Check("0\n&nbsp; 1", @"
paragraph    ( 0, 0)  0-9
literal      ( 0, 0)  0-0
linebreak    ( 0, 1)  1-1
htmlentity   ( 1, 0)  2-7
literal      ( 1, 6)  8-9
");
    }

    [Fact]
    public void TestAbbreviations()
    {
        Check("*[HTML]: Hypertext Markup Language\r\n\r\nLater in a text we are using HTML and it becomes an abbr tag HTML\r\n\r\nHTML abbreviation at the beginning of a line", @"
paragraph    ( 2, 0) 38-102
literal      ( 2, 0) 38-66
abbreviation ( 2,29) 67-70
literal      ( 2,33) 71-98
abbreviation ( 2,61) 99-102
paragraph    ( 4, 0) 107-150
abbreviation ( 4, 0) 107-110
literal      ( 4, 4) 111-150
", "abbreviations");
    }

    [Fact]
    public void TestCitation()
    {
        //     0123 4 567 8
        Check("01 \"\"23\"\"", @"
paragraph    ( 0, 0)  0-8
literal      ( 0, 0)  0-2
emphasis     ( 0, 3)  3-8
literal      ( 0, 5)  5-6
", "citations");
    }

    [Fact]
    public void TestCustomContainer()
    {
        //     01 2345 678 9ABC DEF
        Check("0\n:::\n23\n:::\n45\n", @"
paragraph    ( 0, 0)  0-0
literal      ( 0, 0)  0-0
customcontainer ( 1, 0)  2-11
paragraph    ( 2, 0)  6-7
literal      ( 2, 0)  6-7
paragraph    ( 4, 0) 13-14
literal      ( 4, 0) 13-14
", "customcontainers");
    }

    [Fact]
    public void TestDefinitionList()
    {
        //     012 3456789A
        Check("a0\n:   1234", @"
definitionlist ( 0, 0)  0-10
definitionitem ( 1, 0)  0-10
definitionterm ( 0, 0)  0-1
literal      ( 0, 0)  0-1
paragraph    ( 1, 4)  7-10
literal      ( 1, 4)  7-10
", "definitionlists");
    }

    [Fact]
    public void TestDefinitionList2()
    {
        //     012 3456789AB CDEF01234
        Check("a0\n:   1234\n:    5678", @"
definitionlist ( 0, 0)  0-20
definitionitem ( 1, 0)  0-10
definitionterm ( 0, 0)  0-1
literal      ( 0, 0)  0-1
paragraph    ( 1, 4)  7-10
literal      ( 1, 4)  7-10
definitionitem ( 2, 4) 12-20
paragraph    ( 2, 5) 17-20
literal      ( 2, 5) 17-20
", "definitionlists");
    }

    [Fact]
    public void TestEmoji()
    {
        //     01 2345
        Check("0\n :)\n", @"
paragraph    ( 0, 0)  0-4
literal      ( 0, 0)  0-0
linebreak    ( 0, 1)  1-1
emoji        ( 1, 1)  3-4
", "emojis");
    }

    [Fact]
    public void TestEmphasisExtra()
    {
        //     0123456
        Check("0 ~~1~~", @"
paragraph    ( 0, 0)  0-6
literal      ( 0, 0)  0-1
emphasis     ( 0, 2)  2-6
literal      ( 0, 4)  4-4
", "emphasisextras");
    }

    [Fact]
    public void TestFigures()
    {
        //     01 2345 67 89AB
        Check("0\n^^^\n0\n^^^\n", @"
paragraph    ( 0, 0)  0-0
literal      ( 0, 0)  0-0
figure       ( 1, 0)  2-10
paragraph    ( 2, 0)  6-6
literal      ( 2, 0)  6-6
", "figures");
    }

    [Fact]
    public void TestFiguresCaption1()
    {
        //     01 234567 89 ABCD
        Check("0\n^^^ab\n0\n^^^\n", @"
paragraph    ( 0, 0)  0-0
literal      ( 0, 0)  0-0
figure       ( 1, 0)  2-12
figurecaption ( 1, 3)  5-6
literal      ( 1, 3)  5-6
paragraph    ( 2, 0)  8-8
literal      ( 2, 0)  8-8
", "figures");
    }

    [Fact]
    public void TestFiguresCaption2()
    {
        //     01 2345 67 89ABCD
        Check("0\n^^^\n0\n^^^ab\n", @"
paragraph    ( 0, 0)  0-0
literal      ( 0, 0)  0-0
figure       ( 1, 0)  2-12
paragraph    ( 2, 0)  6-6
literal      ( 2, 0)  6-6
figurecaption ( 3, 3) 11-12
literal      ( 3, 3) 11-12
", "figures");
    }

    [Fact]
    public void TestFooters()
    {
        //     01 234567 89ABCD
        Check("0\n^^ 12\n^^ 34\n", @"
paragraph    ( 0, 0)  0-0
literal      ( 0, 0)  0-0
footer       ( 1, 0)  2-12
paragraph    ( 1, 3)  5-12
literal      ( 1, 3)  5-6
linebreak    ( 1, 5)  7-7
literal      ( 2, 3) 11-12
", "footers");
    }


    [Fact]
    public void TestAttributes()
    {
        //     0123456789
        Check("0123{#456}", @"
paragraph    ( 0, 0)  0-9
attributes   ( 0, 4)  4-9
literal      ( 0, 0)  0-3
", "attributes");
    }

    [Fact]
    public void TestAttributesForHeading()
    {
        //     0123456789ABC
        Check("# 01 {#456}", @"
heading      ( 0, 0)  0-4
attributes   ( 0, 5)  5-10
literal      ( 0, 2)  2-3
", "attributes");
    }

    [Fact]
    public void TestMathematicsInline()
    {
        //     01 23456789ABCDEF
        Check("0\n012 $abcd$ 321", @"
paragraph    ( 0, 0)  0-15
literal      ( 0, 0)  0-0
linebreak    ( 0, 1)  1-1
literal      ( 1, 0)  2-5
math         ( 1, 4)  6-11
attributes   ( 0, 0)  0--1
literal      ( 1,10) 12-15
", "mathematics");

        //     012345678
        Check("$ abcd $", @"
paragraph    ( 0, 0)  0-7
math         ( 0, 0)  0-7
attributes   ( 0, 0)  0--1
", "mathematics");
    }

    [Fact]
    public void TestSmartyPants()
    {
        //        01234567
        //     01 23456789
        Check("0\n2 <<45>>", @"
paragraph    ( 0, 0)  0-9
literal      ( 0, 0)  0-0
linebreak    ( 0, 1)  1-1
literal      ( 1, 0)  2-3
smartypant   ( 1, 2)  4-5
literal      ( 1, 4)  6-7
smartypant   ( 1, 6)  8-9
", "smartypants");
    }

    [Fact]
    public void TestSmartyPantsUnbalanced()
    {
        //        012345
        //     01 234567
        Check("0\n2 <<45", @"
paragraph    ( 0, 0)  0-7
literal      ( 0, 0)  0-0
linebreak    ( 0, 1)  1-1
literal      ( 1, 0)  2-3
literal      ( 1, 2)  4-5
literal      ( 1, 4)  6-7
", "smartypants");
    }

    [Fact]
    public void TestPipeTable()
    {
        //     0123 4567 89AB
        Check("a|b\n-|-\n0|1\n", @"
table        ( 0, 0)  0-10
tablerow     ( 0, 0)  0-2
tablecell    ( 0, 0)  0-0
paragraph    ( 0, 0)  0-0
literal      ( 0, 0)  0-0
tablecell    ( 0, 2)  2-2
paragraph    ( 0, 2)  2-2
literal      ( 0, 2)  2-2
tablerow     ( 2, 0)  8-10
tablecell    ( 2, 0)  8-8
paragraph    ( 2, 0)  8-8
literal      ( 2, 0)  8-8
tablecell    ( 2, 2) 10-10
paragraph    ( 2, 2) 10-10
literal      ( 2, 2) 10-10
", "pipetables");
    }

    [Fact]
    public void TestPipeTable2()
    {
        //     01 2 3456 789A BCD
        Check("0\n\na|b\n-|-\n0|1\n", @"
paragraph    ( 0, 0)  0-0
literal      ( 0, 0)  0-0
table        ( 2, 0)  3-13
tablerow     ( 2, 0)  3-5
tablecell    ( 2, 0)  3-3
paragraph    ( 2, 0)  3-3
literal      ( 2, 0)  3-3
tablecell    ( 2, 2)  5-5
paragraph    ( 2, 2)  5-5
literal      ( 2, 2)  5-5
tablerow     ( 4, 0) 11-13
tablecell    ( 4, 0) 11-11
paragraph    ( 4, 0) 11-11
literal      ( 4, 0) 11-11
tablecell    ( 4, 2) 13-13
paragraph    ( 4, 2) 13-13
literal      ( 4, 2) 13-13
", "pipetables");
    }

    [Fact]
    public void TestPipeTable3()
    {
        //     01234 5678 9ABCD
        Check("|a|b\n-|-\n0|1|\n", @"
table        ( 0, 0)  0-12
tablerow     ( 0, 1)  1-3
tablecell    ( 0, 1)  1-1
paragraph    ( 0, 1)  1-1
literal      ( 0, 1)  1-1
tablecell    ( 0, 3)  3-3
paragraph    ( 0, 3)  3-3
literal      ( 0, 3)  3-3
tablerow     ( 2, 0)  9-11
tablecell    ( 2, 0)  9-9
paragraph    ( 2, 0)  9-9
literal      ( 2, 0)  9-9
tablecell    ( 2, 2) 11-11
paragraph    ( 2, 2) 11-11
literal      ( 2, 2) 11-11
", "pipetables");
    }

    [Fact]
    public void TestGridTable()
    {
        Check("0\n\n+-+-+\n|A|B|\n+=+=+\n|C|D|\n+-+-+", @"
paragraph    ( 0, 0)  0-0
literal      ( 0, 0)  0-0
table        ( 2, 0)  3-31
tablerow     ( 3, 0)  9-13
tablecell    ( 3, 0)  9-11
paragraph    ( 3, 1) 10-10
literal      ( 3, 1) 10-10
tablecell    ( 3, 2) 11-13
paragraph    ( 3, 3) 12-12
literal      ( 3, 3) 12-12
tablerow     ( 5, 0) 21-25
tablecell    ( 5, 0) 21-23
paragraph    ( 5, 1) 22-22
literal      ( 5, 1) 22-22
tablecell    ( 5, 2) 23-25
paragraph    ( 5, 3) 24-24
literal      ( 5, 3) 24-24", "gridtables");
    }

    [Fact]
    public void TestIndentedCode()
    {
        //     01 2 345678 9ABCDE
        Check("0\n\n    0\n    1\n", @"
paragraph    ( 0, 0)  0-0
literal      ( 0, 0)  0-0
code         ( 2, 0)  3-13
");
    }

    [Fact]
    public void TestIndentedCodeAfterList()
    {
        //     0         1           2         3          4         5
        //     012345678901234567 8 901234567890123456 789012345678901234 56789
        Check("1) Some list item\n\n        some code\n        more code\n", @"
list         ( 0, 0)  0-53
listitem     ( 0, 0)  0-53
paragraph    ( 0, 3)  3-16
literal      ( 0, 3)  3-16
code         ( 2, 0) 19-53
");
    }

    [Fact]
    public void TestIndentedCodeWithTabs()
    {
        //     01 2 3 45 6 78
        Check("0\n\n\t0\n\t1\n", @"
paragraph    ( 0, 0)  0-0
literal      ( 0, 0)  0-0
code         ( 2, 0)  3-7
");
    }

    [Fact]
    public void TestIndentedCodeWithMixedTabs()
    {
        //     01 2 34 56 78 9
        Check("0\n\n \t0\n \t1\n", @"
paragraph    ( 0, 0)  0-0
literal      ( 0, 0)  0-0
code         ( 2, 0)  3-9
");
    }

    [Fact]
    public void TestTabsInList()
    {
        //     012 34 567 89
        Check("- \t0\n- \t1\n", @"
list         ( 0, 0)  0-8
listitem     ( 0, 0)  0-3
paragraph    ( 0, 4)  3-3
literal      ( 0, 4)  3-3
listitem     ( 1, 0)  5-8
paragraph    ( 1, 4)  8-8
literal      ( 1, 4)  8-8
");
    }

    [Fact]
    public void TestDocument()
    {
        //     L0       L0           L1L2         L3         L4         L5L6                    L7L8
        //     0        10        20          30         40         50          60        70          80        90
        //     012345678901234567890 1 2345678901 2345678901 2345678901 2 345678901234567890123 4 5678901234567890123
        Check("# This is a document\n\n1) item 1\n2) item 2\n3) item 4\n\nWith an **emphasis**\n\n> and a blockquote\n", @"
heading      ( 0, 0)  0-19
literal      ( 0, 2)  2-19
list         ( 2, 0) 22-51
listitem     ( 2, 0) 22-30
paragraph    ( 2, 3) 25-30
literal      ( 2, 3) 25-30
listitem     ( 3, 0) 32-40
paragraph    ( 3, 3) 35-40
literal      ( 3, 3) 35-40
listitem     ( 4, 0) 42-51
paragraph    ( 4, 3) 45-50
literal      ( 4, 3) 45-50
paragraph    ( 6, 0) 53-72
literal      ( 6, 0) 53-60
emphasis     ( 6, 8) 61-72
literal      ( 6,10) 63-70
quote        ( 8, 0) 75-92
paragraph    ( 8, 2) 77-92
literal      ( 8, 2) 77-92
");
    }

    private static void Check(string text, string expectedResult, string? extensions = null, bool trackTrivia = false)
    {
        var pipelineBuilder = new MarkdownPipelineBuilder().UsePreciseSourceLocation();
        pipelineBuilder.TrackTrivia = trackTrivia;
        if (extensions is not null)
        {
            pipelineBuilder.Configure(extensions);
        }
        var pipeline = pipelineBuilder.Build();

        var document = MarkdownConverter.Parse(text, pipeline);

        var build = new StringBuilder();
        foreach (var val in document.Descendants())
        {
            var name = GetTypeName(val.GetType());
            build.Append(CultureInfo.InvariantCulture, $"{name,-12} ({val.Line,2},{val.Column,2}) {val.Span.Start,2}-{val.Span.End}\n");
            var attributes = val.TryGetAttributes();
            if (attributes is not null)
            {
                build.Append(CultureInfo.InvariantCulture, $"{"attributes",-12} ({attributes.Line,2},{attributes.Column,2}) {attributes.Span.Start,2}-{attributes.Span.End}\n");
            }
        }
        var result = build.ToString().Trim();

        expectedResult = expectedResult.Trim();
        expectedResult = expectedResult.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

        if (expectedResult != result)
        {
            Console.WriteLine("```````````````````Source");
            Console.WriteLine(TestParser.DisplaySpaceAndTabs(text));
            Console.WriteLine("```````````````````Result");
            Console.WriteLine(result);
            Console.WriteLine("```````````````````Expected");
            Console.WriteLine(expectedResult);
            Console.WriteLine("```````````````````");
            Console.WriteLine();
        }

        TextAssert.AreEqual(expectedResult, result);
    }

    private static string GetTypeName(Type type)
    {
        return type.Name.ToLowerInvariant()
            .Replace("block", string.Empty, StringComparison.Ordinal)
            .Replace("inline", string.Empty, StringComparison.Ordinal);
    }
}
