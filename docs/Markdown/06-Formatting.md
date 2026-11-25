---
title: Formatting
description: Write a document in a consistent style, without changing its HTML.
---

## Format a document

[MarkdownFormatter.Format](cref:Meziantou.Framework.Markdown.MarkdownFormatter.Format(Meziantou.Framework.Markdown.MarkdownSyntaxTree))
writes a document in a consistent style, and the formatted document renders as the same HTML:

- one blank line between the blocks of the document, of a block quote and of a loose list, and none in a tight list,
- the marker of a block quote on each of its lines, and the content of a list item indented by the width of its marker,
- `-` for the items of the bullet lists, and the items of an ordered list numbered from the first one,
- ATX headings (`# Title`), without closing sequence,
- fenced code blocks, with a fence longer than the backticks of the code,
- `---` for the thematic breaks,
- the cells of the tables padded so that their pipes are aligned in a monospace editor: a wide character, such as `漢`
  or an emoji, uses two columns, and a combining mark or a zero-width space none.

The text of the paragraphs, the headings and the cells, the code and the HTML are kept: the formatter does not change the
inlines. The lines of a paragraph lose their indentation, unless it is what makes them lines of the paragraph, such as
`    # text` after a line of text, which would be a heading without it.

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownFormatter_Usage;format=md-fenced-code-block;dedent=true -->
````csharp
var tree = MarkdownSyntaxTree.ParseText("""
    Title
    =====
    * one
    *   two

    3) a
    7) b

    Code:

        var x = 1;

    > quote
    continued

    | a | b |
    |-|:-:|
    | long cell | x |
    """, MarkdownParseOptions.GitHubFlavored);

var formatted = MarkdownFormatter.Format(tree);

Assert.Equal("""
    # Title

    - one
    - two

    3) a
    4) b

    Code:

    ```
    var x = 1;
    ```

    > quote
    > continued

    | a         | b   |
    | --------- | :-: |
    | long cell | x   |

    """, formatted.GetRoot().ToFullString());
Assert.Equal(MarkdownHtmlRenderer.ToHtml(tree), MarkdownHtmlRenderer.ToHtml(formatted));
````
<!-- endref -->

[MarkdownFormattingOptions](cref:Meziantou.Framework.Markdown.MarkdownFormattingOptions) chooses the style:
the line ending, the marker of the bullet lists, or `null` to keep it, whether to renumber the ordered lists, to write the
setext headings and the indented code blocks as they are, the characters of the code fences and of the thematic breaks,
and whether to align the tables.
[FormatText](cref:Meziantou.Framework.Markdown.MarkdownFormatter.FormatText(Meziantou.Framework.Markdown.MarkdownSyntaxTree,Meziantou.Framework.Markdown.MarkdownFormattingOptions))
returns the text instead of its tree.

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownFormattingOptions_Usage;format=md-fenced-code-block;dedent=true -->
````csharp
var tree = MarkdownSyntaxTree.ParseText("Title\n=====\n\n- a\n- b\n\n```cs\nvar x = 1;\n```\n\n***\n");
var options = new MarkdownFormattingOptions
{
    NewLine = "\r\n",
    BulletListMarker = '*',
    UseAtxHeadings = false,
    UseFencedCodeBlocks = false,
    CodeFenceCharacter = '~',
    ThematicBreakCharacter = '_',
};

var text = MarkdownFormatter.FormatText(tree, options);

Assert.Equal("Title\r\n===\r\n\r\n* a\r\n* b\r\n\r\n~~~cs\r\nvar x = 1;\r\n~~~\r\n\r\n___\r\n", text);
````
<!-- endref -->

The formatter keeps what the HTML depends on: a list keeps its blank lines, as they make it loose; two lists that follow
each other get different markers, as they would be one list otherwise; a thematic break after a paragraph is `***`, as
`---` would make the paragraph a heading; and a setext heading of several lines stays a setext heading. The extensions add
their own cases: the pipe tables, the grid tables and the definition lists keep the blank lines around them,
and a table whose cells have attributes, or a pipe that is text, is not aligned. A table that follows a block without a
blank line stays a table after it: when its aligned rows would be lazy lines of the block, such as a list, or make a grid
table of a paragraph that starts with `+`, its rows are written as they are in the text, or a blank line is added before
it.

The formatter checks that the formatted text renders as the same HTML. When it does not, which can happen with rare
combinations of the extensions, such as a lazy line before a table in a list item, it returns the text as it is. The
check includes the content of the footnotes that are not referred to, which the HTML does not show.

The `MarkdownConverter.Normalize` method of `Meziantou.Framework.Markdown` 1.x has other options: the formatter has no
option for the blank lines, and does not change the autolinks into links.

