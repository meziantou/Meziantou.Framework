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

## Write Markdown

[MarkdownEscaper](cref:Meziantou.Framework.Language.Markdown.MarkdownEscaper) writes text into Markdown, so that it is
parsed back to the same text: [Escape](cref:Meziantou.Framework.Language.Markdown.MarkdownEscaper.Escape(System.String))
escapes the ASCII punctuation of a text, and the other methods write a code span, a fenced code block, the destination, the
title and the label of a link, and the text of a table cell.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownEscaper_Usage;format=md-fenced-code-block;dedent=true -->
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

## Render HTML

[MarkdownHtmlRenderer.ToHtml](cref:Meziantou.Framework.Language.Markdown.MarkdownHtmlRenderer.ToHtml(Meziantou.Framework.Language.Markdown.MarkdownSyntaxTree))
renders the HTML of the CommonMark reference implementation. The parser follows CommonMark 0.31.2, and renders the
HTML of every example of the specification.

[MarkdownHtmlRendererOptions](cref:Meziantou.Framework.Language.Markdown.MarkdownHtmlRendererOptions) changes the HTML of
the links: [BaseUrl](cref:Meziantou.Framework.Language.Markdown.MarkdownHtmlRendererOptions.BaseUrl) makes the relative
URLs absolute, [LinkRewriter](cref:Meziantou.Framework.Language.Markdown.MarkdownHtmlRendererOptions.LinkRewriter) changes
the URLs, [LinkRel](cref:Meziantou.Framework.Language.Markdown.MarkdownHtmlRendererOptions.LinkRel) sets the `rel`
attribute of the links, such as `nofollow`, and
[EscapeNonAsciiUrlCharacters](cref:Meziantou.Framework.Language.Markdown.MarkdownHtmlRendererOptions.EscapeNonAsciiUrlCharacters)
keeps the characters of the URLs that are not ASCII:

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlRenderer_LinkOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = MarkdownSyntaxTree.ParseText("[Next page](next.md) ![Logo](logo.png) <https://example.org>\n");
var options = new MarkdownHtmlRendererOptions
{
    BaseUrl = new Uri("https://example.com/docs/"),
    LinkRewriter = url => url.EndsWith(".md", StringComparison.Ordinal) ? url[..^3] + ".html" : url,
    LinkRel = "nofollow noopener",
};

Assert.Equal("<p><a href=\"https://example.com/docs/next.html\" rel=\"nofollow noopener\">Next page</a> <img src=\"https://example.com/docs/logo.png\" alt=\"Logo\" /> <a href=\"https://example.org\" rel=\"nofollow noopener\">https://example.org</a></p>\n", MarkdownHtmlRenderer.ToHtml(tree, options));

// Characters that are not ASCII are percent-encoded, unless the options keep them
var image = MarkdownSyntaxTree.ParseText("![](photo-été.png)\n");
Assert.Equal("<p><img src=\"photo-%C3%A9t%C3%A9.png\" alt=\"\" /></p>\n", MarkdownHtmlRenderer.ToHtml(image));
Assert.Equal("<p><img src=\"photo-été.png\" alt=\"\" /></p>\n", MarkdownHtmlRenderer.ToHtml(image, new MarkdownHtmlRendererOptions { EscapeNonAsciiUrlCharacters = false }));
```
<!-- endref -->

[PragmaLines](cref:Meziantou.Framework.Language.Markdown.MarkdownHtmlRendererOptions.PragmaLines) gives the element of
the first block of each line the id `pragma-line-N`, where N is the number of the line, for an editor to scroll its
preview to a line.
[FindClosestLine](cref:Meziantou.Framework.Language.Markdown.MarkdownDocumentSyntax.FindClosestLine(System.Int32)) finds
the line of the block that is the closest to a line:

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlRenderer_PragmaLines;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = MarkdownSyntaxTree.ParseText("# Title\n\nSome text.\n\n- First\n- Second\n");

Assert.Equal("<h1 id=\"pragma-line-0\">Title</h1>\n<p id=\"pragma-line-2\">Some text.</p>\n<ul id=\"pragma-line-4\">\n<li>First</li>\n<li id=\"pragma-line-5\">Second</li>\n</ul>\n", MarkdownHtmlRenderer.ToHtml(tree, new MarkdownHtmlRendererOptions { PragmaLines = true }));

// The editor shows line 3, a blank line: scroll the preview to the closest element
Assert.Equal(4, tree.GetRoot().FindClosestLine(3));
```
<!-- endref -->

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
- [Footnotes](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.Footnotes): `[^1]: text` defines a footnote,
  a [MarkdownFootnoteDefinitionSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownFootnoteDefinitionSyntax) whose
  next lines are indented by four spaces, and `[^1]` refers to it, a
  [MarkdownFootnoteReferenceSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownFootnoteReferenceSyntax). The
  renderer writes the footnotes that are referred to at the end of the document, numbered in the order of their first
  reference, with links back to the references.
- [GenericAttributes](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.GenericAttributes): `{#id .class
  name=value}` sets the HTML attributes of an element, as
  [MarkdownGenericAttributesSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownGenericAttributesSyntax) nodes:
  after an inline that is not text, such as a link, a code span or an emphasis, the attributes are for it; after text, they
  are for the block; at the end of the line of a heading, or after the info string of a fenced block, they are for the
  block; and a paragraph of attributes only gives them to the next block. By default, the renderer only writes the
  attributes that describe the content, such as `title`, `lang` or `aria-*`: event handlers, URL attributes, `style` or
  `data-*` can run script when the Markdown is not trusted.
  [MarkdownHtmlRendererOptions.AttributeFilter](cref:Meziantou.Framework.Language.Markdown.MarkdownHtmlRendererOptions.AttributeFilter)
  changes this.
- [Alerts](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.Alerts): the alerts of GitHub, a block quote
  whose first line is `[!NOTE]`, `[!TIP]`, `[!IMPORTANT]`, `[!WARNING]` or `[!CAUTION]`, which is a
  [MarkdownAlertSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownAlertSyntax). Only the block quotes of the
  document are alerts, unless
  [AllowNestedAlerts](cref:Meziantou.Framework.Language.Markdown.MarkdownParseOptions.AllowNestedAlerts) is set.
- [Diagrams](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.Diagrams): a code block of `mermaid` is
  rendered as `<pre class="mermaid">`, and one of `nomnoml` as `<div class="nomnoml">`, for their scripts to draw.

- [AutoIdentifiers](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.AutoIdentifiers): each heading gets an
  `id` made from its text,
  [MarkdownHeadingSyntax.Identifier](cref:Meziantou.Framework.Language.Markdown.MarkdownHeadingSyntax.Identifier), unless
  its generic attributes give one. Headings with the same text get a suffix: `-1`, `-2`... A reference link without
  definition whose label is the first line of a heading, such as `[Getting started]`, links to it:
  [MarkdownLinkSyntax.Heading](cref:Meziantou.Framework.Language.Markdown.MarkdownLinkSyntax.Heading) gives the heading.
  [AutoIdentifierOptions](cref:Meziantou.Framework.Language.Markdown.MarkdownParseOptions.AutoIdentifierOptions) chooses
  whether these links are made, whether the identifier only has ASCII characters (`Ærø` becomes `aeroe`), and whether it
  is made as GitHub makes it.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_AutoIdentifiers;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.AutoIdentifiers };
var tree = MarkdownSyntaxTree.ParseText("# Getting started\n\nSee [Getting started].\n\n# Getting started\n", options);

// The second heading with the same text gets a suffix, and the link without definition links to the last heading
var headings = tree.GetRoot().Blocks.OfType<MarkdownHeadingSyntax>().ToList();
Assert.Equal("getting-started", headings[0].Identifier);
Assert.Equal("getting-started-1", headings[1].Identifier);
Assert.Equal("<p>See <a href=\"#getting-started-1\">Getting started</a>.</p>", MarkdownHtmlRenderer.ToHtml(tree).Split('\n')[1]);
```
<!-- endref -->

- [CustomContainers](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.CustomContainers): blocks between a
  fence of three colons or more, `:::name arguments`, and a fence as long, are a
  [MarkdownCustomContainerSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownCustomContainerSyntax), rendered as
  `<div class="name">`. The closing fence closes the container and its blocks, even a code block. Text between `::` is a
  [MarkdownEmphasisExtraSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownEmphasisExtraSyntax) of style
  `CustomContainer`, rendered as `<span>`.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_CustomContainers;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.CustomContainers };
var tree = MarkdownSyntaxTree.ParseText(":::warning Read this\nThis is ::important::.\n:::\n", options);

var container = Assert.IsType<MarkdownCustomContainerSyntax>(tree.GetRoot().Blocks.Single());
Assert.Equal("warning", container.Name);
Assert.Equal("Read this", container.Arguments);
Assert.Equal("<div class=\"warning\"><p>This is <span>important</span>.</p>\n</div>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

- [DefinitionLists](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.DefinitionLists): the lines of a
  paragraph are terms when the next line starts with `:` or `~` and its content starts four columns from the start of the
  line. The [MarkdownDefinitionListSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownDefinitionListSyntax) has
  [MarkdownDefinitionItemSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownDefinitionItemSyntax) definitions,
  each with its terms and its blocks, rendered as `<dl>`, `<dt>` and `<dd>`. The next lines of a definition are indented
  by four columns, and another marker starts the next definition.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_DefinitionLists;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.DefinitionLists };
var tree = MarkdownSyntaxTree.ParseText("Apple\nPear\n:   A fruit.\n:   A tree.\n", options);

// The lines of the paragraph before the marker are the terms, and a marker after a definition starts another one
var list = Assert.IsType<MarkdownDefinitionListSyntax>(tree.GetRoot().Blocks.Single());
Assert.Equal(["Apple", "Pear"], list.Items[0].Terms.Select(term => term.GetPlainText()).ToArray());
Assert.Empty(list.Items[1].Terms);
Assert.Equal("<dl>\n<dt>Apple</dt>\n<dt>Pear</dt>\n<dd>A fruit.</dd>\n<dd>A tree.</dd>\n</dl>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

- [ListExtras](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.ListExtras): ordered lists numbered by
  letters (`a.`, `B)`) or roman numbers (`iv.`, `X)`), rendered with the `type` attribute of `<ol>`.
  [MarkdownListSyntax.OrderedStyle](cref:Meziantou.Framework.Language.Markdown.MarkdownListSyntax.OrderedStyle) gives the
  numbering, and [StartNumber](cref:Meziantou.Framework.Language.Markdown.MarkdownListSyntax.StartNumber) the number of the
  first item. As in Markdig, `i`, `v` and `x` are roman numbers unless they continue a list of letters, and a list of
  letters can interrupt a paragraph whatever its first letter.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_ListExtras;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.ListExtras };
var tree = MarkdownSyntaxTree.ParseText("iv. Four\nv. Five\n", options);

var list = Assert.IsType<MarkdownListSyntax>(tree.GetRoot().Blocks.Single());
Assert.Equal(MarkdownOrderedListStyle.LowerRoman, list.OrderedStyle);
Assert.Equal(4, list.StartNumber);
Assert.Equal("<ol type=\"i\" start=\"4\">\n<li>Four</li>\n<li>Five</li>\n</ol>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

- [SmartyPants](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.SmartyPants): quotes, `<<` and `>>`,
  `...` at the end of a word, `--` and `---` are written as typographic characters: `&ldquo;`, `&laquo;`, `&hellip;`,
  `&ndash;`, `&mdash;`... A quote is typographic when it opens or closes as `_` would, and only when it is paired, so `l'ami`
  keeps its apostrophe. Each one is a token of kind
  [SmartyPantToken](cref:Meziantou.Framework.Language.Markdown.SyntaxKind.SmartyPantToken), whose value is a
  [MarkdownSmartyPant](cref:Meziantou.Framework.Language.Markdown.MarkdownSmartyPant).
  [MarkdownHtmlRendererOptions.SmartyPants](cref:Meziantou.Framework.Language.Markdown.MarkdownHtmlRendererOptions.SmartyPants)
  changes the HTML that the renderer writes for them.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_SmartyPants;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.SmartyPants };
var tree = MarkdownSyntaxTree.ParseText("\"Wait...\" -- it is 'done'\n", options);

Assert.Equal("<p>&ldquo;Wait&hellip;&rdquo; &ndash; it is &lsquo;done&rsquo;</p>\n", MarkdownHtmlRenderer.ToHtml(tree));

// The renderer can write other text, such as the characters themselves
var french = new MarkdownHtmlRendererOptions
{
    SmartyPants = new Dictionary<MarkdownSmartyPant, string>
    {
        [MarkdownSmartyPant.LeftDoubleQuote] = "\u00AB\u00A0",
        [MarkdownSmartyPant.RightDoubleQuote] = "\u00A0\u00BB",
    },
};
Assert.Equal("<p>\u00AB\u00A0Wait&hellip;\u00A0\u00BB &ndash; it is &lsquo;done&rsquo;</p>\n", MarkdownHtmlRenderer.ToHtml(tree, french));
```
<!-- endref -->

- [Emoji](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.Emoji): shortcodes such as `:rocket:`, and smileys
  such as `:)` or `<3`, are written as their emoji when they are not after a letter or a digit. Each one is a token of kind
  [EmojiToken](cref:Meziantou.Framework.Language.Markdown.SyntaxKind.EmojiToken).
  [EmojiMapping](cref:Meziantou.Framework.Language.Markdown.MarkdownParseOptions.EmojiMapping) chooses the shortcodes and
  the smileys: [MarkdownEmojiMapping.Default](cref:Meziantou.Framework.Language.Markdown.MarkdownEmojiMapping.Default) has the
  ones of Markdig, [DefaultWithoutSmileys](cref:Meziantou.Framework.Language.Markdown.MarkdownEmojiMapping.DefaultWithoutSmileys)
  only its shortcodes, and a mapping can be made from dictionaries.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Emoji;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Emoji };
Assert.Equal("<p>Ship it \U0001F680 :)</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("Ship it :rocket: :)\n", options with { EmojiMapping = MarkdownEmojiMapping.DefaultWithoutSmileys })));

// A mapping of its own: shortcodes, and smileys for them
var mapping = new MarkdownEmojiMapping(new Dictionary<string, string> { [":ok:"] = "\u2705" }, new Dictionary<string, string> { ["(y)"] = ":ok:" });
Assert.Equal("<p>\u2705 \u2705 :rocket:</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText(":ok: (y) :rocket:\n", options with { EmojiMapping = mapping })));
```
<!-- endref -->

- [Figures](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.Figures): blocks between a fence of three carets
  or more, `^^^`, and a fence as long are a
  [MarkdownFigureSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownFigureSyntax), rendered as `<figure>`. The text
  after a fence is a [MarkdownFigureCaptionSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownFigureCaptionSyntax),
  rendered as `<figcaption>`.
- [Footers](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.Footers): lines that start with `^^`, as a block
  quote does with `>`, are a [MarkdownFooterSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownFooterSyntax),
  rendered as `<footer>` with its paragraphs without `<p>`. A blank line ends it.
- [Citations](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.Citations): text between `""` is a
  [MarkdownEmphasisExtraSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownEmphasisExtraSyntax) of style
  `Citation`, rendered as `<cite>`. As `_`, the quotes cannot open or close in a word.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_FiguresFootersCitations;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Figures | MarkdownExtensions.Footers | MarkdownExtensions.Citations };
var tree = MarkdownSyntaxTree.ParseText("^^^\n![Logo](logo.png)\n^^^ The *logo*\n\n^^ From \"\"The Book\"\"\n", options);

Assert.Equal("<figure>\n<p><img src=\"logo.png\" alt=\"Logo\" /></p>\n<figcaption>The <em>logo</em></figcaption>\n</figure>\n<footer>From <cite>The Book</cite></footer>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

- [PipeTables](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.PipeTables): the pipe tables of Markdig,
  which replace the [tables of GFM](#github-flavored-markdown). The lines of a paragraph that have a pipe are a table when
  the second one is a delimiter row: a pipe or a line break ends a cell, so the rows can have no pipe around them, and
  the cells of the delimiter row can be empty. The pipes of a code span, of a link or of HTML are text, and a line without
  a pipe ends the table: the lines before and after the table are paragraphs. The table has as many columns as its longest
  row has cells, and a column after the last cell of the delimiter row has its alignment.
  [MarkdownTableSyntax.IsPipeTable](cref:Meziantou.Framework.Language.Markdown.MarkdownTableSyntax.IsPipeTable) tells
  them from the tables of GFM, and the generic attributes of their rows are for the table.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_PipeTables;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.PipeTables };
var tree = MarkdownSyntaxTree.ParseText("Fruits:\n| Name | Price |\n|:-----|------:|\n| Apple | 1 | each\n", options);
var table = tree.GetRoot().Blocks.OfType<MarkdownTableSyntax>().Single();

Assert.True(table.IsPipeTable);
Assert.Equal([MarkdownTableAlignment.Left, MarkdownTableAlignment.Right], table.Alignments);
Assert.Equal(["Apple", "1", "each"], table.Rows[0].Cells.Select(cell => cell.GetPlainText()));
Assert.Equal("<p>Fruits:</p>\n<table>\n<thead>\n<tr>\n<th align=\"left\">Name</th>\n<th align=\"right\">Price</th>\n<th align=\"right\"></th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td align=\"left\">Apple</td>\n<td align=\"right\">1</td>\n<td align=\"right\">each</td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

[PipeTableOptions](cref:Meziantou.Framework.Language.Markdown.MarkdownParseOptions.PipeTableOptions) has the options of
the pipe tables of Markdig.
[RequireHeaderSeparator](cref:Meziantou.Framework.Language.Markdown.MarkdownPipeTableOptions.RequireHeaderSeparator) set to
`false` reads the lines that have a pipe, up to the end of their paragraph, as a table without a header row:
[HeaderRow](cref:Meziantou.Framework.Language.Markdown.MarkdownTableSyntax.HeaderRow) and
[DelimiterRow](cref:Meziantou.Framework.Language.Markdown.MarkdownTableSyntax.DelimiterRow) are `null`.
[UseHeaderForColumnCount](cref:Meziantou.Framework.Language.Markdown.MarkdownPipeTableOptions.UseHeaderForColumnCount) gives
the table as many columns as its first row has cells, and
[InferColumnWidthsFromSeparator](cref:Meziantou.Framework.Language.Markdown.MarkdownPipeTableOptions.InferColumnWidthsFromSeparator)
writes the width of the columns, in proportion to their dashes. As in Markdig, the renderer adds at most 524,288 empty cells
to the short rows of a document: the rows of a table that would exceed the limit keep their cells.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_PipeTableOptions;format=md-fenced-code-block;dedent=true -->
```csharp
// The first row gives the number of columns, and the dashes the widths of the columns
var options = new MarkdownParseOptions
{
    Extensions = MarkdownExtensions.PipeTables,
    PipeTableOptions = new MarkdownPipeTableOptions { UseHeaderForColumnCount = true, InferColumnWidthsFromSeparator = true },
};
var tree = MarkdownSyntaxTree.ParseText("Name | Price\n:---|------:\nApple | 1 | each\n", options);

Assert.Equal("<table>\n<col style=\"width:33.33%\" />\n<col style=\"width:66.67%\" />\n<thead>\n<tr>\n<th align=\"left\">Name</th>\n<th align=\"right\">Price</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td align=\"left\">Apple</td>\n<td align=\"right\">1</td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));

// A table without a delimiter row has no header row
options = new MarkdownParseOptions
{
    Extensions = MarkdownExtensions.PipeTables,
    PipeTableOptions = new MarkdownPipeTableOptions { RequireHeaderSeparator = false },
};
tree = MarkdownSyntaxTree.ParseText("a | b\nc | d\n", options);
var table = tree.GetRoot().Blocks.OfType<MarkdownTableSyntax>().Single();

Assert.Null(table.HeaderRow);
Assert.Null(table.DelimiterRow);
Assert.HasCount(2, table.Rows);
Assert.Equal("<table>\n<tbody>\n<tr>\n<td>a</td>\n<td>b</td>\n</tr>\n<tr>\n<td>c</td>\n<td>d</td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

- [GridTables](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.GridTables): the grid tables of Markdig. A
  line of columns, `+---+:--:+`, starts the table, whose width the dashes of each column give, and the colons its
  alignment. The next lines start with `|`, which separates the cells, or with `+`, which separates the rows: a cell that a
  `+` line does not cross continues in the next row, and the rows above a line of `=` are the header. A cell spans the
  columns up to its next `|`. When the lines are not a table, they are a paragraph. A
  [MarkdownGridTableSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownGridTableSyntax) has the lines as tokens,
  and reads its [Columns](cref:Meziantou.Framework.Language.Markdown.MarkdownGridTableSyntax.Columns) and its
  [Rows](cref:Meziantou.Framework.Language.Markdown.MarkdownGridTableSyntax.Rows) from them. The content of a cell is a
  document of its own, [MarkdownGridTableCell.Content](cref:Meziantou.Framework.Language.Markdown.MarkdownGridTableCell.Content),
  parsed with the options of the tree: its reference links can use the definitions of the tree, but not its footnotes or
  its abbreviations.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_GridTables;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GridTables };
var tree = MarkdownSyntaxTree.ParseText("+-------+:------:+\n| Name  | Price  |\n+=======+========+\n| Apple | 1      |\n|       | each   |\n+-------+--------+\n| - Pear         |\n+-------+--------+\n", options);
var table = Assert.IsType<MarkdownGridTableSyntax>(tree.GetRoot().Blocks.Single());

Assert.Equal([MarkdownTableAlignment.None, MarkdownTableAlignment.Center], table.Columns.Select(column => column.Alignment));
Assert.True(table.Rows[0].IsHeader);

// The text of a cell is its lines between its pipes, which is a document of its own
var price = table.Rows[1].Cells[1];
Assert.Equal(" 1\n each", price.Text);
Assert.Equal("1\neach", price.Content.GetRoot().Blocks.Single().GetPlainText());
var pear = table.Rows[2].Cells.Single();
Assert.Equal(2, pear.ColumnSpan);
Assert.IsType<MarkdownListSyntax>(pear.Content.GetRoot().Blocks.Single());
```
<!-- endref -->

- [MediaLinks](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.MediaLinks): an image of an audio or a video
  file, such as `![](song.mp3)`, is rendered as `<audio>` or `<video>`, and one of YouTube, Vimeo, Yandex Music or
  Odnoklassniki as an `<iframe>`.
  [MarkdownHtmlRendererOptions.MediaLinks](cref:Meziantou.Framework.Language.Markdown.MarkdownHtmlRendererOptions.MediaLinks)
  chooses the size, the class, the file extensions and the sites:
  [MarkdownMediaHost.Create](cref:Meziantou.Framework.Language.Markdown.MarkdownMediaHost.Create(System.String,System.Func{System.Uri,System.String},System.Boolean,System.String))
  makes a site of its own.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_MediaLinks;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = MarkdownSyntaxTree.ParseText("![Intro](https://youtu.be/mswPy5bt3TQ)\n\n![Song](song.mp3)\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.MediaLinks });

Assert.Equal("<p><iframe src=\"https://www.youtube.com/embed/mswPy5bt3TQ\" class=\"youtube\" width=\"500\" height=\"281\" frameborder=\"0\" allowfullscreen=\"\"></iframe></p>\n<p><audio width=\"500\" controls=\"\"><source type=\"audio/mpeg\" src=\"song.mp3\"></source></audio></p>\n", MarkdownHtmlRenderer.ToHtml(tree));

// The options choose the size, and the sites that are an iframe
var options = new MarkdownHtmlRendererOptions
{
    MediaLinks = MarkdownMediaLinkOptions.Default with
    {
        Width = "100%",
        Height = "",
        Hosts = [MarkdownMediaHost.Create("videos.example.com", uri => "https://videos.example.com/embed" + uri.AbsolutePath, iframeClass: "example")],
    },
};
Assert.Equal("<p><iframe src=\"https://videos.example.com/embed/42\" class=\"example\" width=\"100%\" frameborder=\"0\" allowfullscreen=\"\"></iframe></p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("![](https://videos.example.com/42)\n", tree.Options), options));
```
<!-- endref -->

- [JiraLinks](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.JiraLinks): a reference to an issue, such as
  `PROJ-123`, after whitespace or `(` and before whitespace or `)`, is a
  [MarkdownJiraLinkSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownJiraLinkSyntax), a link to the issue.
  [MarkdownParseOptions.JiraLinks](cref:Meziantou.Framework.Language.Markdown.MarkdownParseOptions.JiraLinks) gives the URL
  of the site, and whether the links open in a new window.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_JiraLinks;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions
{
    Extensions = MarkdownExtensions.JiraLinks,
    JiraLinks = new MarkdownJiraLinkOptions { BaseUrl = "https://example.atlassian.net" },
};
var tree = MarkdownSyntaxTree.ParseText("Fixed in (PROJ-123), not in xPROJ-1.\n", options);

var link = tree.GetRoot().DescendantNodes().OfType<MarkdownJiraLinkSyntax>().Single();
Assert.Equal("PROJ", link.ProjectKey);
Assert.Equal("123", link.IssueNumber);
Assert.Equal("<p>Fixed in (<a href=\"https://example.atlassian.net/browse/PROJ-123\" target=\"_blank\">PROJ-123</a>), not in xPROJ-1.</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

- [MarkdigAutolinks](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.MarkdigAutolinks): the autolinks of
  Markdig, which replace the [extended autolinks of GFM](#github-flavored-markdown). A URL that starts with `http://`,
  `https://`, `ftp://`, `mailto:`, `tel:` or `www.`, after whitespace or one of `*_~(`, is a
  [MarkdownAutolinkSyntax](cref:Meziantou.Framework.Language.Markdown.MarkdownAutolinkSyntax) up to whitespace or trailing
  punctuation, when its parentheses are balanced and its domain is valid. It is not a link in a link, nor after an `<a>`
  that is not closed, and the characters of the delimiters of emphasis around it are not part of it.
  [AutolinkOptions](cref:Meziantou.Framework.Language.Markdown.MarkdownParseOptions.AutolinkOptions) chooses the characters
  before it, `https://` for `www.`, the domains without a period, and `target="_blank"`.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_MarkdigAutolinks;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions
{
    Extensions = MarkdownExtensions.MarkdigAutolinks,
    AutolinkOptions = new MarkdownAutolinkOptions { UseHttpsForWwwLinks = true },
};
var tree = MarkdownSyntaxTree.ParseText("See www.example.com, mailto:me@example.com or tel:+15551234.\n", options);

var links = tree.GetRoot().DescendantNodes().OfType<MarkdownAutolinkSyntax>().ToList();
Assert.Equal(["https://www.example.com", "mailto:me@example.com", "tel:+15551234"], links.Select(link => link.Destination));
Assert.Equal("<p>See <a href=\"https://www.example.com\">www.example.com</a>, <a href=\"mailto:me@example.com\">me@example.com</a> or <a href=\"tel:+15551234\">+15551234</a>.</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

- [CjkFriendlyEmphasis](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.CjkFriendlyEmphasis): the
  [CJK-friendly amendment](https://github.com/tats-u/markdown-cjk-friendly) of the delimiters of emphasis. CommonMark does
  not close `**テスト。**です`, as the punctuation before the delimiters is followed by a letter: a Chinese, Japanese or
  Korean character next to the delimiters counts as whitespace, so the delimiters can open or close.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_CjkFriendlyEmphasis;format=md-fenced-code-block;dedent=true -->
```csharp
// CommonMark does not close the emphasis: the punctuation before ** is followed by a letter
Assert.Equal("<p>**テスト。**です</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("**テスト。**です\n")));

var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.CjkFriendlyEmphasis };
Assert.Equal("<p><strong>テスト。</strong>です</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("**テスト。**です\n", options)));
```
<!-- endref -->

- [Bootstrap](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.Bootstrap): the renderer adds the classes of
  [Bootstrap](https://getbootstrap.com/): `table` to the tables, `img-fluid` to the images, `blockquote` to the block
  quotes, `figure` and `figure-caption` to the figures, and `alert` to the alerts, with the class of their kind, `role="alert"`
  and `mb-0` on their last paragraph. The classes come after the ones of the generic attributes.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlRenderer_Bootstrap;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Bootstrap | MarkdownExtensions.PipeTables };
var tree = MarkdownSyntaxTree.ParseText("> Quote\n\n![Logo](logo.png)\n\n| Name |\n|------|\n| Apple |\n", options);

Assert.Equal("<blockquote class=\"blockquote\">\n<p>Quote</p>\n</blockquote>\n<p><img src=\"logo.png\" class=\"img-fluid\" alt=\"Logo\" /></p>\n<table class=\"table\">\n<thead>\n<tr>\n<th>Name</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>Apple</td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

- [Globalization](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.Globalization): the renderer adds
  `dir="rtl"` to the blocks and the links whose text starts with a right-to-left character, such as Arabic or Hebrew, and
  `align="right"` to their tables. The direction is the one of the first character that has one: the digits, the
  punctuation and the whitespace have none, and a block takes the direction of its first child. As Markdig, the text of a
  construct that is not parsed, such as `]` or a lone `\`, is part of the text after it, but a delimiter of emphasis, a pipe
  of a pipe table or a bracket that a `]` closes without a link is not: `*مرحبا` is left-to-right.

<!-- ref:../../tests/Meziantou.Framework.Language.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlRenderer_Globalization;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Globalization | MarkdownExtensions.PipeTables };
var tree = MarkdownSyntaxTree.ParseText("Hello\n\n> مرحبا *world*\n\n| الاسم |\n|-------|\n| x |\n", options);

Assert.Equal("<p>Hello</p>\n<blockquote dir=\"rtl\">\n<p dir=\"rtl\">مرحبا <em>world</em></p>\n</blockquote>\n<table dir=\"rtl\" align=\"right\">\n<thead>\n<tr>\n<th>الاسم</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>x</td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

[Advanced](cref:Meziantou.Framework.Language.Markdown.MarkdownExtensions.Advanced) enables the extensions of
`UseAdvancedExtensions` of `Meziantou.Framework.Markdown` 1.x: alerts, abbreviations, auto identifiers, citations, custom
containers, definition lists, the extra emphasis, figures, footers, footnotes, grid tables, mathematics, media links, pipe
tables, list extras, task lists, diagrams, the autolinks of Markdig and generic attributes.

[DisableHtml](cref:Meziantou.Framework.Language.Markdown.MarkdownParseOptions.DisableHtml) reads HTML as text: the
renderer escapes it, and autolinks such as `<https://example.com>` are still links.

## Limitations of the prototype

- Inserting and removing blocks parses the whole text again.
- Parsing allocates more and is slower than [Meziantou.Framework.Markdown](../Markdown/01-Introduction.md).
