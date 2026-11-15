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

[DisableHtml](cref:Meziantou.Framework.Language.Markdown.MarkdownParseOptions.DisableHtml) reads HTML as text: the
renderer escapes it, and autolinks such as `<https://example.com>` are still links.

## Limitations of the prototype

- Not all the extensions of `Meziantou.Framework.Markdown` are supported yet: no grid tables, Markdig pipe tables, media links...
- Inserting and removing blocks parses the whole text again.
- Parsing allocates more and is slower than [Meziantou.Framework.Markdown](../Markdown/01-Introduction.md).
