---
title: Scheme autolinks
description: Turn the URLs of the text into links, for http, https, ftp, mailto, tel and www addresses.
---

[MarkdigAutolinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.MarkdigAutolinks) turns the URLs of the text
into links, as the [extended autolinks](05-Autolinks.md) of GFM do, which it replaces when both are enabled, with more
schemes and options: a URL that starts with `http://`, `https://`, `ftp://`, `mailto:`, `tel:` or `www.` is a link.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_MarkdigAutolinks;format=md-fenced-code-block;dedent=true -->
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

## Details

- A URL is a link after whitespace or one of `*_~(`, up to whitespace or trailing punctuation, when its parentheses are
  balanced and its domain is valid.
- It is not a link in a link, nor after an `<a>` that is not closed, and the characters of the delimiters of emphasis
  around it are not part of it.
- It is a [MarkdownAutolinkSyntax](cref:Meziantou.Framework.Markdown.MarkdownAutolinkSyntax), whose
  [IsMarkdigAutolink](cref:Meziantou.Framework.Markdown.MarkdownAutolinkSyntax.IsMarkdigAutolink) is `true`.

## Options

[AutolinkOptions](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.AutolinkOptions) chooses the characters before
a link, `https://` for `www.`, the domains without a period, and `target="_blank"`.
