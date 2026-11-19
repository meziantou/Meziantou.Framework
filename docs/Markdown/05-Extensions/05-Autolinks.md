---
title: Extended autolinks
description: Turn web addresses and email addresses of the text into links, without angle brackets, as GitHub does.
---

CommonMark only makes a link of an address between angle brackets, such as `<https://example.com>`.
[Autolinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Autolinks) adds the extended autolinks of GitHub
Flavored Markdown, so that the addresses people type are links: an address that starts with `www.`, `http://` or
`https://`, and an email address. An address that starts with `www.` links to `http://`. An email address can start
with `mailto:`, and a Jabber address with `xmpp:`, which can be followed by a resource after a `/`, as in
`xmpp:me@example.com/home`: the scheme is part of the link, and the address is its own destination.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Autolinks;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Autolinks };
var tree = MarkdownSyntaxTree.ParseText("See www.example.com, https://example.com/docs or contact@example.com.\n", options);

var links = tree.GetRoot().DescendantNodes().OfType<MarkdownAutolinkSyntax>().ToList();
Assert.Equal(["http://www.example.com", "https://example.com/docs", "mailto:contact@example.com"], links.Select(link => link.Destination));
Assert.Equal("<p>See <a href=\"http://www.example.com\">www.example.com</a>, <a href=\"https://example.com/docs\">https://example.com/docs</a> or <a href=\"mailto:contact@example.com\">contact@example.com</a>.</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

An extended autolink is a [MarkdownAutolinkSyntax](cref:Meziantou.Framework.Markdown.MarkdownAutolinkSyntax) that holds
the tokens of its text: [Destination](cref:Meziantou.Framework.Markdown.MarkdownAutolinkSyntax.Destination) gives the
URL, and [IsExtended](cref:Meziantou.Framework.Markdown.MarkdownAutolinkSyntax.IsExtended) tells it from an autolink
between angle brackets. The trailing punctuation, such as the period that ends a sentence, is not part of the link.

[Scheme autolinks](29-MarkdigAutolinks.md) recognize more schemes, and replace these autolinks when both are enabled.
