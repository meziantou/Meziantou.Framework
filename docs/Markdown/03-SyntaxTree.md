---
title: The syntax tree
description: Read the nodes of a document, edit it without changing the rest of its text, and write text into Markdown.
---

## Read the nodes

Each construct has its type of node, which derives from
[MarkdownBlockSyntax](cref:Meziantou.Framework.Markdown.MarkdownBlockSyntax) for a block, or from
[MarkdownInlineSyntax](cref:Meziantou.Framework.Markdown.MarkdownInlineSyntax) for an inline:

| Blocks | Inlines |
| --- | --- |
| [MarkdownParagraphSyntax](cref:Meziantou.Framework.Markdown.MarkdownParagraphSyntax) | [MarkdownEmphasisSyntax](cref:Meziantou.Framework.Markdown.MarkdownEmphasisSyntax) |
| [MarkdownHeadingSyntax](cref:Meziantou.Framework.Markdown.MarkdownHeadingSyntax) | [MarkdownStrikethroughSyntax](cref:Meziantou.Framework.Markdown.MarkdownStrikethroughSyntax) |
| [MarkdownBlockQuoteSyntax](cref:Meziantou.Framework.Markdown.MarkdownBlockQuoteSyntax) | [MarkdownCodeSpanSyntax](cref:Meziantou.Framework.Markdown.MarkdownCodeSpanSyntax) |
| [MarkdownListSyntax](cref:Meziantou.Framework.Markdown.MarkdownListSyntax), [MarkdownListItemSyntax](cref:Meziantou.Framework.Markdown.MarkdownListItemSyntax) | [MarkdownLinkSyntax](cref:Meziantou.Framework.Markdown.MarkdownLinkSyntax), for links and images |
| [MarkdownCodeBlockSyntax](cref:Meziantou.Framework.Markdown.MarkdownCodeBlockSyntax), fenced or indented | [MarkdownAutolinkSyntax](cref:Meziantou.Framework.Markdown.MarkdownAutolinkSyntax) |
| [MarkdownHtmlBlockSyntax](cref:Meziantou.Framework.Markdown.MarkdownHtmlBlockSyntax) | [MarkdownRawHtmlSyntax](cref:Meziantou.Framework.Markdown.MarkdownRawHtmlSyntax) |
| [MarkdownThematicBreakSyntax](cref:Meziantou.Framework.Markdown.MarkdownThematicBreakSyntax) | |
| [MarkdownLinkReferenceDefinitionSyntax](cref:Meziantou.Framework.Markdown.MarkdownLinkReferenceDefinitionSyntax) | |
| [MarkdownTableSyntax](cref:Meziantou.Framework.Markdown.MarkdownTableSyntax), with its rows and cells | |

The nodes give their tokens, such as the marker of a list item, and the values they stand for: the level of a heading,
the code of a code block without the markers of its containers, the destination of a link without its escapes, which a
reference link takes from its definition. The text between the inlines is tokens, such as a
[TextToken](cref:Meziantou.Framework.Markdown.SyntaxKind.TextToken).
[GetPlainText](cref:Meziantou.Framework.Markdown.MarkdownSyntaxNode.GetPlainText) gives the text of a node as a
reader sees it:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_TypedApi;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = MarkdownSyntaxTree.ParseText("""
    # Install *the* tool

    See [the guide][guide] or ![logo](/logo.png "Logo").

    [guide]: https://example.com/guide
    """);
var root = tree.GetRoot();

var heading = Assert.IsType<MarkdownHeadingSyntax>(root.Blocks[0]);
Assert.Equal(1, heading.Level);
Assert.Equal("Install the tool", heading.GetPlainText());

// A reference link gets its destination from its definition
var links = root.DescendantNodes().OfType<MarkdownLinkSyntax>().ToList();
Assert.Equal(MarkdownLinkKind.Full, links[0].LinkKind);
Assert.Equal("https://example.com/guide", links[0].Destination);
Assert.True(links[1].IsImage);
Assert.Equal("Logo", links[1].Title);
```
<!-- endref -->

[MarkdownSyntaxWalker](cref:Meziantou.Framework.Markdown.MarkdownSyntaxWalker) visits every node of a tree, with
a method for each type of node.

## Edit a document

An edit replaces a node or a token, and the text around it stays as it was, the markers of the containers included:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Editing;format=md-fenced-code-block;dedent=true -->
```csharp
var root = MarkdownSyntaxTree.ParseText("> - first\n>   item\n> - second\n").GetRoot();
var first = root.DescendantTokens().First(token => token.Text == "first");

// The new token takes the trivia of the one it replaces
var replacement = MarkdownSyntaxTree.ParseText("updated").GetRoot().DescendantTokens().First().WithTriviaFrom(first);
var updated = root.ReplaceToken(first, replacement);

// The markers of the quote and the indentation of the list item are where they were
Assert.Equal("> - updated\n>   item\n> - second\n", updated.ToFullString());
```
<!-- endref -->

[MarkdownSyntaxRewriter](cref:Meziantou.Framework.Markdown.MarkdownSyntaxRewriter) builds a new tree from the
nodes and tokens its methods return. A node it returns `null` for is removed, with its text:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxRewriter_RemoveHtml;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class RemoveHtml : MarkdownSyntaxRewriter
{
    public override SyntaxNode? VisitMarkdownHtmlBlock(MarkdownHtmlBlockSyntax node) => null;

    public override SyntaxNode? VisitMarkdownRawHtml(MarkdownRawHtmlSyntax node) => null;
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxRewriter_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = MarkdownSyntaxTree.ParseText("<div>\nhidden\n</div>\n\nText with <b>tags</b>.\n");

var root = new RemoveHtml().Visit(tree.GetRoot())!;

Assert.Equal("\nText with tags.\n", root.ToFullString());
```
<!-- endref -->

To add blocks, or to remove a block with its lines,
[InsertBlocksBefore](cref:Meziantou.Framework.Markdown.MarkdownSyntaxTree.InsertBlocksBefore(Meziantou.Framework.Markdown.MarkdownBlockSyntax,System.String)),
[InsertBlocksAfter](cref:Meziantou.Framework.Markdown.MarkdownSyntaxTree.InsertBlocksAfter(Meziantou.Framework.Markdown.MarkdownBlockSyntax,System.String))
and [RemoveBlock](cref:Meziantou.Framework.Markdown.MarkdownSyntaxTree.RemoveBlock(Meziantou.Framework.Markdown.MarkdownBlockSyntax))
change the text and parse it again. The new blocks are written as if they were not in a container: the markers of the
containers of the block are added to each of their lines.

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_InsertBlocks;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = MarkdownSyntaxTree.ParseText("> - first\n> - second\n");
var first = tree.GetRoot().DescendantNodes().OfType<MarkdownListItemSyntax>().First();

// The markers of the block quote are added to the new line
var updated = tree.InsertBlocksAfter(first, "- inserted");

Assert.Equal("> - first\n> - inserted\n> - second\n", updated.GetRoot().ToFullString());
Assert.HasCount(3, updated.GetRoot().DescendantNodes().OfType<MarkdownListItemSyntax>());
```
<!-- endref -->

The text is inserted as it is: separate the new blocks from the others with a blank line when they would otherwise be
read as one, such as two paragraphs. Removing the first block of a list item keeps the marker of the item. Inserting and removing
blocks parses the whole text again, as a block can change how the blocks after it are read.

## Write Markdown

[MarkdownEscaper](cref:Meziantou.Framework.Markdown.MarkdownEscaper) writes text into Markdown, so that it is
parsed back to the same text: [Escape](cref:Meziantou.Framework.Markdown.MarkdownEscaper.Escape(System.String))
escapes the ASCII punctuation of a text, and the other methods write a code span, a fenced code block, the destination, the
title and the label of a link, and the text of a table cell.

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownEscaper_Usage;format=md-fenced-code-block;dedent=true -->
````csharp
// Escape the text so that Markdown displays it as is: every ASCII punctuation character is escaped
Assert.Equal(@"\*Note\* \[docs\]\!", MarkdownEscaper.Escape("*Note* [docs]!"));

// The delimiters of code are longer than the runs of backticks of the content
Assert.Equal("``a`b``", MarkdownEscaper.CreateCodeSpan("a`b"));
Assert.Equal("`` `code` ``", MarkdownEscaper.CreateCodeSpan("`code`"));
Assert.Equal("```csharp\nvar a = 1;\n```\n", MarkdownEscaper.CreateFencedCodeBlock("var a = 1;", "csharp"));

// Build a link from untrusted parts
var text = MarkdownEscaper.Escape("Docs [v2]");
var destination = MarkdownEscaper.EscapeLinkDestination("https://example.com/a b");
var title = MarkdownEscaper.EscapeLinkTitle("Say \"hi\"");
Assert.Equal("<https://example.com/a b>", destination);
Assert.Equal("\"Say \\\"hi\\\"\"", title);

var html = MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText($"[{text}]({destination} {title})"));
Assert.Equal("<p><a href=\"https://example.com/a%20b\" title=\"Say &quot;hi&quot;\">Docs [v2]</a></p>\n", html);

// The label of a reference link
Assert.Equal(@"see \[1\]", MarkdownEscaper.EscapeLinkLabel("see [1]"));

// A cell of a pipe table cannot contain a pipe or a line ending
Assert.Equal(@"a \| b c", MarkdownEscaper.EscapeTableCell("a | b\nc"));
````
<!-- endref -->

The whitespace at the start and at the end of a line of escaped text is not kept, and a blank line starts a new paragraph.
The line endings of a title are written as entities, as a line could start a block, and the ones of a table cell are spaces.

