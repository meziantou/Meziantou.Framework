---
title: The syntax tree
description: Parse a document into blocks and inlines, find elements, walk the tree and attach your own data to its elements.
---

## Parse a document

[MarkdownConverter.Parse](cref:Meziantou.Framework.Markdown.MarkdownConverter.Parse) returns a [MarkdownDocument](cref:Meziantou.Framework.Markdown.Syntax.MarkdownDocument), the root of the syntax tree. Pass the pipeline you render with,
so that the tree contains the elements of its extensions. [Descendants\<T\>()](cref:Meziantou.Framework.Markdown.Syntax.MarkdownObjectExtensions.Descendants) finds the elements of a type anywhere in the
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

[Descendants()](cref:Meziantou.Framework.Markdown.Syntax.MarkdownObjectExtensions.Descendants) returns all the blocks and inlines, in document order.

## Blocks and inlines

The tree has two kinds of elements, both derived from [MarkdownObject](cref:Meziantou.Framework.Markdown.Syntax.MarkdownObject):

- **Blocks** ([Block](cref:Meziantou.Framework.Markdown.Syntax.Block), namespace [Meziantou.Framework.Markdown.Syntax](cref:Meziantou.Framework.Markdown.Syntax)) are the structure of the document. A
  [ContainerBlock](cref:Meziantou.Framework.Markdown.Syntax.ContainerBlock), such as a list or a quote, contains other blocks. A [LeafBlock](cref:Meziantou.Framework.Markdown.Syntax.LeafBlock), such as a paragraph or a heading,
  contains text: its [Inline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.Inline) property holds the inlines of that text.
- **Inlines** ([Inline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.Inline), namespace [Meziantou.Framework.Markdown.Syntax.Inlines](cref:Meziantou.Framework.Markdown.Syntax.Inlines)) are the content of the text. A
  [ContainerInline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.ContainerInline), such as an emphasis or a link, contains other inlines. A [LeafInline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.LeafInline), such as a literal or a code
  span, does not.

| Markdown | Element | Useful members |
| --- | --- | --- |
| The document | [MarkdownDocument](cref:Meziantou.Framework.Markdown.Syntax.MarkdownDocument) | [LineCount](cref:Meziantou.Framework.Markdown.Syntax.MarkdownDocument.LineCount), the blocks |
| `# Title`, or a line underlined with `===` | [HeadingBlock](cref:Meziantou.Framework.Markdown.Syntax.HeadingBlock) | [Level](cref:Meziantou.Framework.Markdown.Syntax.HeadingBlock.Level), [IsSetext](cref:Meziantou.Framework.Markdown.Syntax.HeadingBlock.IsSetext) |
| A paragraph | [ParagraphBlock](cref:Meziantou.Framework.Markdown.Syntax.ParagraphBlock) | [Inline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.Inline) |
| `- item`, `1. item` | [ListBlock](cref:Meziantou.Framework.Markdown.Syntax.ListBlock) of [ListItemBlock](cref:Meziantou.Framework.Markdown.Syntax.ListItemBlock) | [IsOrdered](cref:Meziantou.Framework.Markdown.Syntax.ListBlock.IsOrdered), [BulletType](cref:Meziantou.Framework.Markdown.Syntax.ListBlock.BulletType), [OrderedStart](cref:Meziantou.Framework.Markdown.Syntax.ListBlock.OrderedStart), [IsLoose](cref:Meziantou.Framework.Markdown.Syntax.ListBlock.IsLoose) |
| `> quote` | [QuoteBlock](cref:Meziantou.Framework.Markdown.Syntax.QuoteBlock) | The blocks of the quote |
| A fenced code block | [FencedCodeBlock](cref:Meziantou.Framework.Markdown.Syntax.FencedCodeBlock) | [Info](cref:Meziantou.Framework.Markdown.Syntax.FencedCodeBlock.Info) (the language), [Arguments](cref:Meziantou.Framework.Markdown.Syntax.FencedCodeBlock.Arguments), [Lines](cref:Meziantou.Framework.Markdown.Syntax.LeafBlock.Lines) |
| An indented code block | [CodeBlock](cref:Meziantou.Framework.Markdown.Syntax.CodeBlock) | [Lines](cref:Meziantou.Framework.Markdown.Syntax.LeafBlock.Lines) |
| `<div>` | [HtmlBlock](cref:Meziantou.Framework.Markdown.Syntax.HtmlBlock) | [Type](cref:Meziantou.Framework.Markdown.Syntax.HtmlBlock.Type), [Lines](cref:Meziantou.Framework.Markdown.Syntax.LeafBlock.Lines) |
| `***`, `---` | [ThematicBreakBlock](cref:Meziantou.Framework.Markdown.Syntax.ThematicBreakBlock) | |
| `[label]: url` | [LinkReferenceDefinition](cref:Meziantou.Framework.Markdown.Syntax.LinkReferenceDefinition) | [Label](cref:Meziantou.Framework.Markdown.Syntax.LinkReferenceDefinition.Label), `Url`, [Title](cref:Meziantou.Framework.Markdown.Syntax.LinkReferenceDefinition.Title) |
| Text | [LiteralInline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.LiteralInline) | [Content](cref:Meziantou.Framework.Markdown.Syntax.Inlines.LiteralInline.Content); `ToString()` returns the text |
| `*a*`, `**a**` | [EmphasisInline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.EmphasisInline) | [DelimiterChar](cref:Meziantou.Framework.Markdown.Syntax.Inlines.EmphasisInline.DelimiterChar), [DelimiterCount](cref:Meziantou.Framework.Markdown.Syntax.Inlines.EmphasisInline.DelimiterCount) (2 for strong) |
| `[a](url)`, `![a](url)` | [LinkInline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.LinkInline) | `Url`, [Title](cref:Meziantou.Framework.Markdown.Syntax.Inlines.LinkInline.Title), [IsImage](cref:Meziantou.Framework.Markdown.Syntax.Inlines.LinkInline.IsImage), [Label](cref:Meziantou.Framework.Markdown.Syntax.Inlines.LinkInline.Label) |
| `<https://…>` | [AutolinkInline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.AutolinkInline) | `Url`, [IsEmail](cref:Meziantou.Framework.Markdown.Syntax.Inlines.AutolinkInline.IsEmail) |
| `` `code` `` | [CodeInline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.CodeInline) | [Content](cref:Meziantou.Framework.Markdown.Syntax.Inlines.CodeInline.Content) |
| `<b>` | [HtmlInline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.HtmlInline) | [Tag](cref:Meziantou.Framework.Markdown.Syntax.Inlines.HtmlInline.Tag) |
| `&amp;` | [HtmlEntityInline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.HtmlEntityInline) | [Original](cref:Meziantou.Framework.Markdown.Syntax.Inlines.HtmlEntityInline.Original), [Transcoded](cref:Meziantou.Framework.Markdown.Syntax.Inlines.HtmlEntityInline.Transcoded) |
| A line break | [LineBreakInline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.LineBreakInline) | [IsHard](cref:Meziantou.Framework.Markdown.Syntax.Inlines.LineBreakInline.IsHard) |

Each extension adds its own elements, in its namespace: [Table](cref:Meziantou.Framework.Markdown.Extensions.Tables.Table) for tables, [Footnote](cref:Meziantou.Framework.Markdown.Extensions.Footnotes.Footnote), [TaskList](cref:Meziantou.Framework.Markdown.Extensions.TaskLists.TaskList), [AlertBlock](cref:Meziantou.Framework.Markdown.Extensions.Alerts.AlertBlock),
[YamlFrontMatterBlock](cref:Meziantou.Framework.Markdown.Extensions.Yaml.YamlFrontMatterBlock)… See [Read a table from the tree](04-Extensions/02-Tables.md#read-a-table-from-the-tree).

## Walk the tree

A [ContainerBlock](cref:Meziantou.Framework.Markdown.Syntax.ContainerBlock) is a list of its child blocks: index it, count it or enumerate it. Every block has a [Parent](cref:Meziantou.Framework.Markdown.Syntax.Block.Parent).
Inlines are linked: a [ContainerInline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.ContainerInline) has a [FirstChild](cref:Meziantou.Framework.Markdown.Syntax.Inlines.ContainerInline.FirstChild) and a [LastChild](cref:Meziantou.Framework.Markdown.Syntax.Inlines.ContainerInline.LastChild), and each inline has a [Parent](cref:Meziantou.Framework.Markdown.Syntax.Inlines.Inline.Parent), a
[PreviousSibling](cref:Meziantou.Framework.Markdown.Syntax.Inlines.Inline.PreviousSibling) and a [NextSibling](cref:Meziantou.Framework.Markdown.Syntax.Inlines.Inline.NextSibling):

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
[LiteralInline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.LiteralInline) elements. For instance, to build a table of contents:

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

The text of code spans ([CodeInline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.CodeInline)) and of the other leaf inlines is in their own properties. To render an element as
plain text, see [Plain text and other formats](08-PlainTextAndOtherFormats.md).

## Attach data to elements

[SetData](cref:Meziantou.Framework.Markdown.Syntax.MarkdownObject.SetData) and [GetData](cref:Meziantou.Framework.Markdown.Syntax.MarkdownObject.GetData) attach your own values to any element of the tree, for instance the results of an analysis. A
[DataKey\<T\>](cref:Meziantou.Framework.Markdown.Syntax.DataKey{T}) gives typed access:

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

`SetData(key, value)` and `GetData(key)` also accept any object as the key. [TryGetData](cref:Meziantou.Framework.Markdown.Syntax.MarkdownObjectDataExtensions.TryGetData) tells whether a value is set,
and [RemoveData](cref:Meziantou.Framework.Markdown.Syntax.MarkdownObject.RemoveData) removes it. The renderers do not write your data.

## Debug the tree

[DumpTo](cref:Meziantou.Framework.Markdown.Syntax.Inlines.Inline.DumpTo) writes the inlines of an element, one per line, indented by their depth:

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
