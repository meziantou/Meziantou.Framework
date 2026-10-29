---
title: Computed styles
description: Read the value of every CSS property of an element after the cascade, the inheritance, the media queries and the layout.
---

`Window.GetComputedStyle(element)` returns the values of the properties of an element, as `getComputedStyle()` does in a
browser:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Css/ComputedStyleTests.cs;region=WebPlatform_ComputedStyleViewport;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    Viewport = new ViewportOptions { Width = 1280, Height = 720 },
});

var document = context.Navigate("https://example.com/", _ => """
    <!DOCTYPE html>
    <style>
      body { margin: 0 }
      nav { display: flex; width: 50% }
      nav::before { content: "Menu" }
      @media (max-width: 600px) { nav { display: none } }
    </style>
    <nav></nav>
    """);

var nav = document.QuerySelector("nav")!;
var style = context.Window.GetComputedStyle(nav);
Assert.Equal("flex", style.Display);
Assert.Equal("640px", style.Width); // a used value, computed by the layout
Assert.Equal("\"Menu\"", context.Window.GetComputedStyle(nav, "::before").GetPropertyValue("content"));

// The styles follow the viewport: the media queries and the layout use its new size
context.Viewport = new ViewportOptions { Width = 400, Height = 800 };
Assert.Equal("none", context.Window.GetComputedStyle(nav).Display);
```
<!-- endref -->

- The declarations are read-only and live: they follow the changes of the document, of its style sheets and of the
  viewport.
- `GetPropertyValue(name)` reads any property, including the custom properties (`--brand-color`); the common properties
  also have their own members (`Color`, `Display`, `Width`...).
- The values are serialized as in a browser: the colors as `rgb()`, the lengths in `px`, the font weights as numbers.
- The values that depend on the layout (`width`, `height`, the margins, the positions...) are the used values computed by
  the layout engine, as in a browser: `width: 50%` reads as a length in pixels.
- `GetComputedStyle(element, "::before")` reads the styles of a pseudo-element.

The computed styles only exist for the elements of a document loaded in a browsing context. For an element that is not
connected, or a document parsed by `HtmlDocumentParser`, the declarations are empty.

## The viewport and the preferences of the user

The media queries and the viewport units (`vw`, `vh`...) use the `ViewportOptions` of the browsing context: its size
(800x600 CSS pixels by default), its device pixel ratio, the media type (`screen` or `print`), and the preferences of the
user, such as `PreferredColorScheme`, `PreferredContrast`, `PrefersReducedMotion` and the forced colors mode. Set
`BrowsingContext.Viewport` to change them: the styles and the layout follow. See
[Viewport and devices](../05-LoadingPages/04-ViewportAndDevices.md).

`Window.MatchMedia(query)` evaluates a media query, as `matchMedia()` does in a browser.

## Typed values

`Element.ComputedStyleMap()` returns the same values as objects (a length is a number and a unit, a keyword is a
`CssKeywordValue`): see [Typed values](04-TypedValues.md).

## Animations and transitions

The CSS animations and transitions are part of the computed styles: their values follow the clock of the event loop of
the browsing context. With a fake `TimeProvider`, you choose the time at which you read them: see
[Time and the event loop](../05-LoadingPages/07-TimeAndTheEventLoop.md).
