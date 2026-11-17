---
title: Security
description: "Render the Markdown of your users safely: disable raw HTML, keep the attribute filter, filter the tags of GitHub, limit the nesting and sanitize the HTML."
---

Markdown is often written by users: comments, descriptions, messages. CommonMark lets the author write raw HTML and any
URL, so the HTML of such Markdown can run scripts in the pages of your site. Before you render untrusted Markdown,
apply the measures below.

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
`noembed`, `noframes`, `script` and `plaintext`. It keeps the other tags and their attributes: it is not a sanitizer.

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

## Sanitize the HTML

`[link](javascript:alert(1))` produces a `javascript:` URL, with or without raw HTML. Pass the generated HTML through
an HTML sanitizer, such as [Meziantou.Framework.HtmlSanitizer](../HtmlSanitizer/01-Introduction.md), to remove the
unsafe URLs and attributes.

## Keep the default attribute filter

[GenericAttributes](cref:Meziantou.Framework.Markdown.MarkdownExtensions.GenericAttributes) lets the author add
attributes, such as `{#id .class title="text"}`. By default,
[AttributeFilter](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.AttributeFilter) only writes the
identifier, the classes and the attributes that describe the content, such as `title`, `lang` or `aria-*`. Event
handlers, URL attributes, `style`, `data-*` and the directive attributes of client-side frameworks (`x-init`,
`hx-get`...) can run a script or cover the page: do not allow them for untrusted Markdown.
