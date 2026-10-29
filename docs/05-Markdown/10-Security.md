---
title: Security
description: Render Markdown written by your users safely: disable raw HTML, filter attributes, sanitize the output and limit the nesting depth.
---

Markdown is often written by users: comments, descriptions, messages. CommonMark lets the author write raw HTML and any
URL, so the HTML of such Markdown can run scripts in the pages of your site. Before you render untrusted Markdown,
apply all the measures below.

## Disable raw HTML

CommonMark copies raw HTML to the output unchanged. `DisableHtml` writes it as escaped text instead:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_DisableHtml;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = "<script>alert(1)</script>\n\nSome <b>bold</b> text";

Assert.Equal("<script>alert(1)</script>\n<p>Some <b>bold</b> text</p>\n", MarkdownConverter.ToHtml(markdown));

var pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();
Assert.Equal("<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>\n<p>Some &lt;b&gt;bold&lt;/b&gt; text</p>\n", MarkdownConverter.ToHtml(markdown, pipeline));
```
<!-- endref -->

## Sanitize the HTML

Links are not filtered: `[link](javascript:alert(1))` produces a `javascript:` URL, and `DisableHtml` does not change
that. Pass the generated HTML through an HTML sanitizer, such as `Meziantou.Framework.HtmlSanitizer`, to remove the
unsafe URLs and attributes.

## Keep the default attribute filter

[`UseGenericAttributes`](04-Extensions/06-GenericAttributes.md) lets the author add attributes. By default, it only
writes the identifier, the classes and the attributes that describe the content. `style`, `data-*` and the directive
attributes of client-side frameworks (`x-init`, `hx-get`…) can run a script or cover the page: do not allow them for
untrusted Markdown.

The default filter allows the general attributes that `Meziantou.Framework.HtmlSanitizer` allows, other than `class`:
`abbr`, `align`, `alt`, `axis`, `bgcolor`, `border`, `cellpadding`, `cellspacing`, `clear`, `color`, `cols`, `colspan`,
`compact`, `coords`, `datetime`, `decoding`, `dir`, `face`, `headers`, `height`, `hidden`, `hreflang`, `hspace`,
`ismap`, `lang`, `language`, `loading`, `nohref`, `nowrap`, `open`, `rel`, `rev`, `reversed`, `role`, `rows`,
`rowspan`, `rules`, `scope`, `scrolling`, `shape`, `size`, `span`, `start`, `summary`, `tabindex`, `target`, `title`,
`translate`, `type`, `valign`, `value`, `vspace`, `width` and `aria-*`. `{#id}` and `{.class}` are always written.

## Limit the nesting depth

A document nested too deeply, such as hundreds of `>` quote markers, could overflow the stack. The parser limits the
depth of the tree to `MaximumNestingDepth` (128 by default), and throws an `ArgumentException` beyond. Catch it when you
process untrusted input:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownPipelineBuilder_MaximumNestingDepth;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder { MaximumNestingDepth = 64 }
    .DisableHtml()
    .Build();

static string? RenderUntrusted(string markdown, MarkdownPipeline pipeline)
{
    try
    {
        return MarkdownConverter.ToHtml(markdown, pipeline);
    }
    catch (ArgumentException)
    {
        return null; // The document is nested too deeply
    }
}

Assert.Equal("<p>Hello</p>\n", RenderUntrusted("Hello", pipeline));
Assert.Null(RenderUntrusted(new string('>', 100) + " Hello", pipeline));
```
<!-- endref -->

Raising the limit accepts deeper documents, but can increase the cost of parsing and the stack used by the rendering.

## Other extensions to review

- [`UseMediaLinks`](04-Extensions/03-HeadingsAndLinks.md#embedded-media) embeds `<iframe>` players of other sites.
- [`UseAutoLinks`](04-Extensions/03-HeadingsAndLinks.md#automatic-links) links any URL of the text.
- [`UseReferralLinks`](04-Extensions/03-HeadingsAndLinks.md#rel-attributes) adds `nofollow`, `noopener` and
  `noreferrer` to the links of user content.
