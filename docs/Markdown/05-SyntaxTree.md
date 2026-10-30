---
title: The syntax tree
description: Parse a document into blocks and inlines, find elements, walk the tree and attach your own data to its elements.
---

## Parse a document

`MarkdownConverter.Parse` returns a `MarkdownDocument`, the root of the syntax tree. Pass the pipeline you render with,
so that the tree contains the elements of its extensions. `Descendants<T>()` finds the elements of a type anywhere in the
tree:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_SyntaxTree;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

`Descendants()` returns all the blocks and inlines, in document order.

## Blocks and inlines

The tree has two kinds of elements, both derived from `MarkdownObject`:

- **Blocks** (`Block`, namespace `Meziantou.Framework.Markdown.Syntax`) are the structure of the document. A
  `ContainerBlock`, such as a list or a quote, contains other blocks. A `LeafBlock`, such as a paragraph or a heading,
  contains text: its `Inline` property holds the inlines of that text.
- **Inlines** (`Inline`, namespace `Meziantou.Framework.Markdown.Syntax.Inlines`) are the content of the text. A
  `ContainerInline`, such as an emphasis or a link, contains other inlines. A `LeafInline`, such as a literal or a code
  span, does not.

| Markdown | Element | Useful members |
| --- | --- | --- |
| The document | `MarkdownDocument` | `LineCount`, the blocks |
| `# Title`, or a line underlined with `===` | `HeadingBlock` | `Level`, `IsSetext` |
| A paragraph | `ParagraphBlock` | `Inline` |
| `- item`, `1. item` | `ListBlock` of `ListItemBlock` | `IsOrdered`, `BulletType`, `OrderedStart`, `IsLoose` |
| `> quote` | `QuoteBlock` | The blocks of the quote |
| A fenced code block | `FencedCodeBlock` | `Info` (the language), `Arguments`, `Lines` |
| An indented code block | `CodeBlock` | `Lines` |
| `<div>` | `HtmlBlock` | `Type`, `Lines` |
| `***`, `---` | `ThematicBreakBlock` | |
| `[label]: url` | `LinkReferenceDefinition` | `Label`, `Url`, `Title` |
| Text | `LiteralInline` | `Content`; `ToString()` returns the text |
| `*a*`, `**a**` | `EmphasisInline` | `DelimiterChar`, `DelimiterCount` (2 for strong) |
| `[a](url)`, `![a](url)` | `LinkInline` | `Url`, `Title`, `IsImage`, `Label` |
| `<https://…>` | `AutolinkInline` | `Url`, `IsEmail` |
| `` `code` `` | `CodeInline` | `Content` |
| `<b>` | `HtmlInline` | `Tag` |
| `&amp;` | `HtmlEntityInline` | `Original`, `Transcoded` |
| A line break | `LineBreakInline` | `IsHard` |

Each extension adds its own elements, in its namespace: `Table` for tables, `Footnote`, `TaskList`, `AlertBlock`,
`YamlFrontMatterBlock`… See [Read a table from the tree](04-Extensions/02-Tables.md#read-a-table-from-the-tree).

## Walk the tree

A `ContainerBlock` is a list of its child blocks: index it, count it or enumerate it. Every block has a `Parent`.
Inlines are linked: a `ContainerInline` has a `FirstChild` and a `LastChild`, and each inline has a `Parent`, a
`PreviousSibling` and a `NextSibling`:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=ContainerBlock_WalkChildren;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

## Get the text of an element

The text of a heading or a paragraph is split into inlines. To get it without the markup, concatenate its
`LiteralInline` elements. For instance, to build a table of contents:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownDocument_TableOfContents;format=md-fenced-code-block;dedent=true -->
```csharp
var document = MarkdownConverter.Parse("# Guide\n\n## Install *the* package\n\nText\n\n## Use it");

var outline = new List<string>();
foreach (var heading in document.Descendants<HeadingBlock>())
{
    // The text of a heading is in its inlines: concatenate the literals to drop the markup
    var text = string.Concat(heading.Inline!.Descendants<LiteralInline>().Select(literal => literal.ToString()));
    outline.Add(new string(' ', (heading.Level - 1) * 2) + text);
}

Assert.Equal("Guide\n  Install the package\n  Use it", string.Join('\n', outline));
```
<!-- endref -->

The text of code spans (`CodeInline`) and of the other leaf inlines is in their own properties. To render an element as
plain text, see [Plain text and other formats](08-PlainTextAndOtherFormats.md).

## Attach data to elements

`SetData` and `GetData` attach your own values to any element of the tree, for instance the results of an analysis. A
`DataKey<T>` gives typed access:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestMarkdownObjectDataExtensions.cs;region=MarkdownObjectDataExtensions_TypedKey;format=md-fenced-code-block;dedent=true -->
```csharp
var sectionNumberKey = new DataKey<int>();

var document = MarkdownConverter.Parse("# Install\n\nText\n\n# Configure");
var number = 0;
foreach (var heading in document.Descendants<HeadingBlock>())
{
    number++;
    heading.SetData(sectionNumberKey, number);
}

var configure = document.Descendants<HeadingBlock>().Last();
Assert.Equal(2, configure.GetData(sectionNumberKey));
Assert.False(document[1].TryGetData(sectionNumberKey, out _)); // Nothing on the paragraph
```
<!-- endref -->

`SetData(key, value)` and `GetData(key)` also accept any object as the key. `TryGetData` tells whether a value is set,
and `RemoveData` removes it. The renderers do not write your data.

## Debug the tree

`DumpTo` writes the inlines of an element, one per line, indented by their depth:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=Inline_DumpTo;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

To change the tree, see [Editing documents](07-EditingDocuments.md). To know where each element is in the source, see
[Source positions](06-SourcePositions.md).
