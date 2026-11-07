# CSS Typed OM

The [CSS Typed OM](https://drafts.css-houdini.org/css-typed-om-1/) exposes the values of the CSS properties as objects
(`CssStyleValue` and its subclasses, in the `Meziantou.Framework.WebPlatform.Css.TypedOm` namespace) instead of strings:

- `Element.AttributeStyleMap` (`attributeStyleMap`) and `CssStyleRule.StyleMap` (`styleMap`) are `StylePropertyMap`
  objects: the declarations of the `style` attribute and of a style rule. They are live, and setting a value updates the
  declarations (and the `style` attribute).
- `Element.ComputedStyleMap()` (`computedStyleMap()`) is a `StylePropertyMapReadOnly`: the computed values of the element
  (see [computed-style.md](computed-style.md)). It is live, and empty when the element is not rendered (e.g. not connected).
- `CssStyleValue.Parse(property, text)` and `ParseAll()` parse a value of a property, `CssNumericValue.Parse(text)` a
  numeric value, and `CssColorValue.Parse(text)` a color.

```csharp
using Meziantou.Framework.WebPlatform.Css.TypedOm;
using CSS = Meziantou.Framework.WebPlatform.Css.Om.Css;

var element = (HtmlElement)document.QuerySelector("div")!;
element.AttributeStyleMap.Set("width", CSS.Px(10).Add(CSS.Em(2)));    // style="width: calc(10px + 2em);"
element.AttributeStyleMap.Append("transition-duration", CSS.S(1), "200ms");

var width = (CssMathSum)element.AttributeStyleMap.Get("width")!;      // CSSMathSum(10px, 2em)
var height = element.ComputedStyleMap().Get("height");                // CSSUnitValue in px, or CSSKeywordValue("auto")
var value = CssNumericValue.Parse("calc(1in + 4px)").To("px");        // 100px
```

## Values

- **Numeric values**: `CssUnitValue` (a number with a unit: `"number"`, `"percent"` or the ASCII lowercase unit of a
  dimension, e.g. `"px"`, `"q"`, `"hz"`), and the math values `CssMathSum`, `CssMathProduct`, `CssMathNegate`,
  `CssMathInvert`, `CssMathMin`, `CssMathMax` and `CssMathClamp`. The arithmetic (`Add`, `Sub`, `Mul`, `Div`, `Min`,
  `Max`), the conversions (`To`, `ToSum`), the equality (`NumericEquals`, the `equals()` method of the IDL) and the types
  (`Type()`, with the percent hints) follow the specification. The `Css` class has the factory functions (`CSS.px()`,
  `CSS.em()`, `CSS.cqi()`...).
- **Keywords**: `CssKeywordValue`, serialized as an identifier (e.g. `CSS.escape()` of the value).
- **var() references**: `CssUnparsedValue`, a list of strings and of `CssVariableReferenceValue` objects. A value that
  contains `var()` (or another substitution function) and the values of the unregistered custom properties are unparsed
  values, whatever the property.
- **Registered custom properties**: in a computed style map (and in the style maps of a
  [paint callback](rendering.md#paint-worklet-images) and of the callbacks and children of a
  [layout class](layout.md#layout-api-containers-csslayoutlayoutapi-csslayoutapi)), the computed value of a custom property registered with a syntax
  other than `*` is reified with the component of the syntax it matches: a numeric value for `<length>`, `<number>`,
  `<percentage>`, `<length-percentage>`, `<integer>`, `<angle>`, `<time>` and `<resolution>`, a `CssKeywordValue` for an
  identifier and `<custom-ident>`, a `CssImageValue` for a URL of `<image>`, a `CssTransformValue` for
  `<transform-function>` and `<transform-list>` (the other images, the colors and the URLs are `CssStyleValue` objects,
  as in the browsers), and one value per item of a list (`+` or `#`).
- **Transforms**: `CssTransformValue`, a list of transform components (`CssTranslate`, `CssRotate`, `CssScale`, `CssSkew`,
  `CssSkewX`, `CssSkewY`, `CssPerspective`, `CssMatrixComponent`). `ToMatrix()` returns the `DomMatrix` of a value or of a
  component; the lengths must be absolute (a relative length or a percentage throws a `TypeError`).
- **Images**: `CssImageValue`, an opaque value (`url()`, gradients, `image-set()`...).
- **Colors**: the color values (`CssRgb`, `CssHsl`, `CssHwb`, `CssLab`, `CssLch`, `CssOkLab`, `CssOkLch` and `CssColor` for
  `color()`) are created by their constructors and by `CssColorValue.Parse()`. Like in the browsers, the colors of the
  properties are not reified as color values: they are direct `CssStyleValue` objects, except `currentcolor` and the
  system colors (keywords).
- The other values are direct `CssStyleValue` objects: they are opaque, serialize to the CSS text of the value, and can
  only be set to the property they were read from (a `TypeError` is thrown otherwise).

## Reification

The [property-specific rules](https://drafts.css-houdini.org/css-typed-om-1/#reify-stylevalue) of the specification are
derived from the grammar of the properties, and are close to the behavior of the browsers:

- The declared maps reify the keywords and the numeric values from the value as specified, not from its serialization,
  which does not keep the structure of every value: `opacity: 50%` (serialized `0.5`) is `CSSUnitValue(50, "percent")`,
  and `animation-timing-function: step-start` (serialized `steps(1, start)`) is a keyword. The other values are reified
  from the serialized value.
- The CSS-wide keywords are keywords. A single keyword of the grammar (e.g. `auto`, `none`, `block`) is a keyword, and so
  is a `<custom-ident>` that is the whole value (e.g. `animation-name: slide`, but not `counter-reset: chapter`).
- A number, a percentage or a dimension is a `CssUnitValue` (a unitless zero is `0px` when the property does not accept
  numbers). A math function (`calc()`, `min()`, `max()`, `clamp()`) is a math value: it is simplified as a specified value
  first (e.g. `calc(1px + 1in)` is `CSSMathSum(97px)`). The other math functions (e.g. `round()`, `sign()`) are not
  represented: the value is a direct `CssStyleValue` (and `CssNumericValue.Parse()` throws a `SyntaxError`).
- The computed values use the canonical units (`px`, `deg`, `s`, `hz`, `dppx`), and a math function that computes to a
  single value is a `CssUnitValue`. The computed values that always have several components (e.g.
  `border-image-outset`, `border-image-repeat`, the pair of radii of `border-top-left-radius`) are direct `CssStyleValue`
  objects; `text-overflow`, whose computed value has the components of the specified value, reifies a single keyword.
- The integer of a `<grid-line>` (`grid-row-start`, `grid-column-end`...) is a line number, which can be combined with a
  name and with `span`: as in the browsers, it is a direct `CssStyleValue` object, and a numeric value cannot be set
  (the specification has no rule for these properties).
- The values of the rule properties of CSS Gaps (`column-rule-style`, `column-rule-width`, `row-rule-style`,
  `row-rule-width`) are lists of values and of `repeat()` functions: they are direct `CssStyleValue` objects, even when
  the list has one value.
- The computed value of a registered custom property is reified according to the component of its syntax that it matches
  ([CSS Properties and Values API](https://drafts.css-houdini.org/css-properties-values-api/#css-style-value-reification),
  see the value types above). The specified values of the custom properties are unparsed values.
- The computed value of the `background` shorthand is serialized with the computed value of every longhand of every
  layer, as in Blink and WebKit (`rgb(0, 0, 255) none repeat scroll 0% 0% / auto padding-box border-box`), where
  `getComputedStyle()` omits the initial values.
- The list-valued properties (the properties whose grammar is a comma-separated list, e.g. `transition-duration`,
  `background-image`) have one value per iteration: `Get()` returns the first one and `GetAll()` all of them.
- The shorthands are direct `CssStyleValue` objects (`text-align`, which is a longhand for the Typed OM, reifies its
  keywords as keywords), as is `font-family`.
- A specified list of keywords serialized in its shortest form, a single keyword (`paint-order: fill stroke` is `fill`,
  `scroll-snap-type: inline proximity` is `inline`), is a direct `CssStyleValue` of the value as specified, not a keyword.

## Setting values

`StylePropertyMap.Set()` and `Append()` accept `CssStyleValue` objects and strings. The values are checked against the
grammar of the property with the parser of the CSSOM (a `TypeError` is thrown when a value is invalid):

- A numeric value must have the type of the property (e.g. `CSS.number(0)` is not a valid `width`). A `CssUnitValue`
  outside of the range of the property is wrapped in `calc()` (e.g. `width: calc(-1px)`, `z-index: calc(1.5)`), as
  specified by [create an internal representation](https://drafts.css-houdini.org/css-typed-om-1/#create-an-internal-representation).
- A keyword, a numeric value or a transform value set through the Typed OM is the internal representation of the
  declaration: `Get()` returns it as it was set, whatever the grammar of the property (e.g. a keyword of
  `column-rule-style`, the length of a `CSSPerspective`, which is serialized in `calc()` when it is relative). The
  declaration is serialized like any specified value (`CSSMathSum(2, 3)` is `calc(5)`), but, as in the browsers, the
  math value keeps its structure (`Get()` returns `CSSMathSum(2, 3)`), unless one of its units is not canonical: the
  units are then converted and the value is simplified, as
  [specified](https://drafts.css-houdini.org/css-typed-om-1/#create-an-internal-representation) for every math value
  (`CSSMathSum(1s, 500ms)` is `CSSMathSum(1.5s)`).
- A custom property, registered or not, only accepts a string or a `CssUnparsedValue` (the other values do not match its
  grammar: a `TypeError` is thrown).
- Several values can only be set to a list-valued property, and not when one of them contains a `var()` reference.
- `Set()` removes the declaration, then appends it at the end of the block; the `style` attribute is updated once.

## Limitations

- The shorthands of the library that are longhands in the browsers (e.g. `box-shadow`, `overflow-clip-margin`,
  `vertical-align`) are direct `CssStyleValue` objects, and the properties whose grammar or computed value differs from
  the browsers (e.g. the border widths when the border style is `none`) reify accordingly. The shorthands accept the
  typed values that match their grammar, as specified (Blink throws a `TypeError`).
- The serialization of the math values follows the specification: their arguments are not sorted nor converted to the
  canonical units (e.g. `CSSMathSum(1px, 1em)` is `calc(1px + 1em)`, also in a `CSSTransformValue`, where Blink sorts
  them). `CSSScale` always serializes its x and y scales (`scale(1.5, 1.5)`), as Blink and WebKit do, where the
  specification omits a y scale equal to the x scale.
- The pseudo-elements have no computed style map (`CSSPseudoElement` has no `computedStyleMap()`).
