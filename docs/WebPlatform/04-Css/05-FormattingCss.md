---
title: Formatting CSS
description: Write a style sheet, a rule or a declaration block minified, indented or with your own rules.
---

`ToCss(formatter)` writes a style sheet (`CssStyleSheet`), a rule (`CssRule`) or a declaration block
(`CssStyleDeclaration`, such as the `Style` of an element) with a formatter of the
[Meziantou.Framework.WebPlatform.Formatting](cref:Meziantou.Framework.WebPlatform.Formatting) namespace. `ToCss(TextWriter, formatter)` writes it to a `TextWriter`:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Formatting/CssFormatterTests.cs;region=WebPlatform_FormatCss;format=md-fenced-code-block;dedent=true -->
```csharp
var sheet = new CssStyleSheet();
sheet.ReplaceSync("""
    /* Links */
    a > b, c:hover { color: #FF0000; margin: 0px auto !important }
    @media (min-width: 100px) { a { transition: color 0.5s } }
    """);

var minified = sheet.ToCss(new CssMinifyFormatter());
Assert.Equal("a>b,c:hover{color:#f00;margin:0px auto!important}@media (min-width:100px){a{transition:color .5s}}", minified);

var indented = sheet.ToCss(new CssPrettyFormatter());
Assert.Equal("""
    a > b,
    c:hover {
      color: rgb(255, 0, 0);
      margin: 0px auto !important;
    }
    @media (min-width: 100px) {
      a {
        transition: color 0.5s;
      }
    }
    """, indented);
```
<!-- endref -->

| Formatter | Output |
| --- | --- |
| `CssFormatter.Default` (no formatter) | The serialization of the browsers (`CssText`), one rule per line |
| `CssMinifyFormatter` | A smaller text that is parsed to the same rules |
| `CssPrettyFormatter` | An indented text, to read or compare style sheets |

The object model is written, not the text it was parsed from: the comments, the invalid rules and the declarations of
unsupported properties are not written, and the values are in their serialized form (the longhands are combined into
their shorthand, `calc(1px + 2px)` is `calc(3px)`). The formatters are immutable and never change the style sheet.

## Minify

`CssMinifyFormatter` removes the whitespace that is not needed and the last semicolon of each block. Its options, all
true by default:

| Option | Effect |
| --- | --- |
| `RemoveEmptyRules` | Removes the style rules without declarations and the empty `@media`, `@supports`, `@container`, `@scope` and `@starting-style` rules (an empty `@layer` or `@keyframes` rule is kept: it has an effect) |
| `ShortenColors` | Writes the opaque colors in hexadecimal (`#f00`) |
| `RemoveLeadingZeros` | Writes `0.5s` as `.5s` |

The values of the custom properties are written as they are.

## Indent

`CssPrettyFormatter` writes each rule, each selector of a selector list and each declaration on its own line, indented by
its depth (`Indentation`, two spaces by default; `NewLine`, `"\n"` by default).

## In HTML

To format the `style` elements and the `style` attributes while writing HTML, set the `StyleElementFormatter` and
`StyleAttributeFormatter` of the HTML formatter: see
[Serializing and formatting](../03-HtmlAndDom/05-SerializingAndFormatting.md#format-the-css-of-the-page).

## Write your own formatter

Derive from `CssFormatter` and `CssWriter`, and override the methods that write the parts of a rule (`WritePrelude`,
`WriteBlockStart`, `WriteDeclaration`, `WriteBlockEnd`...). `ShouldWrite` skips a rule and its child rules, and `Depth` is
the number of blocks around the item being written.
