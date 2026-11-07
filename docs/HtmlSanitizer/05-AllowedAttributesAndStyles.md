---
title: Allowed attributes and styles
description: Allow attributes by name or by prefix, keep event handlers and identifiers out, and format content without CSS.
---

An attribute is kept when its name is in [ValidAttributes](cref:Meziantou.Framework.Sanitizers.HtmlSanitizer.ValidAttributes) or starts with one of the prefixes of
[ValidAttributePrefixes](cref:Meziantou.Framework.Sanitizers.HtmlSanitizer.ValidAttributePrefixes); every other attribute is removed. Names and prefixes are compared without regard to case. The
sets apply to every element: there is no list of attributes per element.

## Allow or remove attributes

Add or remove names in [ValidAttributes](cref:Meziantou.Framework.Sanitizers.HtmlSanitizer.ValidAttributes):

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_CustomAttributes;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

// Add custom attributes
sanitizer.ValidAttributes.Add("data-custom");

// Remove attributes from allowed list
sanitizer.ValidAttributes.Remove("target");

// Add attributes that should be URL-validated
sanitizer.UriAttributes.Add("data-url");

var result = sanitizer.SanitizeHtmlFragment("<a data-custom='value' target='_blank' href='https://example.com'>link</a>");
Assert.Equal("<a data-custom=\"value\" href=\"https://example.com\">link</a>", result);
```
<!-- endref -->

Adding a name to [ValidAttributes](cref:Meziantou.Framework.Sanitizers.HtmlSanitizer.ValidAttributes) keeps its value as it is. When the attribute holds a URL, add it to [UriAttributes](cref:Meziantou.Framework.Sanitizers.HtmlSanitizer.UriAttributes)
too, so its value is validated. See [URLs](06-Urls.md#validate-your-own-url-attributes).

## Allow attributes by prefix

[ValidAttributePrefixes](cref:Meziantou.Framework.Sanitizers.HtmlSanitizer.ValidAttributePrefixes) allows every attribute whose name starts with a prefix. It contains `aria-` by default:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_AttributePrefixes;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

// Allow every data-* attribute
sanitizer.ValidAttributePrefixes.Add("data-");

// Remove the ARIA attributes
sanitizer.ValidAttributePrefixes.Remove("aria-");

var result = sanitizer.SanitizeHtmlFragment("<p data-id='1' aria-label='text'>test</p>");
Assert.Equal("<p data-id=\"1\">test</p>", result);
```
<!-- endref -->

An attribute allowed by a prefix is URL-validated only when its name is in [UriAttributes](cref:Meziantou.Framework.Sanitizers.HtmlSanitizer.UriAttributes). Frameworks such as htmx or
Knockout run the value of some `data-*` attributes (`data-hx-on:click`, `data-bind`), so only allow `data-` when your
pages do not use such a framework.

## Attributes to keep out

- **Event handlers** (`onclick`, `onerror`, `onload`, …) run script. Never allow them, or a prefix such as `on`.
- **`id` and `name`** are removed by default. An element with an `id` or a `name` can shadow the global variables and
  the properties of `document` that the scripts of your page read (DOM clobbering), and two elements can end up with the
  same `id`. Allow them only when no script of the page can be confused by them.
- **`is`** is removed like any other attribute that is not allowed, and the element is recreated without it: otherwise
  it would still be a customized built-in element, which a browser upgrades with the custom element of the page that
  has this name.

## Styles

The library does not sanitize CSS. By default the `style` attribute is removed and the `style` element is removed with
its content; the `style` element can never be allowed (see
[Allowed elements](04-AllowedElements.md#elements-that-cannot-be-allowed)).

Adding `style` to [ValidAttributes](cref:Meziantou.Framework.Sanitizers.HtmlSanitizer.ValidAttributes) keeps the CSS exactly as it was written:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_StyleIsNotSanitized;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();
sanitizer.ValidAttributes.Add("style");

// The CSS is kept as is, including the URL that it loads
var result = sanitizer.SanitizeHtmlFragment("<p style='background:url(https://tracker.example/pixel)'>x</p>");
Assert.Equal("""<p style="background:url(https://tracker.example/pixel)">x</p>""", result);
```
<!-- endref -->

CSS that you did not write can load resources from any site (and so tell it who reads the page), cover your page with
its own content, or hide your page. Keep `style` out of the allowed attributes and let authors format their content
with elements instead:

- **Formatting elements** such as `b`, `em`, `code`, `sub`, `h1`–`h6` or `table` are allowed by default.
- **`class`** is allowed by default. Its value is not checked, so the content can use any class of your stylesheets.
  Check that none of them lets the content pass for part of your page, for instance with fixed positioning, or remove
  `class` from [ValidAttributes](cref:Meziantou.Framework.Sanitizers.HtmlSanitizer.ValidAttributes).
- **Presentational attributes** such as `align`, `bgcolor`, `color`, `face`, `width` and `height` are allowed by
  default.
