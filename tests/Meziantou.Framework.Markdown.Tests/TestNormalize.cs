// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Extensions.Tables;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers.Normalize;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

public class TestNormalize
{
    [Fact]
    public void SyntaxCodeBlock()
    {
        AssertSyntax("````csharp\npublic void HelloWorld()\n{\n}\n````", new FencedCodeBlock(null!)
        {
            FencedChar = '`',
            OpeningFencedCharCount = 4,
            ClosingFencedCharCount = 4,
            Info = "csharp",
            Lines = new StringLineGroup(4)
            {
                new StringSlice("public void HelloWorld()"),
                new StringSlice("{"),
                new StringSlice("}"),
            }
        });

        AssertSyntax("    public void HelloWorld()\n    {\n    }", new CodeBlock(null!)
        {
            Lines = new StringLineGroup(4)
            {
                new StringSlice("public void HelloWorld()"),
                new StringSlice("{"),
                new StringSlice("}"),
            }
        });
    }

    [Fact]
    public void SyntaxHeadline()
    {
        AssertSyntax("## Headline", new HeadingBlock(null!)
        {
            HeaderChar = '#',
            Level = 2,
            Inline = new ContainerInline().AppendChild(new LiteralInline("Headline")),
        });
    }

    [Fact]
    public void SyntaxHeadlineLevel7()
    {
        AssertSyntax("####### Headline", new HeadingBlock(null!) {
            HeaderChar = '#',
            Level = 7,
            Inline = new ContainerInline().AppendChild(new LiteralInline("Headline")),
        });
    }

    [Fact]
    public void SyntaxParagraph()
    {
        AssertSyntax("This is a normal paragraph", new ParagraphBlock()
        {
            Inline = new ContainerInline()
                .AppendChild(new LiteralInline("This is a normal paragraph")),
        });

        AssertSyntax("This is a\nnormal\nparagraph", new ParagraphBlock()
        {
            Inline = new ContainerInline()
                .AppendChild(new LiteralInline("This is a"))
                .AppendChild(new LineBreakInline())
                .AppendChild(new LiteralInline("normal"))
                .AppendChild(new LineBreakInline())
                .AppendChild(new LiteralInline("paragraph")),
        });
    }

    [Fact]
    public void CodeBlock()
    {
        AssertNormalizeNoTrim("    public void HelloWorld();\n    {\n    }");
        AssertNormalizeNoTrim("    public void HelloWorld();\n    {\n    }\n\ntext after two newlines");
        AssertNormalizeNoTrim("````\npublic void HelloWorld();\n{\n}\n````\n\ntext after two newlines");
        AssertNormalizeNoTrim("````csharp\npublic void HelloWorld();\n{\n}\n````");
        AssertNormalizeNoTrim("````csharp hideNewKeyword=true\npublic void HelloWorld();\n{\n}\n````");
    }

    [Fact]
    public void Heading()
    {
        AssertNormalizeNoTrim("# Heading");
        AssertNormalizeNoTrim("## Heading");
        AssertNormalizeNoTrim("### Heading");
        AssertNormalizeNoTrim("#### Heading");
        AssertNormalizeNoTrim("##### Heading");
        AssertNormalizeNoTrim("###### Heading");
        AssertNormalizeNoTrim("###### Heading\n\ntext after two newlines");
        AssertNormalizeNoTrim("# Heading\nAnd Text1\n\nAndText2", options: new NormalizeOptions() { EmptyLineAfterHeading = false });

        AssertNormalizeNoTrim("Heading\n=======\n\ntext after two newlines", "# Heading\n\ntext after two newlines");
    }

    [Fact]
    public void AutoIdentifiersDoNotEmitGeneratedLinkReferenceDefinitions()
    {
        var pipeline = new MarkdownPipelineBuilder().UseAutoIdentifiers().Build();
        var document = MarkdownConverter.Parse("# Test\n\n## Test", pipeline);
        using var writer = new StringWriter();
        var renderer = new NormalizeRenderer(writer);

        renderer.Render(document);

        Assert.Equal("# Test\n\n## Test", writer.ToString());
    }

    [Fact]
    public void Backslash()
    {
        AssertNormalizeNoTrim("This is a hardline  \nAnd this is another hardline\\\nThis is standard newline");
        AssertNormalizeNoTrim("This is a line\nWith another line\nAnd a last line");
    }

    [Fact]
    public void HtmlBlock()
    {
        /*AssertNormalizeNoTrim(@"<div id=""foo"" class=""bar
baz"">
</ div >");*/ // TODO: Bug: Throws Exception during emit
    }

    [Fact]
    public void Paragraph()
    {
        AssertNormalizeNoTrim("This is a plain paragraph");
        AssertNormalizeNoTrim(@"This
is
a
plain
paragraph");
    }

    [Fact]
    public void ParagraphMulti()
    {
        AssertNormalizeNoTrim(@"line1

line2

line3");
    }

    [Fact]
    public void ListUnordered()
    {
        AssertNormalizeNoTrim(@"- a
- b
- c");
    }

    [Fact]
    public void ListUnorderedLoose()
    {
        AssertNormalizeNoTrim(@"- a

- b

- c");
    }

    [Fact]
    public void ListUnorderedSingleLineNested()
    {
        AssertNormalizeNoTrim("- - a");
    }

    [Fact]
    public void ListUnorderedWithQuoteBlock()
    {
        AssertNormalizeNoTrim("- > p");
    }

    [Theory]
    [InlineData("- >", "- > ")]
    [InlineData("1. >", "1. > ")]
    [InlineData("- >\n- a", "- > \n- a")]
    [InlineData("1. >\n2. a", "1. > \n2. a")]
    [InlineData("- > >", "- > > ")]
    [InlineData("- >\n\ntext", "- > \n\ntext")]
    [InlineData(">", "> ")]
    public void EmptyQuotePreservesMarkers(string markdown, string expected)
    {
        AssertNormalizeNoTrim(markdown, expected);
        AssertNormalizeNoTrim(expected);
    }

    [Fact]
    public void EmptyQuoteWithoutSpacePreservesMarkers()
    {
        AssertNormalizeNoTrim("- >", options: new NormalizeOptions { SpaceAfterQuoteBlock = false });
    }

    [Fact]
    public void ListOrderedWithQuoteBlocks()
    {
        AssertNormalizeNoTrim("List of blockquotes\n1. > first\n2. > second\n3. > third",
            "List of blockquotes\n\n1. > first\n2. > second\n3. > third");
        AssertNormalizeNoTrim("9. > first\n   > continuation\n10. > second\n    > continuation");
    }

    [Fact]
    public void HangingIndentUsesMarkerOnceAndComposesWithNestedIndents()
    {
        using var writer = new StringWriter();
        var renderer = new NormalizeRenderer(writer);
        renderer.PushHangingIndent("10. ");
        renderer.PushIndent("> ");
        renderer.WriteLine("first");
        renderer.WriteLine("second");
        renderer.PopIndent();
        renderer.WriteLine("third");
        renderer.PopIndent();
        renderer.Write("last");

        Assert.Equal("10. > first\n    > second\n    third\nlast", writer.ToString());
    }

    [Fact]
    public void HangingIndentRejectsNullWithoutChangingState()
    {
        using var writer = new StringWriter();
        var renderer = new NormalizeRenderer(writer);
        var exception = Assert.Throws<ArgumentNullException>(() => renderer.PushHangingIndent(null!));
        Assert.Equal("marker", exception.ParamName);
        renderer.Write("text");
        Assert.Equal("text", writer.ToString());
    }

    [Fact]
    public void LineSpecificIndentRejectsNullWithoutChangingState()
    {
        using var writer = new StringWriter();
        var renderer = new NormalizeRenderer(writer);
        renderer.PushIndent("> ");
        renderer.Write("first");

        var exception = Assert.Throws<ArgumentNullException>(() => renderer.PushIndent((string[])null!));
        Assert.Equal("lineSpecific", exception.ParamName);
        renderer.WriteLine(" second");
        renderer.Write("third");
        renderer.PopIndent();

        Assert.Equal("> first second\n> third", writer.ToString());
    }

    [Fact]
    public void LineSpecificIndentStopsAfterLastEntry()
    {
        using var writer = new StringWriter();
        var renderer = new NormalizeRenderer(writer);
        renderer.PushIndent(new[] { "first: ", "next: " });
        renderer.WriteLine("a");
        renderer.WriteLine("b");
        renderer.Write("c");
        renderer.PopIndent();

        Assert.Equal("first: a\nnext: b\nc", writer.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrderedListNormalizationRenumbersWithoutSourceBullets(bool trackTrivia)
    {
        var builder = new MarkdownPipelineBuilder();
        if (trackTrivia)
        {
            builder.EnableTrackTrivia();
        }
        var normalized = MarkdownConverter.Normalize("3. first\n9. second\n9. third", pipeline: builder.Build());
        var list = (ListBlock)MarkdownConverter.Parse(normalized)[0];
        Assert.Equal(new[] { 3, 4, 5 }, list.Select(item => ((ListItemBlock)item).Order).ToArray());
    }

    [Fact]
    public void ListUnorderedEmpty()
    {
        AssertNormalizeNoTrim("-", "- ");
        AssertNormalizeNoTrim("- ");
    }

    [Fact]
    public void ListOrderedLooseAndCodeBlock()
    {
        AssertNormalizeNoTrim("1. ```\n   foo\n   ```\n   \n   bar");
    }

    [Fact(Skip = "Not sure this is the correct normalize for this one. Need to check the specs")]
    public void ListUnorderedLooseTop()
    {
        AssertNormalizeNoTrim("* foo\n  * bar\n  \n  baz", options: new NormalizeOptions() { ListItemCharacter = '*' });
    }

    [Fact]
    public void ListUnorderedLooseMultiParagraph()
    {
        AssertNormalizeNoTrim("- a\n  \n  And another paragraph a\n\n- b\n  \n  And another paragraph b\n\n- c");
    }


    [Fact]
    public void ListOrdered()
    {
        AssertNormalizeNoTrim(@"1. a
2. b
3. c");
    }


    [Fact]
    public void ListOrderedAndIntended()
    {
        AssertNormalizeNoTrim(@"1. a
2. b
   - foo
   - bar
     a) 1234
     b) 1324
3. c
4. c
5. c
6. c
7. c
8. c
9. c
10. c
    - Foo
    - Bar
11. c
12. c");
    }

    [Fact]
    public void ListOrderedEmpty()
    {
        AssertNormalizeNoTrim("1.", "1. ");
        AssertNormalizeNoTrim("1. ");
    }

    [Fact]
    public void HeaderAndParagraph()
    {
        AssertNormalizeNoTrim(@"# heading

paragraph

paragraph2 without newlines");
    }


    [Fact]
    public void QuoteBlock()
    {
        AssertNormalizeNoTrim("> test1\n> \n> test2");

        AssertNormalizeNoTrim(@"> test1
This is a continuation
> test2",
            @"> test1
> This is a continuation
> test2"
);

        AssertNormalizeNoTrim(@"> test1
> -foobar

asdf

> test2
> -foobar sen.");
    }

    [Fact]
    public void ThematicBreak()
    {
        AssertNormalizeNoTrim("***\n");

        AssertNormalizeNoTrim("* * *\n", "***\n");
    }

    [Fact]
    public void AutolinkInline()
    {
        AssertNormalizeNoTrim("This has a <auto.link.com>");
    }

    [Fact]
    public void CodeInline()
    {
        AssertNormalizeNoTrim("This has a ` ` in it");
        AssertNormalizeNoTrim("This has a `HelloWorld()` in it");
        AssertNormalizeNoTrim(@"This has a ``Hello`World()`` in it");
        AssertNormalizeNoTrim(@"This has a ``` Hello`World() ``` in it", @"This has a ``Hello`World()`` in it");
        AssertNormalizeNoTrim(@"This has a ``Hello`World()` `` in it");
        AssertNormalizeNoTrim(@"This has a ```` ``Hello```World()` ```` in it");
        AssertNormalizeNoTrim(@"This has a `` `Hello`World()`` in it");
        AssertNormalizeNoTrim(@"This has a ``` ``Hello`World()` ``` in it");
    }

    [Fact]
    public void EmphasisInline()
    {
        AssertNormalizeNoTrim("This is a plain **paragraph**");
        AssertNormalizeNoTrim("This is a plain *paragraph*");
        AssertNormalizeNoTrim("This is a plain _paragraph_");
        AssertNormalizeNoTrim("This is a plain __paragraph__");
        AssertNormalizeNoTrim("This is a pl*ai*n **paragraph**");
    }

    [Fact]
    public void LineBreakInline()
    {
        AssertNormalizeNoTrim("normal\nline break");
        AssertNormalizeNoTrim("hard  \nline break");
        AssertNormalizeNoTrim("This is a hardline  \nAnd this is another hardline\\\nThis is standard newline");
        AssertNormalizeNoTrim("This is a line\nWith another line\nAnd a last line");
    }

    [Fact]
    public void LinkInline()
    {
        AssertNormalizeNoTrim("This is a [link](http://company.com)");
        AssertNormalizeNoTrim("This is an ![image](http://company.com)");

        AssertNormalizeNoTrim(@"This is a [link](http://company.com ""Crazy Company"")");
        AssertNormalizeNoTrim(@"This is a [link](http://company.com ""Crazy \"" Company"")");
    }

    [Fact]
    public void LinkReferenceDefinition()
    {
        // Full link
        AssertNormalizeNoTrim("This is a [link][MyLink]\n\n[MyLink]: http://company.com");

        AssertNormalizeNoTrim("[MyLink]: http://company.com\nThis is a [link][MyLink]",
            "This is a [link][MyLink]\n\n[MyLink]: http://company.com");

        AssertNormalizeNoTrim("This is a [link][MyLink] a normal link [link](http://google.com) and another def link [link2][MyLink2]\n\n[MyLink]: http://company.com\n[MyLink2]: http://company2.com");

        // Collapsed link
        AssertNormalizeNoTrim("This is a [link][]\n\n[link]: http://company.com");

        // Shortcut link
        AssertNormalizeNoTrim("This is a [link]\n\n[link]: http://company.com");
    }

    [Fact]
    public void EscapeInline()
    {
        AssertNormalizeNoTrim("This is an escape \\* with another \\[");
    }

    [Fact]
    public void HtmlEntityInline()
    {
        AssertNormalizeNoTrim("This is a &auml; blank");
    }

    [Fact]
    public void HtmlInline()
    {
        AssertNormalizeNoTrim("foo <hr/> bar");
        AssertNormalizeNoTrim(@"foo <hr foo=""bar""/> bar");
    }


    [Fact]
    public void SpaceBetweenNodes()
    {
        AssertNormalizeNoTrim("# Hello World\nFoobar is a better bar.",
                              "# Hello World\n\nFoobar is a better bar.");
    }

    [Fact]
    public void SpaceBetweenNodesEvenForHeadlines()
    {
        AssertNormalizeNoTrim("# Hello World\n## Chapter 1\nFoobar is a better bar.",
                              "# Hello World\n\n## Chapter 1\n\nFoobar is a better bar.");
    }

    [Fact]
    public void SpaceRemoveAtStartAndEnd()
    {
        AssertNormalizeNoTrim("\n\n# Hello World\n## Chapter 1\nFoobar is a better bar.\n\n",
                              "# Hello World\n\n## Chapter 1\n\nFoobar is a better bar.");
    }

    [Fact]
    public void SpaceShortenBetweenNodes()
    {
        AssertNormalizeNoTrim("# Hello World\n\n\n\nFoobar is a better bar.",
                              "# Hello World\n\nFoobar is a better bar.");
    }

    [Fact]
    public void BiggerSample()
    {
        var input = @"# Heading 1

This is a paragraph

This is another paragraph

- This is a list item 1
- This is a list item 2
- This is a list item 3

```C#
This is a code block
```

> This is a quote block

    This is an indented code block
    line 2 of indented

This is a last line";
        AssertNormalizeNoTrim(input);
    }

    [Fact]
    public void TaskLists()
    {
        AssertNormalizeNoTrim("- [X] This is done");
        AssertNormalizeNoTrim("- [x] This is done",
                              "- [X] This is done");
        AssertNormalizeNoTrim("- [ ] This is not done");

        // ignore
        AssertNormalizeNoTrim("[x] This is not a task list");
        AssertNormalizeNoTrim("[ ] This is not a task list");
    }

    [Fact]
    public void JiraLinks()
    {
        AssertNormalizeNoTrim("FOO-1234");
        AssertNormalizeNoTrim("AB-1");

        AssertNormalizeNoTrim("**Hello World AB-1**");
    }

    [Fact]
    public void AutoLinks()
    {
        AssertNormalizeNoTrim("Hello from http://example.com/foo", "Hello from [http://example.com/foo](http://example.com/foo)", new NormalizeOptions() { ExpandAutoLinks = true, });
        AssertNormalizeNoTrim("Hello from www.example.com/foo", "Hello from [www.example.com/foo](http://www.example.com/foo)", new NormalizeOptions() { ExpandAutoLinks = true, });
        AssertNormalizeNoTrim("Hello from ftp://example.com", "Hello from [ftp://example.com](ftp://example.com)", new NormalizeOptions() { ExpandAutoLinks = true, });
        AssertNormalizeNoTrim("Hello from mailto:hello@example.com", "Hello from [hello@example.com](mailto:hello@example.com)", new NormalizeOptions() { ExpandAutoLinks = true, });

        AssertNormalizeNoTrim("Hello from http://example.com/foo", "Hello from http://example.com/foo", new NormalizeOptions() { ExpandAutoLinks = false, });
        AssertNormalizeNoTrim("Hello from www.example.com/foo", "Hello from http://www.example.com/foo", new NormalizeOptions() { ExpandAutoLinks = false, });
        AssertNormalizeNoTrim("Hello from mailto:hello@example.com", "Hello from mailto:hello@example.com", new NormalizeOptions() { ExpandAutoLinks = false, });
    }

    [Fact]
    public void PipeTables()
    {
        AssertNormalizeNoTrim(@"Foo | Bar
--- | ---
Hello | *World*",            @"| Foo | Bar |
| --- | --- |
| Hello | *World* |");
        AssertNormalizeNoTrim(@"
Foo | Bar
:---: | ---:
Hello | *World*",             @"| Foo | Bar |
| :---: | ---: |
| Hello | *World* |");
        AssertNormalizeNoTrim(@"| Foo |
| --- |
| Hello World |");
        AssertNormalizeNoTrim(@"| Foo | Bar |
| --- | --- |
| Hello World | *World* |");
        AssertNormalizeNoTrim(@"| Foo | Bar |
| :--- | ---: |
| Hello World | *World* |");
    }

    [Fact]
    public void PipeTablesFollowedByText()
    {
        AssertNormalizeNoTrim(@"| Foo |
| --- |
| Hello World |

Text following the table.");
    }

    [Theory]
    [InlineData("| A |\n| :--- |\n| x |")]
    [InlineData("| A | B |\n| :--- | :--- |\n| x | y |")]
    [InlineData("| A | B |\n| --- | :--- |\n| x | y |")]
    public void PipeTableAlignmentSurvivesNormalization(string markdown)
    {
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();
        var normalized = MarkdownConverter.Normalize(markdown, pipeline: pipeline);
        Assert.Equal(markdown, normalized);
        Assert.Equal(MarkdownConverter.ToHtml(markdown, pipeline), MarkdownConverter.ToHtml(normalized, pipeline));
    }

    [Theory]
    [InlineData("---")]
    [InlineData(":---")]
    [InlineData("---:")]
    public void PipeTableSeparatorMatchesExpandedHeader(string separator)
    {
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();
        var markdown = $"| A |\n| {separator} |\n| x | y |";
        var normalized = MarkdownConverter.Normalize(markdown, pipeline: pipeline);
        Assert.Equal($"| A |  |\n| {separator} | {separator} |\n| x | y |", normalized);
        Assert.Equal(MarkdownConverter.ToHtml(markdown, pipeline), MarkdownConverter.ToHtml(normalized, pipeline));
        Assert.Equal(normalized, MarkdownConverter.Normalize(normalized, pipeline: pipeline));
    }

    [Theory]
    [InlineData("---", "---------")]
    [InlineData(":---", "-------:")]
    [InlineData(":-----:", ":-------")]
    [InlineData("", "---")]
    [InlineData("---", "")]
    public void PipeTableInferredWidthsSurviveNormalization(string first, string second)
    {
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables(new PipeTableOptions
        {
            InferColumnWidthsFromSeparator = true
        }).Build();
        var markdown = $"| A | B |\n| {first} | {second} |\n| x | y |";
        var normalized = MarkdownConverter.Normalize(markdown, pipeline: pipeline);
        Assert.Equal(markdown, normalized);
        Assert.Equal(MarkdownConverter.ToHtml(markdown, pipeline), MarkdownConverter.ToHtml(normalized, pipeline));
        Assert.Equal(normalized, MarkdownConverter.Normalize(normalized, pipeline: pipeline));
    }

    [Fact]
    public void PipeTableWithoutWidthInferenceUsesStandardSeparators()
    {
        AssertNormalizeNoTrim("| A | B |\n| --- | --------- |\n| x | y |",
            "| A | B |\n| --- | --- |\n| x | y |");
        AssertNormalizeNoTrim("| A | B |\n| --- | |\n| x | y |",
            "| A | B |\n| --- | --- |\n| x | y |");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyMiddleSeparatorPreservesInferredWidths(bool headerOnly)
    {
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables(new PipeTableOptions
        {
            InferColumnWidthsFromSeparator = true
        }).Build();
        var markdown = "| A | B | C |\n| --- |  | --- |" + (headerOnly ? "" : "\n| x | y | z |");
        var normalized = MarkdownConverter.Normalize(markdown, pipeline: pipeline);
        Assert.Equal(markdown, normalized);
        var table = (Table)MarkdownConverter.Parse(normalized, pipeline)[0];
        Assert.Equal(new[] { 50f, 0f, 50f }, table.ColumnDefinitions.Select(column => column.Width).ToArray());
        Assert.Equal(MarkdownConverter.ToHtml(markdown, pipeline), MarkdownConverter.ToHtml(normalized, pipeline));
        Assert.Equal(normalized, MarkdownConverter.Normalize(normalized, pipeline: pipeline));
    }

    [Fact]
    public void ChangingInferredWidthDiscardsOriginalSeparatorCount()
    {
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables(new PipeTableOptions
        {
            InferColumnWidthsFromSeparator = true
        }).Build();
        var table = (Table)MarkdownConverter.Parse("| A | B |\n| --- | --------- |", pipeline)[0];
        var column = table.ColumnDefinitions[1];
        Assert.Equal(9, column.SeparatorDashCount);
        column.Width = column.Width;
        Assert.Equal(9, column.SeparatorDashCount);
        column.Width = 0;
        Assert.Null(column.SeparatorDashCount);
    }

    [Theory]
    [InlineData("```\n<img src=x onerror=alert(1)>", "```\n<img src=x onerror=alert(1)>\n```")]
    [InlineData("1. ```\n   [x](javascript:alert(1))", "1. ```\n   [x](javascript:alert(1))\n   ```")]
    [InlineData("> ~~~\n> *a*\n\n*b*", "> ~~~\n> *a*\n> ~~~\n\n*b*")]
    public void FencedCodeBlockIsAlwaysClosed(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Fact]
    public void FencedCodeBlockFenceIsLongerThanItsContent()
    {
        AssertSyntax("`````\n```\n  ```` \n`````", new FencedCodeBlock(null!)
        {
            FencedChar = '`',
            Lines = new StringLineGroup(4)
            {
                new StringSlice("```"),
                new StringSlice("  ```` "),
            }
        });
    }

    [Theory]
    [InlineData("``  a  ``", "`  a  `")]
    [InlineData("`` `a ``", "`` `a ``")]
    [InlineData("`` a` ``", "`` a` ``")]
    [InlineData("`a``a``", "`a``a``")]
    public void CodeInlinePaddingPreservesContent(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Theory]
    [InlineData("a\n\t-", "a\n    -")]
    [InlineData(">a\n=", "> a\n>     =")]
    [InlineData("- a\n--", "- a\n      --")]
    [InlineData("a\n    >", "a\n    >")]
    [InlineData("a\n\t<div", "a\n    <div")]
    [InlineData("]\n\t#", "]\n    #")]
    [InlineData("a\n    1. b\n    2. c", "a\n    1. b\n2. c")]
    [InlineData("a\n    ```", "a\n    ```")]
    public void ParagraphContinuationLineDoesNotStartBlock(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Fact]
    public void ParagraphContinuationLineUsesThePipelineBlockParsers()
    {
        AssertNormalizePreservesHtml("a\n    b. c", "a\nb. c");
        AssertNormalizePreservesHtml("a\n    b. c", "a\n    b. c", new MarkdownPipelineBuilder().UseListExtras().Build());
        AssertNormalizePreservesHtml("a\n    :   b", "a\n    :   b", new MarkdownPipelineBuilder().UseDefinitionLists().Build());
    }

    [Theory]
    [InlineData("*\n  ***", "* ___\n")]
    [InlineData("-\n  ---", "- ___\n")]
    [InlineData("-\n  --", "- \\--")]
    [InlineData("-\n  -\n    --", "- - \\--")]
    public void ListItemFirstLineDoesNotMergeWithTheMarker(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Theory]
    [InlineData("a\\ \nb", "a\\\\\nb")]
    [InlineData("\\ \nb", "\\\\\nb")]
    public void LiteralBackslashBeforeLineBreakIsEscaped(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Theory]
    [InlineData("<div>\n\n*text*", "<div>\n\n*text*")]
    [InlineData("<div\n\na", "<div\n\na")]
    [InlineData("> <p>\n>\n> *a*", "> <p>\n> \n> *a*")]
    [InlineData("- <!--\na", "- <!--\na")]
    [InlineData("+ <?\na", "+ <?\na")]
    public void HtmlBlockKeepsItsEnd(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Theory]
    [InlineData("* >\n  x", "* > \n  x")]
    [InlineData("- # a\n  b", "- # a\n  b")]
    [InlineData("-     a\n  b", "-     a\n  b")]
    [InlineData("- ***\n  a", "- ***\n  a")]
    [InlineData("- ```\n  x\n  ```\n  y", "- ```\n  x\n  ```\n  y")]
    [InlineData("- > a\n  >\n  > b", "- > a\n  > \n  > b")]
    [InlineData("- > a\n  >\n  b", "- > a\n  > \n  b")]
    public void TightListItemBlocksAreNotSeparatedByBlankLines(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Theory]
    [InlineData("- a\n  - - -", "- a\n  ***\n")]
    [InlineData("- a\n  - - -\n  b", "- a\n  ***\n  b")]
    public void ThematicBreakAfterParagraphIsNotSetextUnderline(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Fact]
    public void LinkWithEmptyDestinationKeepsIt()
    {
        AssertNormalizePreservesHtml("[]()", "[](<>)");

        // A reference to a heading has no URL until the document is rendered
        AssertNormalizePreservesHtml("# a\n\n[a]", "# a\n\n[a]", new MarkdownPipelineBuilder().UseAutoIdentifiers().Build());
    }

    [Theory]
    [InlineData("[a](<b c>)", "[a](<b c>)")]
    [InlineData("[a](<b)c>)", "[a](<b)c>)")]
    [InlineData("[a](b \"c\\\\\")", "[a](b \"c\\\\\")")]
    [InlineData("[a](&amp;lt;)", "[a](&amp;lt;)")]
    [InlineData("[a]\n\n[a]: <>", "[a]\n\n[a]: <>")]
    [InlineData("[a]\n\n[a]: <b c> \"t\"", "[a]\n\n[a]: <b c> \"t\"")]
    public void LinkDestinationAndTitleAreEscaped(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Theory]
    [InlineData("[a]: b\n\nc", "[a]: b\n\nc")]
    [InlineData("[a]: b\n\n    c", "[a]: b\n\n    c")]
    [InlineData("[a]: b\n\n-", "[a]: b\n\n- ")]
    public void LinkReferenceDefinitionsAreFollowedByBlankLine(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Fact]
    public void AutoIdentifiersDoNotEmitGeneratedLinkReferenceDefinitionsNextToOtherDefinitions()
    {
        AssertNormalizePreservesHtml("[b]: /u\n\n# a\n\n[a] [b]", "[b]: /u\n\n# a\n\n[a] [b]", new MarkdownPipelineBuilder().UseAutoIdentifiers().Build());
    }

    [Theory]
    [InlineData("a\nb\n=", "a\nb\n===")]
    [InlineData("a\nb\n---", "a\nb\n---")]
    [InlineData("> a\n> b\n> -", "> a\n> b\n> ---")]
    [InlineData("a\n    # b\n=", "a\n    # b\n===")]
    public void MultilineHeadingIsSetextHeading(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Fact]
    public void MultilineHeadingWithCarriageReturnsIsSetextHeading()
    {
        AssertNormalizePreservesHtml("a\ra\r-", "a\ra\n---");
    }

    [Theory]
    [InlineData("# # #", "# # #")]
    [InlineData("a #\n-", "## a # #")]
    public void HeadingContentEndingWithNumberSignHasClosingSequence(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Theory]
    [InlineData("  1. a\n\n    b", "1.   a\n\n    b")]
    [InlineData("   - a\n\n    b", "-    a\n\n    b")]
    [InlineData("- a\n-  b\n  <div", "- a\n-    b\n\n  <div\n")]
    public void IndentedBlockAfterListIsNotPartOfTheLastItem(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Theory]
    [InlineData("+\n    <a>", "+ \n    <a>\n")]
    [InlineData("- a\n-\n    <div>", "- a\n- \n    <div>\n")]
    public void IndentedHtmlBlockStartingListItemKeepsItsIndentation(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Theory]
    [InlineData("```&#9;\n```", "```&#9;\n```")]
    [InlineData("~~~ ~\n~~~", "~~~ ~\n~~~")]
    [InlineData("``` &amp;lt;\n```", "```&amp;lt;\n```")]
    [InlineData("``` a&#32;b\n```", "```a&#32;b\n```")]
    public void FencedCodeBlockInfoIsEscaped(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Theory]
    [InlineData("Docs: https://docs.example/x](https://evil.example) end", "Docs: [https://docs.example/x\\](https://evil.example)](https://docs.example/x](https://evil.example)) end")]
    [InlineData("Docs: https://docs.example/x](javascript:alert(1)) end", "Docs: [https://docs.example/x\\](javascript:alert(1))](https://docs.example/x](javascript:alert(1))) end")]
    [InlineData("www.a.b/*c*d", "[www.a.b/\\*c\\*d](http://www.a.b/*c*d)")]
    [InlineData("` http://a.b/``", "` [http://a.b/&#96;&#96;](http://a.b/``)")]
    public void ExpandedAutoLinkTextIsEscaped(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected, new MarkdownPipelineBuilder().UseAutoLinks().Build());
    }

    [Fact]
    public void ExpandedAutoLinkTextIsEscapedForThePipelineInlines()
    {
        AssertNormalizePreservesHtml("www.a.b/$", "[www\\.a\\.b\\/\\$](http://www.a.b/$)", new MarkdownPipelineBuilder().UseAutoLinks().UseMathematics().Build());
    }

    [Theory]
    [InlineData("> [!NOTE]\n> text", "> [!NOTE]\n> text")]
    [InlineData("> [!warning]  \n> a\n>\n> - b", "> [!warning]\n> a\n> \n> - b")]
    public void AlertKeepsItsKind(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected, new MarkdownPipelineBuilder().UseAlertBlocks().Build());
    }

    [Theory]
    [InlineData("   1. a\n\n     b", "1.    a\n\n     b")]
    [InlineData("   *     a\n    b", "   *     a\n\n    b")]
    [InlineData("   - a\n   -     b\n    c", "   - a\n   -     b\n\n    c")]
    public void IndentedBlockAfterListIsNotPartOfTheLastItemWhateverItsIndentation(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Theory]
    [InlineData("+ <textarea\n\n+", "+ <textarea\n  \n+ ")]
    [InlineData("- <!--\n\n- a", "- <!--\n  \n- a")]
    public void OpenHtmlBlockInLooseListIsNotFollowedByAnotherBlankLine(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Theory]
    [InlineData("-\n\n\n-", "- \n\n* ")]
    [InlineData("1.\n\n\n2.\n\n\n3.", "1. \n\n2) \n\n3. ")]
    public void AdjacentListsAreNotMerged(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Fact]
    public void ParagraphContinuationLineIsNotConsumedByBlockParser()
    {
        AssertNormalizePreservesHtml("a\n    *[b]: c", "a\n    *[b]: c", new MarkdownPipelineBuilder().UseAbbreviations().Build());
    }

    [Theory]
    [InlineData("a*http://a.b*", "a*http://a.b*")]
    [InlineData("__aa_http://a.b", "__aa_http://a.b")]
    public void AutoLinkNextToDelimiterIsNotExpanded(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected, new MarkdownPipelineBuilder().UseAutoLinks().Build());
    }

    [Fact]
    public void ParagraphStartingWithHtmlTagContinuesLinkReferenceDefinitions()
    {
        // The paragraph cannot start on its own line, so it stays after the definitions, as in the source
        var markdown = "- [x]: /a\n\n[y]: /b\n<b>";
        var normalized = MarkdownConverter.Normalize(markdown);
        Assert.Equal("- \n\n[x]: /a\n[y]: /b\n<b>", normalized);
        Assert.Equal(MarkdownConverter.ToHtml(markdown), MarkdownConverter.ToHtml(normalized));
    }

    [Theory]
    [InlineData("a. x\nb. y", "a. x\nb. y")]
    [InlineData("B) x\nC) y", "B) x\nC) y")]
    [InlineData("iv. x\nv. y", "iv. x\nv. y")]
    [InlineData("I. x\nII. y", "I. x\nII. y")]
    public void ListExtrasKeepTheirMarkers(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected, new MarkdownPipelineBuilder().UseListExtras().Build());
    }

    [Theory]
    [InlineData("[Foo*bar\\]]:my_(url) 'title (with parens)'\n\n[Foo*bar\\]]", "[Foo*bar\\]]: my_(url) \"title (with parens)\"\n\n[Foo*bar\\]][Foo*bar\\]]")]
    [InlineData("[foo][ref\\[]\n\n[ref\\[]: /uri", "[foo][ref\\[]\n\n[ref\\[]: /uri")]
    [InlineData("[bar\\\\]: /uri\n\n[bar\\\\]", "[bar\\\\]: /uri\n\n[bar\\\\][bar\\\\]")]
    public void LinkLabelIsEscaped(string markdown, string expected)
    {
        AssertNormalizePreservesHtml(markdown, expected);
    }

    [Fact]
    public void CodeInlineNextToAutoLinkWithBacktickIsIdempotent()
    {
        AssertNormalizePreservesHtml("www.a.b/` ``a``", "[www.a.b/&#96;](http://www.a.b/`) `a`", new MarkdownPipelineBuilder().UseAutoLinks().Build());
    }

    private static void AssertNormalizePreservesHtml(string markdown, string expected, MarkdownPipeline? pipeline = null)
    {
        pipeline ??= new MarkdownPipelineBuilder().Build();
        var normalized = MarkdownConverter.Normalize(markdown, pipeline: pipeline);
        Assert.Equal(expected, normalized);
        Assert.Equal(MarkdownConverter.ToHtml(markdown, pipeline), MarkdownConverter.ToHtml(normalized, pipeline));
        Assert.Equal(normalized, MarkdownConverter.Normalize(normalized, pipeline: pipeline));
    }

    private static void AssertSyntax(string expected, MarkdownObject syntax)
    {
        var writer = new StringWriter();
        var normalizer = new NormalizeRenderer(writer);
        var document = new MarkdownDocument();
        if (syntax is Block)
        {
            document.Add((Block)syntax);
        }
        else
        {
            throw new InvalidOperationException();
        }
        normalizer.Render(document);

        var actual = writer.ToString();

        Assert.Equal(expected, actual);
    }

    internal static void TestSpec(string inputText, string expectedOutputText, string? extensions = null, string? context = null)
    {
        foreach (var pipeline in TestParser.GetPipeline(extensions))
        {
            AssertNormalize(inputText, expectedOutputText, trim: false, pipeline: pipeline.Value, context: context);
        }
    }

    internal static void AssertNormalizeNoTrim(string input, string? expected = null, NormalizeOptions? options = null)
        => AssertNormalize(input, expected, false, options);

    internal static void AssertNormalize(string input, string? expected = null, bool trim = true, NormalizeOptions? options = null, MarkdownPipeline? pipeline = null, string? context = null)
    {
        expected = expected ?? input;
        input = NormText(input, trim);
        expected = NormText(expected, trim);

        pipeline = pipeline ?? new MarkdownPipelineBuilder()
            .UseAutoLinks()
            .UseJiraLinks(new Extensions.JiraLinks.JiraLinkOptions("https://jira.example.com"))
            .UseTaskLists()
            .UsePipeTables()
            .Build();

        var result = MarkdownConverter.Normalize(input, options, pipeline: pipeline);
        result = NormText(result, trim);

        TestParser.PrintAssertExpected(input, result, expected, context);
    }

    private static string NormText(string text, bool trim)
    {
        if (trim)
        {
            text = text.Trim();
        }
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
}
