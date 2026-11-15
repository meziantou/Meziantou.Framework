---
title: Rendering
description: Render a document as HTML or as plain text, and change the URLs, the attributes and the ids of the elements.
---

## Render HTML

[MarkdownHtmlRenderer.ToHtml](cref:Meziantou.Framework.Markdown.MarkdownHtmlRenderer.ToHtml(Meziantou.Framework.Markdown.MarkdownSyntaxTree))
renders the HTML of the CommonMark reference implementation. The parser follows CommonMark 0.31.2, and renders the
HTML of every example of the specification.

[MarkdownHtmlRendererOptions](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions) changes the HTML of
the links: [BaseUrl](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.BaseUrl) makes the relative
URLs absolute, [LinkRewriter](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.LinkRewriter) changes
the URLs, [LinkRel](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.LinkRel) sets the `rel`
attribute of the links, such as `nofollow`, and
[EscapeNonAsciiUrlCharacters](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.EscapeNonAsciiUrlCharacters)
keeps the characters of the URLs that are not ASCII:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlRenderer_LinkOptions;format=md-fenced-code-block;dedent=true -->
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

[PragmaLines](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.PragmaLines) gives the element of
the first block of each line the id `pragma-line-N`, where N is the number of the line, for an editor to scroll its
preview to a line.
[FindClosestLine](cref:Meziantou.Framework.Markdown.MarkdownDocumentSyntax.FindClosestLine(System.Int32)) finds
the line of the block that is the closest to a line:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlRenderer_PragmaLines;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = MarkdownSyntaxTree.ParseText("# Title\n\nSome text.\n\n- First\n- Second\n");

Assert.Equal("<h1 id=\"pragma-line-0\">Title</h1>\n<p id=\"pragma-line-2\">Some text.</p>\n<ul id=\"pragma-line-4\">\n<li>First</li>\n<li id=\"pragma-line-5\">Second</li>\n</ul>\n", MarkdownHtmlRenderer.ToHtml(tree, new MarkdownHtmlRendererOptions { PragmaLines = true }));

// The editor shows line 3, a blank line: scroll the preview to the closest element
Assert.Equal(4, tree.GetRoot().FindClosestLine(3));
```
<!-- endref -->

## Render plain text

[MarkdownPlainTextRenderer.ToPlainText](cref:Meziantou.Framework.Markdown.MarkdownPlainTextRenderer.ToPlainText(Meziantou.Framework.Markdown.MarkdownSyntaxTree))
writes the text of the document, as `MarkdownConverter.ToPlainText` of `Meziantou.Framework.Markdown` 1.x does: the text of
the HTML without its tags, not escaped. Each block ends its line, HTML is not written, an image is its text, a footnote
reference is its number, a task is `[x]` or `[ ]`, and the cells of a table are followed by a space. Unlike 1.x, the text
after a table starts on a new line, the characters of SmartyPants are not entities, and an alert has no title.

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownPlainTextRenderer_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = MarkdownSyntaxTree.ParseText("# Title\n\nSome **bold** text and [a link](https://example.com).\n\n- First\n- Second");

Assert.Equal("Title\nSome bold text and a link.\nFirst\nSecond\n", MarkdownPlainTextRenderer.ToPlainText(tree));
```
<!-- endref -->

