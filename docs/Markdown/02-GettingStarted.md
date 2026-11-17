---
title: Getting started
description: Install the package, convert Markdown to HTML, enable extensions and parse a document into a syntax tree.
---

## Install the package

Add the `Meziantou.Framework.Markdown` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.Markdown" />

The types are in the `Meziantou.Framework.Markdown` namespace. The tokens, the trivia and the spans of the tree are in
the `Meziantou.Framework.Language` namespace.

## Convert Markdown

[MarkdownConverter](cref:Meziantou.Framework.Markdown.MarkdownConverter) converts text to HTML or to plain text in
one call. Without options, it reads CommonMark; [MarkdownParseOptions](cref:Meziantou.Framework.Markdown.MarkdownParseOptions)
enables the extensions:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownConverter_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal("<p>Some <strong>bold</strong> text</p>\n", MarkdownConverter.ToHtml("Some **bold** text"));
Assert.Equal("<p><del>Old</del> new</p>\n", MarkdownConverter.ToHtml("~~Old~~ new", MarkdownParseOptions.GitHubFlavored));
Assert.Equal("Some bold text\n", MarkdownConverter.ToPlainText("Some **bold** text"));
```
<!-- endref -->

## Parse a document

[MarkdownSyntaxTree.ParseText](cref:Meziantou.Framework.Markdown.MarkdownSyntaxTree.ParseText(System.String,Meziantou.Framework.Markdown.MarkdownParseOptions,System.String))
parses a document into a syntax tree, and
[MarkdownHtmlRenderer.ToHtml](cref:Meziantou.Framework.Markdown.MarkdownHtmlRenderer.ToHtml(Meziantou.Framework.Markdown.MarkdownSyntaxTree))
renders the tree. Parse the document once to render it and to read it:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Overview;format=md-fenced-code-block;dedent=true -->
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

The characters that no token covers are [trivia](../Language/03-Concepts/03-Trivia.md), as in the other languages of
`Meziantou.Framework.Language`: the indentation, the line endings that end blocks, and the markers of the containers on
their continuation lines. In `> a` followed by `> b`, the first `>` is a token of the block quote, and the second one is
a [BlockQuoteMarkerTrivia](cref:Meziantou.Framework.Markdown.SyntaxKind.BlockQuoteMarkerTrivia) before `b`. The line
endings in a paragraph are tokens, because they are part of its text: a soft or a hard line break.

The parser never throws on its input: any text is a document, and the tree reproduces it exactly.
