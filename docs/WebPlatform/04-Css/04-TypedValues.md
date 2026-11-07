---
title: Typed values
description: "Read and write CSS values as objects with the CSS Typed OM: numbers with units, math expressions, keywords and transforms."
---

The [CSS Typed OM](https://drafts.css-houdini.org/css-typed-om-1/) exposes the values of the properties as objects
instead of strings, in the [Meziantou.Framework.WebPlatform.Css.TypedOm](cref:Meziantou.Framework.WebPlatform.Css.TypedOm) namespace. The factory functions of the `CSS`
namespace of the browsers (`CSS.px()`, `CSS.em()`...) are the static methods of the
[Meziantou.Framework.WebPlatform.Css.Om.Css](cref:Meziantou.Framework.WebPlatform.Css.Om.Css) class; the example below imports it as `CssFactory`:

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
| [CssStyleRule.StyleMap](cref:Meziantou.Framework.WebPlatform.Css.Om.CssStyleRule.StyleMap) | The declarations of a style rule, read and write |
| [Element.ComputedStyleMap()](cref:Meziantou.Framework.WebPlatform.Dom.Element.ComputedStyleMap) | The computed values of the element, read only (empty when the element is not rendered) |
| `CssStyleValue.Parse(property, text)`, `CssNumericValue.Parse(text)` | Values parsed from CSS text |

## Values

- **Numbers with units**: [CssUnitValue](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssUnitValue) has a [Value](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssUnitValue.Value) and a [Unit](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssUnitValue.Unit) (`"px"`, `"em"`, `"percent"`, `"number"`...). The
  math values ([CssMathSum](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssMathSum), [CssMathProduct](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssMathProduct), [CssMathMin](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssMathMin), [CssMathMax](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssMathMax), [CssMathClamp](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssMathClamp)...) represent `calc()` and
  its functions. [Add](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssNumericValue.Add), [Sub](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssNumericValue.Sub), [Mul](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssNumericValue.Mul), [Div](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssNumericValue.Div), [Min](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssNumericValue.Min) and [Max](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssNumericValue.Max) combine numeric values, and `To(unit)` converts a value to
  another unit when the conversion does not depend on the context (`1in` is `96px`, `1em` cannot be converted).
- **Keywords**: [CssKeywordValue](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssKeywordValue) (`auto`, `block`...).
- **`var()` references**: [CssUnparsedValue](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssUnparsedValue), a list of strings and [CssVariableReferenceValue](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssVariableReferenceValue) objects.
- **Transforms**: [CssTransformValue](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssTransformValue), a list of [CssTranslate](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssTranslate), [CssRotate](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssRotate), [CssScale](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssScale), [CssSkew](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssSkew), [CssPerspective](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssPerspective) and
  [CssMatrixComponent](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssMatrixComponent); [ToMatrix()](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssTransformComponent.ToMatrix) returns the matrix of absolute lengths.
- **Images**: [CssImageValue](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssImageValue), an opaque value.
- The other values, and the colors of the properties (as in the browsers), are [CssStyleValue](cref:Meziantou.Framework.WebPlatform.Css.TypedOm.CssStyleValue) objects that serialize to
  their CSS text.
