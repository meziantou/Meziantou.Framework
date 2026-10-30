---
title: Typed values
description: Read and write CSS values as objects with the CSS Typed OM: numbers with units, math expressions, keywords and transforms.
---

The [CSS Typed OM](https://drafts.css-houdini.org/css-typed-om-1/) exposes the values of the properties as objects
instead of strings, in the `Meziantou.Framework.WebPlatform.Css.TypedOm` namespace. The factory functions of the `CSS`
namespace of the browsers (`CSS.px()`, `CSS.em()`...) are the static methods of the
`Meziantou.Framework.WebPlatform.Css.Om.Css` class; the example below imports it as `CssFactory`:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Css/TypedOmTests.cs;region=WebPlatform_TypedOm;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext();
var document = context.Navigate("https://example.com/", _ => "<div style='font-size: 16px; height: 2em'></div>");
var element = (HtmlElement)document.QuerySelector("div")!;

// Set a value as an object: the style attribute is updated
element.AttributeStyleMap.Set("width", CssFactory.Px(10).Add(CssFactory.Em(2)));
Assert.Equal("font-size: 16px; height: 2em; width: calc(2em + 10px);", element.GetAttribute("style"));

// Read the computed values as numbers and units
var height = Assert.IsType<CssUnitValue>(element.ComputedStyleMap().Get("height"));
Assert.Equal((32, "px"), (height.Value, height.Unit));

// Parse and convert values
var length = CssNumericValue.Parse("calc(1in + 4px)").To("px");
Assert.Equal(100, length.Value);
```
<!-- endref -->

| Member | Values |
| --- | --- |
| `Element.AttributeStyleMap` | The declarations of the `style` attribute, read and write: setting a value updates the attribute |
| `CssStyleRule.StyleMap` | The declarations of a style rule, read and write |
| `Element.ComputedStyleMap()` | The computed values of the element, read only (empty when the element is not rendered) |
| `CssStyleValue.Parse(property, text)`, `CssNumericValue.Parse(text)` | Values parsed from CSS text |

## Values

- **Numbers with units**: `CssUnitValue` has a `Value` and a `Unit` (`"px"`, `"em"`, `"percent"`, `"number"`...). The
  math values (`CssMathSum`, `CssMathProduct`, `CssMathMin`, `CssMathMax`, `CssMathClamp`...) represent `calc()` and
  its functions. `Add`, `Sub`, `Mul`, `Div`, `Min` and `Max` combine numeric values, and `To(unit)` converts a value to
  another unit when the conversion does not depend on the context (`1in` is `96px`, `1em` cannot be converted).
- **Keywords**: `CssKeywordValue` (`auto`, `block`...).
- **`var()` references**: `CssUnparsedValue`, a list of strings and `CssVariableReferenceValue` objects.
- **Transforms**: `CssTransformValue`, a list of `CssTranslate`, `CssRotate`, `CssScale`, `CssSkew`, `CssPerspective` and
  `CssMatrixComponent`; `ToMatrix()` returns the matrix of absolute lengths.
- **Images**: `CssImageValue`, an opaque value.
- The other values, and the colors of the properties (as in the browsers), are `CssStyleValue` objects that serialize to
  their CSS text.
