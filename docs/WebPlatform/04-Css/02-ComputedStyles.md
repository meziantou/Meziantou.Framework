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
connected, or a document parsed by [HtmlDocumentParser](cref:Meziantou.Framework.WebPlatform.HtmlDocumentParser), the declarations are empty.

## The viewport and the preferences of the user

The media queries and the viewport units (`vw`, `vh`...) use the [ViewportOptions](cref:Meziantou.Framework.WebPlatform.Dom.ViewportOptions) of the browsing context: its size
(800x600 CSS pixels by default), its device pixel ratio, the media type (`screen` or `print`), and the preferences of the
user, such as [PreferredColorScheme](cref:Meziantou.Framework.WebPlatform.Dom.PreferredColorScheme), [PreferredContrast](cref:Meziantou.Framework.WebPlatform.Dom.PreferredContrast), [PrefersReducedMotion](cref:Meziantou.Framework.WebPlatform.Dom.ViewportOptions.PrefersReducedMotion) and the forced colors mode. Set
[BrowsingContext.Viewport](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContext.Viewport) to change them: the styles and the layout follow. See
[Viewport and devices](../05-LoadingPages/04-ViewportAndDevices.md).

`Window.MatchMedia(query)` evaluates a media query, as `matchMedia()` does in a browser.

## Mixins and private properties

The computed styles apply the mixins of [CSS Mixins](https://drafts.csswg.org/css-mixins-1/): a `@mixin` rule holds
declarations and nested rules, and an `@apply` rule inserts them in a style rule, with arguments and a contents block.
A `@private` rule declares custom properties that only its rule (and the rules nested in it) can reference: they are
not exposed by `GetPropertyValue`.

```css
@mixin --bordered(--color <color>: black) {
  @private { --width: 2px; }
  border: var(--width) solid var(--color);
  &:hover { @contents; }
}
button { @apply --bordered(red) { color: red; } }
```

- The body of a mixin is written like the body of a style rule: its declarations apply to the elements of the rule that
  applies it, and its nested rules (`&:hover`, `.icon`) are relative to that rule.
- `@contents` inserts the block of the `@apply` rule, or its own fallback block when the `@apply` rule has none.
- The private properties are experimental, like their proposal: [CssPrivateRule](cref:Meziantou.Framework.WebPlatform.Css.Om.CssPrivateRule) is marked with the `WEBPLATEXP0004`
  diagnostic.

## Typed values

[Element.ComputedStyleMap()](cref:Meziantou.Framework.WebPlatform.Dom.Element.ComputedStyleMap) returns the same values as objects (a length is a number and a unit, a keyword is a
[CssKeywordValue](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssKeywordValue)): see [Typed values](04-TypedValues.md).

## Animations and transitions

The CSS animations and transitions are part of the computed styles: their values follow the clock of the event loop of
the browsing context. With a fake `TimeProvider`, you choose the time at which you read them: see
[Time and the event loop](../05-LoadingPages/07-TimeAndTheEventLoop.md).
