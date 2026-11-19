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

A node that replaces another can change how the text around it is read, such as a token without the blank line that
separated two paragraphs. [WithRoot](cref:Meziantou.Framework.Language.SyntaxTree.WithRoot(Meziantou.Framework.Language.SyntaxNode)) on a [MarkdownSyntaxTree](cref:Meziantou.Framework.Markdown.MarkdownSyntaxTree)
parses the text of the root again, so that the tree it returns is the one of its text.

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxRewriter_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = MarkdownSyntaxTree.ParseText("<div>\nhidden\n</div>\n\nText with <b>tags</b>.\n");

var root = new RemoveHtml().Visit(tree.GetRoot())!;

Assert.Equal("\nText with tags.\n", root.ToFullString());
```
<!-- endref -->

### Change a heading, a link or a code block

The `With` methods return a copy of a node with a new value, and write the Markdown for it: they escape the text, and
keep the rest of the node as it was. The copy is in no tree: put it in the place of the node with `ReplaceNode` or
`ReplaceNodes`.

| Node | Methods |
| --- | --- |
| [MarkdownHeadingSyntax](cref:Meziantou.Framework.Markdown.MarkdownHeadingSyntax) | [WithLevel](cref:Meziantou.Framework.Markdown.MarkdownHeadingSyntax.WithLevel(System.Int32)) |
| [MarkdownLinkSyntax](cref:Meziantou.Framework.Markdown.MarkdownLinkSyntax) | [WithDestination](cref:Meziantou.Framework.Markdown.MarkdownLinkSyntax.WithDestination(System.String)), [WithTitle](cref:Meziantou.Framework.Markdown.MarkdownLinkSyntax.WithTitle(System.String)) |
| [MarkdownLinkReferenceDefinitionSyntax](cref:Meziantou.Framework.Markdown.MarkdownLinkReferenceDefinitionSyntax) | [WithDestination](cref:Meziantou.Framework.Markdown.MarkdownLinkReferenceDefinitionSyntax.WithDestination(System.String)), [WithTitle](cref:Meziantou.Framework.Markdown.MarkdownLinkReferenceDefinitionSyntax.WithTitle(System.String)) |
| [MarkdownCodeBlockSyntax](cref:Meziantou.Framework.Markdown.MarkdownCodeBlockSyntax) | [WithInfo](cref:Meziantou.Framework.Markdown.MarkdownCodeBlockSyntax.WithInfo(System.String)) |

[WithLevel](cref:Meziantou.Framework.Markdown.MarkdownHeadingSyntax.WithLevel(System.Int32)) changes the number of `#` of an ATX heading, and the underline of a setext heading for the levels 1 and 2. A
setext heading of level 3 to 6 becomes an ATX heading, which has one line: its lines are joined with spaces, and a hard
line break becomes a space.

The destination and the title of a reference link are the ones of its definition: change the definition. Removing a
title passes `null`:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownLinkSyntax_WithDestination;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = MarkdownSyntaxTree.ParseText("See [the docs](http://example.com/docs \"Docs\") and ![logo](logo.png).\n");

// The destinations are escaped: a space needs angle brackets
var links = tree.GetRoot().DescendantNodes().OfType<MarkdownLinkSyntax>();
var root = tree.GetRoot().ReplaceNodes(links, (link, rewritten) => link.IsImage
    ? rewritten.WithDestination("images/my logo.png").WithTitle("Logo")
    : rewritten.WithDestination(link.Destination.Replace("http://", "https://", StringComparison.Ordinal)).WithTitle(null));

Assert.Equal("See [the docs](https://example.com/docs) and ![logo](<images/my logo.png> \"Logo\").\n", root.ToFullString());
```
<!-- endref -->

A copy only changes the text of the node, but in Markdown, that text can change how the text around it is read. For
example, without its title, a definition can take the next line as its title when that line is in quotes. Check the
result by parsing the text again when the document can contain such cases.

### Change the generic attributes of a block

With [GenericAttributes](cref:Meziantou.Framework.Markdown.MarkdownExtensions.GenericAttributes), any block can
have an id, classes and other HTML attributes, written as `{#id .class name=value}`.
[GetGenericAttributes](cref:Meziantou.Framework.Markdown.MarkdownBlockSyntax.GetGenericAttributes) returns the ones the
HTML renderer gives to the block, and
[WithGenericAttributes](cref:Meziantou.Framework.Markdown.MarkdownSyntaxTree.WithGenericAttributes(Meziantou.Framework.Markdown.MarkdownBlockSyntax,Meziantou.Framework.Markdown.MarkdownGenericAttributes))
replaces them, or removes them when you pass `null`. Every `{...}` the block had is removed, and the new attributes are written where Markdig reads them:

- at the end of the first line of a heading, a fenced code block, a math block or a custom container,
- at the end of the last line of a paragraph,
- on a line before the other blocks, such as a list, a block quote or a table, with the markers of their containers. A
  blank line separates this line from a paragraph before it, or from a thematic break, which would make it a setext heading.

The text is parsed again, and the method checks that the block has the attributes. It throws an
`InvalidOperationException` for a block Markdig gives no attributes to, such as a list item or a footnote definition.

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_WithGenericAttributes;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GenericAttributes | MarkdownExtensions.AutoIdentifiers };
var tree = MarkdownSyntaxTree.ParseText("# Steps {.lead}\n\n> - Install\n> - Run\n", options);

// Add a property to the attributes of the heading, which keeps its class, and give the list an id
var heading = tree.GetRoot().Blocks.OfType<MarkdownHeadingSyntax>().Single();
var attributes = heading.GetGenericAttributes();
tree = tree.WithGenericAttributes(heading, attributes with { Properties = [.. attributes.Properties, new("data-level", "1")] });

var list = tree.GetRoot().DescendantNodes().OfType<MarkdownListSyntax>().Single();
tree = tree.WithGenericAttributes(list, new MarkdownGenericAttributes("steps", ["compact"], []));

// The attributes of a list are on the line before it, with the markers of the block quote
Assert.Equal("# Steps {.lead data-level=1}\n\n> {#steps .compact}\n> - Install\n> - Run\n", tree.GetRoot().ToFullString());
Assert.Equal("steps", tree.GetRoot().DescendantNodes().OfType<MarkdownListSyntax>().Single().GetGenericAttributes().Id);
```
<!-- endref -->

[MarkdownGenericAttributes](cref:Meziantou.Framework.Markdown.MarkdownGenericAttributes) is a record: `with` changes
one part, such as the id. Each call of
[WithGenericAttributes](cref:Meziantou.Framework.Markdown.MarkdownSyntaxTree.WithGenericAttributes(Meziantou.Framework.Markdown.MarkdownBlockSyntax,Meziantou.Framework.Markdown.MarkdownGenericAttributes)) parses the whole text again. To change many blocks, use a
[MarkdownSyntaxTreeEditor](cref:Meziantou.Framework.Markdown.MarkdownSyntaxTreeEditor): it records the changes to the
blocks of a tree, and [GetChangedTree](cref:Meziantou.Framework.Markdown.MarkdownSyntaxTreeEditor.GetChangedTree)
writes them and parses the text once. Here, every heading gets the identifier it has, so the links to it keep working
when its text changes:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_HeadingIdentifiers;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GenericAttributes | MarkdownExtensions.AutoIdentifiers };
var tree = MarkdownSyntaxTree.ParseText("# Introduction\n\nGetting started {.lead}\n===\n", options);

// Write the identifier of every heading as its id, so that the links to it keep working when its text changes. The editor
// parses the text once, after all the changes: the headings are the ones of the original tree.
var editor = new MarkdownSyntaxTreeEditor(tree);
foreach (var heading in tree.GetRoot().DescendantNodes().OfType<MarkdownHeadingSyntax>())
{
    editor.SetGenericAttributes(heading, heading.GetGenericAttributes() with { Id = heading.Identifier });
}

tree = editor.GetChangedTree();

Assert.Equal("# Introduction {#introduction}\n\nGetting started {#getting-started .lead}\n===\n", tree.GetRoot().ToFullString());
```
<!-- endref -->

### Insert, replace and remove blocks

To add blocks, or to replace or remove a block with its lines,
[InsertBlocksBefore](cref:Meziantou.Framework.Markdown.MarkdownSyntaxTree.InsertBlocksBefore(Meziantou.Framework.Markdown.MarkdownBlockSyntax,System.String)),
[InsertBlocksAfter](cref:Meziantou.Framework.Markdown.MarkdownSyntaxTree.InsertBlocksAfter(Meziantou.Framework.Markdown.MarkdownBlockSyntax,System.String)),
[ReplaceBlock](cref:Meziantou.Framework.Markdown.MarkdownSyntaxTree.ReplaceBlock(Meziantou.Framework.Markdown.MarkdownBlockSyntax,System.String))
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

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_ReplaceBlock;format=md-fenced-code-block;dedent=true -->
````csharp
var tree = MarkdownSyntaxTree.ParseText("> - first\n> - second\n");
var paragraph = tree.GetRoot().DescendantNodes().OfType<MarkdownParagraphSyntax>().First();

// The markers of the block quote and the indentation of the list item are added to the new lines
var updated = tree.ReplaceBlock(paragraph, "Paragraph\n\n```\ncode\n```");

Assert.Equal("> - Paragraph\n>\n>   ```\n>   code\n>   ```\n> - second\n", updated.GetRoot().ToFullString());
````
<!-- endref -->

The markers of the containers are the `>` of a block quote, the `^^` of a footer, and the indentation of the content
of a list item, of a footnote and of a definition: 4 spaces for a footnote, whatever the length of its label. The edits
of an indented code block start before its indentation, and inserting blocks after a fenced block that is not closed,
such as a code block that ends the document, writes its closing fence first, so that the new blocks are not in the code. The
kind of an alert, such as `[!NOTE]`, stays on its line when its first paragraph is replaced or removed.

The text is inserted as it is: separate the new blocks from the others with a blank line when they would otherwise be
read as one, such as two paragraphs. Removing a block writes a blank line in place of its lines when the lines around
them are not blank, so that the blocks around it stay apart: a paragraph before a thematic break would otherwise become
a setext heading. Removing the first block of a list item keeps the marker of the item, and moves the next block of the
item to the line of the marker.

Each of these methods parses the whole text again, as a block can change how the blocks after it are read.
[MarkdownSyntaxTreeEditor](cref:Meziantou.Framework.Markdown.MarkdownSyntaxTreeEditor) has the same methods, and
parses the text once for all the changes. Its changes are given with the blocks of the original tree, and cannot
overlap: changing a block and a block it contains throws.

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

