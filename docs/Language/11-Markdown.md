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

Each node of the tree is a [MarkdownSyntaxNode](cref:Meziantou.Framework.Language.Markdown.MarkdownSyntaxNode) whose
[Kind](cref:Meziantou.Framework.Language.Markdown.MarkdownSyntaxNode.Kind) is a block (a paragraph, a block quote, a list
item...) or an inline (an emphasis, a link...). The tokens are what the constructs are made of: the marker that opens
a block quote, the backticks of a code span, the brackets and the destination of a link, the delimiters of an
emphasis.

The characters that no token covers are trivia, as in the other languages: the indentation, the line endings that end
blocks, and the markers of the containers on their continuation lines. In `> a` followed by `> b`, the first `>` is a
token of the block quote, and the second one is a
[BlockQuoteMarkerTrivia](cref:Meziantou.Framework.Language.Markdown.SyntaxKind.BlockQuoteMarkerTrivia) before `b`. The
line endings in a paragraph are tokens, because they are part of its text: a soft or a hard line break.

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

## Limitations of the prototype

- Only CommonMark and the extensions of GitHub Flavored Markdown: no footnotes, front matter, math...
- No typed API: the nodes are read by their kind and the kinds of their tokens.
- The nodes cannot be inserted or removed in a list of blocks yet: replace a node or a token instead.
- Parsing allocates more and is slower than [Meziantou.Framework.Markdown](../Markdown/01-Introduction.md).
