---
title: Markdown
description: A prototype of a CommonMark and GitHub Flavored Markdown parser whose tree keeps every character, so that a document is written back exactly and an edit only changes what it touches.
---

`Meziantou.Framework.Language.Markdown` parses CommonMark, and optionally the extensions of GitHub Flavored Markdown,
into a tree that keeps every character of the text, and renders it as HTML. It is a prototype: it is not published, and
its API is not final.

## Parse a document

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Overview;format=md-fenced-code-block;dedent=true -->
```csharp
var text = """
    > A quote with *emphasis*
    > on two lines
    """;
var tree = MarkdownSyntaxTree.ParseText(text);

// Every character is in the tree: the markers of the quote on its second line are trivia
Assert.Equal(text, tree.GetRoot().ToFullString());
Assert.Equal("<blockquote>\n<p>A quote with <em>emphasis</em>\non two lines</p>\n</blockquote>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

The tokens are what the constructs are made of: the marker that opens a block quote, the backticks of a code span, the
brackets and the destination of a link, the delimiters of an emphasis.

The characters that no token covers are trivia, as in the other languages: the indentation, the line endings that end
blocks, and the markers of the containers on their continuation lines. In `> a` followed by `> b`, the first `>` is a
token of the block quote, and the second one is a
[BlockQuoteMarkerTrivia](cref:Meziantou.Framework.Language.Markdown.SyntaxKind.BlockQuoteMarkerTrivia) before `b`. The
line endings in a paragraph are tokens, because they are part of its text: a soft or a hard line break.

## Read the nodes

Each construct has its type of node, which derives from
[MarkdownBlockSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownBlockSyntax) for a block, or from
[MarkdownInlineSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownInlineSyntax) for an inline:

| Blocks | Inlines |
| --- | --- |
| [MarkdownParagraphSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownParagraphSyntax) | [MarkdownEmphasisSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownEmphasisSyntax) |
| [MarkdownHeadingSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownHeadingSyntax) | [MarkdownStrikethroughSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownStrikethroughSyntax) |
| [MarkdownBlockQuoteSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownBlockQuoteSyntax) | [MarkdownCodeSpanSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownCodeSpanSyntax) |
| [MarkdownListSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownListSyntax), [MarkdownListItemSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownListItemSyntax) | [MarkdownLinkSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownLinkSyntax), for links and images |
| [MarkdownCodeBlockSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownCodeBlockSyntax), fenced or indented | [MarkdownAutolinkSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownAutolinkSyntax) |
| [MarkdownHtmlBlockSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownHtmlBlockSyntax) | [MarkdownRawHtmlSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownRawHtmlSyntax) |
| [MarkdownThematicBreakSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownThematicBreakSyntax) | |
| [MarkdownLinkReferenceDefinitionSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownLinkReferenceDefinitionSyntax) | |
| [MarkdownTableSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownTableSyntax), with its rows and cells | |

The nodes give their tokens, such as the marker of a list item, and the values they stand for: the level of a heading,
the code of a code block without the markers of its containers, the destination of a link without its escapes, which a
reference link takes from its definition. The text between the inlines is tokens, such as a
[TextToken](cref:Meziantou.Framework.Language.Markdown.SyntaxKind.TextToken).
[GetPlainText](cref:Meziantou.Framework.Language.Markdown.MarkdownSyntaxNode.GetPlainText) gives the text of a node as a
reader sees it:

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_TypedApi;format=md-fenced-code-block;dedent=true -->
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

[MarkdownSyntaxWalker](cref:Meziantou.Framework.Language.Markdown.MarkdownSyntaxWalker) visits every node of a tree, with
a method for each type of node.

## Edit a document

An edit replaces a node or a token, and the text around it stays as it was, the markers of the containers included:

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Editing;format=md-fenced-code-block;dedent=true -->
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

[MarkdownSyntaxRewriter](cref:Meziantou.Framework.Language.Markdown.MarkdownSyntaxRewriter) builds a new tree from the
nodes and tokens its methods return. A node it returns `null` for is removed, with its text:

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxRewriter_RemoveHtml;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class RemoveHtml : MarkdownSyntaxRewriter
{
    public override SyntaxNode? VisitMarkdownHtmlBlock(MarkdownHtmlBlockSyntax node) => null;

    public override SyntaxNode? VisitMarkdownRawHtml(MarkdownRawHtmlSyntax node) => null;
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxRewriter_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = MarkdownSyntaxTree.ParseText("<div>\nhidden\n</div>\n\nText with <b>tags</b>.\n");

var root = new RemoveHtml().Visit(tree.GetRoot())!;

Assert.Equal("\nText with tags.\n", root.ToFullString());
```
<!-- endref -->

To add blocks, or to remove a block with its lines,
[InsertBlocksBefore](cref:Meziantou.Framework.Language.Markdown.MarkdownSyntaxTree.InsertBlocksBefore(Meziantou.Framework.Language.Markdown.MarkdownBlockSyntax,System.String)),
[InsertBlocksAfter](cref:Meziantou.Framework.Language.Markdown.MarkdownSyntaxTree.InsertBlocksAfter(Meziantou.Framework.Language.Markdown.MarkdownBlockSyntax,System.String))
and [RemoveBlock](cref:Meziantou.Framework.Language.Markdown.MarkdownSyntaxTree.RemoveBlock(Meziantou.Framework.Language.Markdown.MarkdownBlockSyntax))
change the text and parse it again. The new blocks are written as if they were not in a container: the markers of the
containers of the block are added to each of their lines.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_InsertBlocks;format=md-fenced-code-block;dedent=true -->
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
read as one, such as two paragraphs. Removing the first block of a list item keeps the marker of the item.

## Render HTML

[MarkdownHtmlRenderer.ToHtml](cref:Meziantou.Framework.Language.Markdown.MarkdownHtmlRenderer.ToHtml(Meziantou.Framework.Language.Markdown.MarkdownSyntaxTree))
renders the HTML of the CommonMark reference implementation. The parser follows CommonMark 0.31.2, and renders the
HTML of every example of the specification.

## GitHub Flavored Markdown

[MarkdownParseOptions.GitHubFlavored](cref:Meziantou.Framework.Language.Markdown.MarkdownParseOptions.GitHubFlavored)
parses the extensions of [GitHub Flavored Markdown](https://github.github.com/gfm/): tables, strikethrough, task list
items, extended autolinks, and the tag filter of the HTML renderer.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_GitHubFlavored;format=md-fenced-code-block;dedent=true -->
```csharp
var text = """
    | Task       | Done |
    | ---------- | :--: |
    | ~~Draft~~  | yes  |

    - [x] Read www.example.com
    """;
var tree = MarkdownSyntaxTree.ParseText(text, MarkdownParseOptions.GitHubFlavored);

Assert.Equal(text, tree.GetRoot().ToFullString());
Assert.Equal("""
    <table>
    <thead>
    <tr>
    <th>Task</th>
    <th align="center">Done</th>
    </tr>
    </thead>
    <tbody>
    <tr>
    <td><del>Draft</del></td>
    <td align="center">yes</td>
    </tr>
    </tbody>
    </table>
    <ul>
    <li><input checked="" disabled="" type="checkbox"> Read <a href="http://www.example.com">www.example.com</a></li>
    </ul>

    """.ReplaceLineEndings("\n"), MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

[Extensions](cref:Meziantou.Framework.Language.Markdown.MarkdownParseOptions.Extensions) chooses the extensions one by one,
from the flags of [MarkdownExtensions](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions):

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Extensions;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Strikethrough };
var tree = MarkdownSyntaxTree.ParseText("~~old~~ www.example.com", options);

// Strikethrough only: the address is text
Assert.Equal("<p><del>old</del> www.example.com</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

The extensions keep every character too:

- A table has a header row, a delimiter row, and its other rows. The pipes and the cells of the delimiter row (`:--:`)
  are tokens of the rows, and a cell holds its inlines. In a cell, `\|` is a pipe, even in a code span, as GFM says.
- The `[ ]` or `[x]` of a task list item is a token of the list item, after its marker.
- A strikethrough has its tildes as tokens, as an emphasis has its delimiters.
- An extended autolink (`www.example.com`, `https://example.com`, `user@example.com`) holds the tokens of its text.

The parser follows the reference implementation, cmark-gfm, where the specification leaves a case open, with two
differences: a task list item marker is recognized in any container, such as a block quote, and needs text after it;
and the delimiters between the tildes of a strikethrough are text, as they are in an emphasis, so that the constructs
never overlap.

## Other extensions

The other flags of [MarkdownExtensions](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions) enable the
extensions of `Meziantou.Framework.Markdown`, which this parser is meant to replace:

- [EmphasisExtras](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.EmphasisExtras): subscript (`H~2~O`),
  superscript (`2^10^`), inserted (`++text++`) and marked text (`==text==`), and strikethrough (`~~text~~`). They are
  [MarkdownEmphasisExtraSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownEmphasisExtraSyntax) nodes. When
  subscript is enabled, a single `~` is a subscript rather than a strikethrough.
- [HardLineBreaks](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.HardLineBreaks): the line breaks of a
  paragraph are rendered as `<br />`.
- [YamlFrontMatter](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.YamlFrontMatter): a front matter at
  the start of the document, between `---` and `---` or `...`, is a
  [MarkdownFrontMatterSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownFrontMatterSyntax), which the renderer
  does not write. Its `Yaml` property gives the YAML.
- [Mathematics](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.Mathematics): inline math between `$` or
  `$$`, and math blocks between `$$` lines, rendered for MathJax or KaTeX as `\(...\)` and `\[...\]`.
- [Abbreviations](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.Abbreviations): `*[HTML]: Hypertext
  Markup Language` defines an abbreviation, a
  [MarkdownAbbreviationDefinitionSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownAbbreviationDefinitionSyntax)
  that the renderer does not write, and each word of the text that is the abbreviation is a
  [MarkdownAbbreviationSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownAbbreviationSyntax), rendered as
  `<abbr title="Hypertext Markup Language">HTML</abbr>`. The titles that the abbreviations add are limited to the length
  of the text, and to at least 100,000 characters, so that a short text cannot render as a huge one.
- [Alerts](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.Alerts): the alerts of GitHub, a block quote
  whose first line is `[!NOTE]`, `[!TIP]`, `[!IMPORTANT]`, `[!WARNING]` or `[!CAUTION]`, which is a
  [MarkdownAlertSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownAlertSyntax). Only the block quotes of the
  document are alerts, unless
  [AllowNestedAlerts](cref:Meziantou.Framework.Language.Markdown.MarkdownParseOptions.AllowNestedAlerts) is set.
- [Diagrams](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.Diagrams): a code block of `mermaid` is
  rendered as `<pre class="mermaid">`, and one of `nomnoml` as `<div class="nomnoml">`, for their scripts to draw.

[DisableHtml](cref:Meziantou.Framework.Language.Markdown.MarkdownParseOptions.DisableHtml) reads HTML as text: the
renderer escapes it, and autolinks such as `<https://example.com>` are still links.

## Limitations of the prototype

- Not all the extensions of `Meziantou.Framework.Markdown` are supported yet: no footnotes, front matter, math...
- Inserting and removing blocks parses the whole text again.
- Parsing allocates more and is slower than [Meziantou.Framework.Markdown](../Markdown/01-Introduction.md).
