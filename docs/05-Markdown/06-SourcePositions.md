---
title: Source positions
description: Map each block and inline back to its line, column and character range in the source, and synchronize an editor with a preview.
---

## Positions of blocks

Every element of the [syntax tree](05-SyntaxTree.md) has a position in the source text:

- `Line` and `Column` are zero-based: the first character of the document is at line 0, column 0.
- `Span` is a `SourceSpan`: the offset of its first character (`Start`) and of its last character (`End`) in the
  source text. Both are included, so `Length` is `End - Start + 1`.

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestSourcePosition.cs;region=MarkdownObject_BlockPositions;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = "# Title\n\nSome *text* here.";
var document = MarkdownConverter.Parse(markdown);

var paragraph = document.Descendants<ParagraphBlock>().Single();
Assert.Equal(2, paragraph.Line);   // Zero-based: the third line
Assert.Equal(0, paragraph.Column);
Assert.Equal(new SourceSpan(9, 25), paragraph.Span); // Start and End are both included
Assert.Equal("Some *text* here.", markdown.Substring(paragraph.Span.Start, paragraph.Span.Length));
```
<!-- endref -->

The offsets are in characters of the string you parsed. `ToPositionText()` formats the position of an element as
`line, column, start-end`, for error messages and logs.

## Positions of inlines

By default, the parser computes the position of the blocks only: the inlines have a `Span` of `0-0`. Enable
`UsePreciseSourceLocation` when you need the position of the inlines, for instance to highlight a link in an editor:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestSourcePosition.cs;region=MarkdownPipelineBuilder_UsePreciseSourceLocation;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = "# Title\n\nSome *text* here.";

// Without precise source location, inlines have no position
var emphasis = MarkdownConverter.Parse(markdown).Descendants<EmphasisInline>().Single();
Assert.Equal(new SourceSpan(0, 0), emphasis.Span);

var pipeline = new MarkdownPipelineBuilder()
    .UsePreciseSourceLocation()
    .Build();
emphasis = MarkdownConverter.Parse(markdown, pipeline).Descendants<EmphasisInline>().Single();
Assert.Equal(new SourceSpan(14, 19), emphasis.Span);
Assert.Equal("*text*", markdown.Substring(emphasis.Span.Start, emphasis.Span.Length));
Assert.Equal(2, emphasis.Line);
Assert.Equal(5, emphasis.Column);
```
<!-- endref -->

Computing these positions makes parsing slower: enable it only when you use them. `UsePipeTables` enables it too,
because the parser of tables needs the positions.

## Find the element at a position

`FindBlockAtPosition` returns the innermost block that contains an offset of the source, for instance the position of
the caret in an editor:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestSourcePosition.cs;region=BlockExtensions_FindBlockAtPosition;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = "# Title\n\n- First\n- Second\n";
var document = MarkdownConverter.Parse(markdown);

// For instance, the position of the caret in an editor
var caret = markdown.IndexOf("Second", StringComparison.Ordinal);

// The innermost block that contains the position
var block = document.FindBlockAtPosition(caret);
Assert.IsType<ParagraphBlock>(block);
Assert.IsType<ListItemBlock>(block!.Parent);
```
<!-- endref -->

On a blank line between two blocks, no child contains the offset: it returns the container, here the document. It
returns `null` only when the offset is outside the block you search. `ContainsPosition` and `CompareToPosition` test the
position of one block, and `FindClosestBlock` finds the block closest to a line.

## Synchronize an editor and a preview

`UsePragmaLines` gives the outermost block that starts on each line an `id` with its line number, `pragma-line-<line>`,
so that a preview can scroll to the element that the editor shows:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestPragmaLines.cs;region=MarkdownExtensions_UsePragmaLines;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UsePragmaLines()
    .Build();

var html = MarkdownConverter.ToHtml("# Title\n\nSome text.\n\n- First\n- Second", pipeline);
Assert.Equal("<h1 id=\"pragma-line-0\">Title</h1>\n<p id=\"pragma-line-2\">Some text.</p>\n<ul id=\"pragma-line-4\">\n<li>First</li>\n<li id=\"pragma-line-5\">Second</li>\n</ul>\n", html);
```
<!-- endref -->

The line of the editor is not always the first line of a block. `FindClosestLine` returns the closest line that has a
`pragma-line` identifier:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestPragmaLines.cs;region=BlockExtensions_FindClosestLine;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder().UsePragmaLines().Build();
var document = MarkdownConverter.Parse("# Title\n\nSome text.\n\n- First\n- Second", pipeline);

// The editor shows line 3 (zero-based), a blank line: scroll the preview to the closest element
var line = document.FindClosestLine(3);
Assert.Equal(4, line); // The element with the id "pragma-line-4"
```
<!-- endref -->

A block that already has an identifier, from [`UseAutoIdentifiers`](04-Extensions/03-HeadingsAndLinks.md#heading-identifiers)
or an [attribute](04-Extensions/06-GenericAttributes.md), keeps it: the pragma identifier goes to an empty
`<a id="pragma-line-<line>"></a>`, at the start of the heading or before the block.
