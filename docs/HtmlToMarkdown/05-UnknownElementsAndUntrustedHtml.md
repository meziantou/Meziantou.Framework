---
title: Unknown elements and untrusted HTML
description: Keep, remove or unwrap the elements that Markdown cannot write, and convert HTML from untrusted sources safely.
---

## Choose what happens to unknown elements

An element that the converter does not know, such as `sub`, `kbd`, `u` or a custom element, has no Markdown syntax.
[UnknownElementHandling](cref:Meziantou.Framework.UnknownElementHandling) chooses what it becomes:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_UnknownElements;format=md-fenced-code-block;dedent=true -->
```csharp
var html = "<p>Water is H<sub>2</sub>O</p>";

var passThrough = HtmlToMarkdown.Convert(html);
Assert.Equal("Water is H<sub>2</sub>O", passThrough);

var strip = HtmlToMarkdown.Convert(html, new HtmlToMarkdownOptions { UnknownElementHandling = UnknownElementHandling.Strip });
Assert.Equal("Water is HO", strip);

var keepContent = HtmlToMarkdown.Convert(html, new HtmlToMarkdownOptions { UnknownElementHandling = UnknownElementHandling.StripKeepContent });
Assert.Equal("Water is H2O", keepContent);
```
<!-- endref -->

| Value | Result |
| --- | --- |
| [PassThrough](cref:Meziantou.Framework.UnknownElementHandling.PassThrough) (default) | The element is written as raw HTML, with its attributes and its content. Markdown allows raw HTML, so a renderer that allows it displays the element. |
| [Strip](cref:Meziantou.Framework.UnknownElementHandling.Strip) | The element and its content are removed. |
| [StripKeepContent](cref:Meziantou.Framework.UnknownElementHandling.StripKeepContent) | The element is removed, and its content is converted. |

[Converted elements](03-ConvertedElements.md) lists the known elements. Whatever the option, `script`, `style` and
`noscript` are removed with their content, also inside an element written as raw HTML.

## Convert untrusted HTML

The converter is not a sanitizer. With [PassThrough](cref:Meziantou.Framework.UnknownElementHandling.PassThrough), an unknown element keeps its attributes: event handlers such as
`onclick` or `onerror`, and `javascript:` URLs, reach the Markdown. If the Markdown is rendered with raw HTML enabled,
they run in the page that shows it.

Sanitize the HTML before you convert it. [Document.ParseHtml](cref:Meziantou.Framework.WebPlatform.Dom.Document.ParseHtml) of
[Meziantou.Framework.WebPlatform](../WebPlatform/03-HtmlAndDom/06-SanitizingHtml.md), a dependency of the package,
parses the HTML and removes what can run scripts:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_UntrustedHtml;format=md-fenced-code-block;dedent=true -->
```csharp
var untrustedHtml = """<p>Hello <u onclick="steal()">world</u></p>""";

// With the default options, the unknown element is copied with its attributes
var unsafeMarkdown = HtmlToMarkdown.Convert(untrustedHtml);
Assert.Equal("""Hello <u onclick="steal()">world</u>""", unsafeMarkdown);

// Sanitize the HTML first: the event handlers, scripts and javascript: URLs are removed
var sanitizedHtml = Document.ParseHtml(untrustedHtml).Body!.InnerHtml;
var markdown = HtmlToMarkdown.Convert(sanitizedHtml);
Assert.Equal("Hello <u>world</u>", markdown);
```
<!-- endref -->

[Document](cref:Meziantou.Framework.WebPlatform.Dom.Document) is in the [Meziantou.Framework.WebPlatform.Dom](cref:Meziantou.Framework.WebPlatform.Dom) namespace. The other options are:

- Convert with [Strip](cref:Meziantou.Framework.UnknownElementHandling.Strip) or [StripKeepContent](cref:Meziantou.Framework.UnknownElementHandling.StripKeepContent): no raw HTML is written (other than the `<br>` of the table cells), so the
  Markdown holds no element and no attribute of the input. The links and the images are still written with their URLs as they are, `javascript:` included: the
  renderer must check their scheme.
- Sanitize the HTML with [Meziantou.Framework.HtmlSanitizer](../HtmlSanitizer/01-Introduction.md).
- Disable raw HTML, or sanitize the output, when you render the Markdown: see
  [Security](../Markdown/10-Security.md) for Meziantou.Framework.Markdown.
