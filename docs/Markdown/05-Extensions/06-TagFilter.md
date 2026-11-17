---
title: Tag filter
description: Write the HTML tags that can change the page, such as <script> or <iframe>, as text, as GitHub does.
---

Markdown can contain HTML, which the renderer writes as it is.
[TagFilter](cref:Meziantou.Framework.Markdown.MarkdownExtensions.TagFilter) is the tag filter of GitHub Flavored
Markdown: it escapes the `<` of the tags that can take over the page or break its layout, so that they are shown as
text. The other tags are written as they are.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlRenderer_TagFilter;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.TagFilter };
var tree = MarkdownSyntaxTree.ParseText("<script>alert(1)</script>\n\nSome <b>bold</b> text\n", options);

// The < of the filtered tags is escaped, the other tags are written as they are
Assert.Equal("&lt;script>alert(1)&lt;/script>\n<p>Some <b>bold</b> text</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

The filtered tags are `title`, `textarea`, `style`, `xmp`, `iframe`, `noembed`, `noframes`, `script` and `plaintext`, in
any case, opening or closing. The filter only changes the HTML that the renderer writes: the syntax tree keeps the HTML
as it is written.

The tag filter is not a sanitizer: attributes such as `onclick` and URLs such as `javascript:` are still written. To
render a document that is not trusted, see [Security](../07-Security.md).
