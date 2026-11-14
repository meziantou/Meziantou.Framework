---
title: Markdown
description: A prototype of a CommonMark parser whose tree keeps every character, so that a document is written back exactly and an edit only changes what it touches.
---

`Meziantou.Framework.Language.Markdown` parses CommonMark into a tree that keeps every character of the text, and
renders it as HTML. It is a prototype: it is not published, and its API is not final.

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

## Limitations of the prototype

- Only CommonMark: no extension (tables, footnotes, task lists...).
- No typed API: the nodes are read by their kind and the kinds of their tokens.
- The nodes cannot be inserted or removed in a list of blocks yet: replace a node or a token instead.
- Parsing allocates more and is slower than [Meziantou.Framework.Markdown](../Markdown/01-Introduction.md).
