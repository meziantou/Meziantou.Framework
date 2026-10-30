---
title: CSS
description: Read the computed styles of the elements, inspect and edit style sheets, work with typed values and format CSS.
---

The library parses CSS as a browser does and applies it with the cascade of the browsers: the `style` elements, the
linked style sheets, the `style` attributes, the user agent style sheet, the media queries, `@layer`, `@container`,
`@scope`, the nesting and the custom properties. The results are exposed by the CSS Object Model (CSSOM) of the browsers.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/BrowsingContextTests.cs;region=WebPlatform_ComputedStyle;format=md-fenced-code-block;dedent=true -->
```csharp
var html = """
    <style>.important { color: red; font-weight: bold }</style>
    <p class="important">Hello</p>
    """;

using var context = new BrowsingContext(new BrowsingContextOptions
{
    Viewport = new ViewportOptions { Width = 1280, Height = 720 }, // optional, 800x600 by default
});

var document = context.Navigate("https://example.com/", _ => html);
var style = context.Window.GetComputedStyle(document.QuerySelector(".important")!);
Assert.Equal("rgb(255, 0, 0)", style.Color);
Assert.Equal("700", style.GetPropertyValue("font-weight"));
```
<!-- endref -->

## What needs a browsing context

The style sheets and the `style` attributes are parsed in every document, so you can read and edit them in a document
parsed by [HtmlDocumentParser](cref:Meziantou.Framework.WebPlatform.HtmlDocumentParser). The cascade needs a window and a viewport: the computed styles, the media queries and the
layout only exist in the documents of a [browsing context](../05-LoadingPages/01-Overview.md). The style sheets of the
`<link rel=stylesheet>` elements and of the `@import` rules are fetched by the document loader of the browsing context.

## In this section

- [Computed styles](02-ComputedStyles.md): the final value of every property of an element, after the cascade and the
  layout.
- [Style sheets](03-StyleSheets.md): read, edit and create style sheets and rules; inspect their selectors.
- [Typed values](04-TypedValues.md): read and write CSS values as numbers with units, keywords and transforms.
- [Formatting CSS](05-FormattingCss.md): write style sheets minified or indented.
