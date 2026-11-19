---
title: Security
description: "Render the Markdown of your users safely: disable raw HTML, filter the URL schemes, limit the nesting, sanitize the HTML, and keep generic attributes for trusted authors."
---

Markdown is often written by users: comments, descriptions, messages. CommonMark lets the author write raw HTML and any
URL, so the HTML of such Markdown can run scripts in the pages of your site. Before you render untrusted Markdown,
apply the measures below. The renderer options reduce what the author can write, but only an HTML sanitizer makes the
HTML of untrusted Markdown safe: [sanitize the HTML](#sanitize-the-html) in every case.

## Disable raw HTML

The renderer copies raw HTML to the output unchanged.
[DisableHtml](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.DisableHtml) reads it as text, which the
renderer escapes. The URLs of the links are not filtered:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownParseOptions_DisableHtml;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = "<script>alert(1)</script>\n\nSome <b>bold</b> text and [a link](javascript:alert(1))";

Assert.Equal("<script>alert(1)</script>\n<p>Some <b>bold</b> text and <a href=\"javascript:alert(1)\">a link</a></p>\n", MarkdownConverter.ToHtml(markdown));

// The HTML is written as text, but the URLs of the links are not filtered
var options = new MarkdownParseOptions { DisableHtml = true };
Assert.Equal("<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>\n<p>Some &lt;b&gt;bold&lt;/b&gt; text and <a href=\"javascript:alert(1)\">a link</a></p>\n", MarkdownConverter.ToHtml(markdown, options));
```
<!-- endref -->

[TagFilter](cref:Meziantou.Framework.Markdown.MarkdownExtensions.TagFilter), an extension of GitHub Flavored
Markdown, escapes the tags that change how the rest of the page is read: `title`, `textarea`, `style`, `xmp`, `iframe`,
`noembed`, `noframes`, `script` and `plaintext`. It keeps the other tags and their attributes, such as
`<img onerror=...>`, and it can be bypassed: as in GitHub, it only recognizes a name followed by whitespace, `>` or
`/>`, while browsers also read `<script/src=...>` as a script. It is not a sanitizer:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlRenderer_TagFilterBypass;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.TagFilter };
var tree = MarkdownSyntaxTree.ParseText("<div>\n<script/src=https://example.com/x.js></script>\n</div>\n", options);

// As in GitHub, a / after the name is not filtered: browsers read <script/src=...> as a script
Assert.Equal("<div>\n<script/src=https://example.com/x.js>&lt;/script>\n</div>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

## Limit the nesting

A few characters can nest many blocks or inlines: each `>` of a line starts a block quote, and each `*` can open an
emphasis. A tree that deep would exhaust the stack of the code that walks it, the renderers and your visitors.
[MaximumNestingDepth](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.MaximumNestingDepth), 128 by default, is
the number of blocks that a block can be nested in, and of inlines that an inline can be nested in: a deeper block is
text of a paragraph, and a deeper inline is text, so parsing never fails:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownParseOptions_MaximumNestingDepth;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = new string('>', 10_000) + " text";

// The block quotes nested in more than 2 block quotes are text: the tree is 3 block quotes deep
var options = new MarkdownParseOptions { MaximumNestingDepth = 2 };
var tree = MarkdownSyntaxTree.ParseText(markdown, options);
Assert.HasCount(3, tree.GetRoot().DescendantNodes().OfType<MarkdownBlockQuoteSyntax>());
Assert.Equal(markdown, tree.GetRoot().ToFullString());
```
<!-- endref -->

## Filter the URL schemes

`[link](javascript:alert(1))` produces a `javascript:` URL, with or without raw HTML.
[AllowedUrlSchemes](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.AllowedUrlSchemes) lists the
schemes that the URLs of the links and of the images can have. A URL with another scheme is written empty, and a
relative URL is kept:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlRendererOptions_AllowedUrlSchemes;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = MarkdownSyntaxTree.ParseText("[a](javascript:alert(1)) [b](https://example.com) [c](/relative) ![d](data:image/png;base64,AA==) <vbscript:x>\n");
var options = new MarkdownHtmlRendererOptions { AllowedUrlSchemes = ["http", "https", "mailto"] };

// The URLs whose scheme is not allowed are written empty, the relative URLs are kept
Assert.Equal("<p><a href=\"\">a</a> <a href=\"https://example.com\">b</a> <a href=\"/relative\">c</a> <img src=\"\" alt=\"d\" /> <a href=\"\">vbscript:x</a></p>\n", MarkdownHtmlRenderer.ToHtml(tree, options));
```
<!-- endref -->

The scheme is compared ignoring case, and read as a browser reads it: the spaces and the control characters at the
start of the URL, and its tabs and line endings, are ignored. The filter applies to every URL that the renderer
writes: links, images, autolinks, media links, and the URLs that node renderers write with
[WriteUrl](cref:Meziantou.Framework.Markdown.MarkdownHtmlWriter.WriteUrl). It does not apply to raw HTML, nor to
the generic attributes that your [AttributeFilter](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.AttributeFilter)
allows. It complements the sanitizer, it does not replace it.

## Sanitize the HTML

Pass the HTML of untrusted Markdown through an HTML sanitizer, such as
[Meziantou.Framework.HtmlSanitizer](../HtmlSanitizer/01-Introduction.md), to remove the unsafe elements, URLs and
attributes. The sanitizer never adds attributes: set
[LinkRel](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.LinkRel), such as `nofollow ugc`, on the
renderer.

## Keep generic attributes for trusted authors

[GenericAttributes](cref:Meziantou.Framework.Markdown.MarkdownExtensions.GenericAttributes) lets the author add
attributes, such as `{#id .class title="text"}`. It is meant for trusted authors, such as the authors of your
documentation: do not enable it for untrusted Markdown, or sanitize the HTML.

By default, [AttributeFilter](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.AttributeFilter) only
writes the identifier, the classes and the attributes that describe the content, such as `title`, `lang` or `aria-*`.
Event handlers, URL attributes, `style`, `data-*` and the directive attributes of client-side frameworks (`x-init`,
`hx-get`...) can run a script or cover the page: do not allow them. An element has one attribute of each name: the
attributes that the renderer writes, such as the `alt` of an image, the `title` of a link, or the `rel` of
[LinkRel](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.LinkRel), cannot be written again by generic
attributes, and a generic attribute written twice keeps its first value.

The filter and the sanitizer still let some attributes through. With the default policy of the sanitizer:

- **Classes.** The value of `class` is not checked: `{.fixed-top .modal}` uses the classes of your stylesheets to cover
  or restyle the page.
- **`target` and `rel`.** Both are allowed: `{target=_blank}` opens a link in a new window, and, when the renderer
  writes no `rel`, `{rel=opener}` gives the opened page access to yours. Set
  [LinkRel](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.LinkRel), which generic attributes cannot
  replace.
- **Identifiers.** The sanitizer removes `id` by default. If you allow it, an identifier such as `{#__proto__}` or the
  name of a global variable can be confused with the objects of your scripts.
