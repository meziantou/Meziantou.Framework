---
title: Formatting CSS
description: Write a style sheet, a rule or a declaration block minified, indented or with your own rules.
---

`ToCss(formatter)` writes a style sheet ([CssStyleSheet](cref:Meziantou.Framework.WebPlatform.Css.Om.CssStyleSheet)), a rule ([CssRule](cref:Meziantou.Framework.WebPlatform.Css.Om.CssRule)) or a declaration block
([CssStyleDeclaration](cref:Meziantou.Framework.WebPlatform.Css.Om.CssStyleDeclaration), such as the [Style](cref:Meziantou.Framework.WebPlatform.Dom.HtmlElement.Style) of an element) with a formatter of the
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
| [CssFormatter.Default](cref:Meziantou.Framework.WebPlatform.Formatting.CssFormatter.Default) (no formatter) | The serialization of the browsers ([CssText](cref:Meziantou.Framework.WebPlatform.Css.Om.CssStyleDeclaration.CssText)), one rule per line |
| [CssMinifyFormatter](cref:Meziantou.Framework.WebPlatform.Formatting.CssMinifyFormatter) | A smaller text that is parsed to the same rules |
| [CssPrettyFormatter](cref:Meziantou.Framework.WebPlatform.Formatting.CssPrettyFormatter) | An indented text, to read or compare style sheets |

The object model is written, not the text it was parsed from: the comments, the invalid rules and the declarations of
unsupported properties are not written, and the values are in their serialized form (the longhands are combined into
their shorthand, `calc(1px + 2px)` is `calc(3px)`). The formatters are immutable and never change the style sheet.

## Minify

[CssMinifyFormatter](cref:Meziantou.Framework.WebPlatform.Formatting.CssMinifyFormatter) removes the whitespace that is not needed and the last semicolon of each block. Its options, all
true by default:

| Option | Effect |
| --- | --- |
| [RemoveEmptyRules](cref:Meziantou.Framework.WebPlatform.Formatting.CssMinifyFormatter.RemoveEmptyRules) | Removes the style rules without declarations and the empty `@media`, `@supports`, `@container`, `@scope` and `@starting-style` rules (an empty `@layer` or `@keyframes` rule is kept: it has an effect) |
| [ShortenColors](cref:Meziantou.Framework.WebPlatform.Formatting.CssMinifyFormatter.ShortenColors) | Writes the opaque colors in hexadecimal (`#f00`) |
| [RemoveLeadingZeros](cref:Meziantou.Framework.WebPlatform.Formatting.CssMinifyFormatter.RemoveLeadingZeros) | Writes `0.5s` as `.5s` |

The values of the custom properties are written as they are.

## Indent

[CssPrettyFormatter](cref:Meziantou.Framework.WebPlatform.Formatting.CssPrettyFormatter) writes each rule, each selector of a selector list and each declaration on its own line, indented by
its depth ([Indentation](cref:Meziantou.Framework.WebPlatform.Formatting.CssPrettyFormatter.Indentation), two spaces by default; [NewLine](cref:Meziantou.Framework.WebPlatform.Formatting.CssPrettyFormatter.NewLine), `"\n"` by default).

## In HTML

To format the `style` elements and the `style` attributes while writing HTML, set the [StyleElementFormatter](cref:Meziantou.Framework.WebPlatform.Formatting.HtmlFormatter.StyleElementFormatter) and
[StyleAttributeFormatter](cref:Meziantou.Framework.WebPlatform.Formatting.HtmlFormatter.StyleAttributeFormatter) of the HTML formatter: see
[Serializing and formatting](../03-HtmlAndDom/05-SerializingAndFormatting.md#format-the-css-of-the-page).

## Write your own formatter

Derive from [CssFormatter](cref:Meziantou.Framework.WebPlatform.Formatting.CssFormatter) and [CssWriter](cref:Meziantou.Framework.WebPlatform.Formatting.CssWriter), and override the methods that write the parts of a rule ([WritePrelude](cref:Meziantou.Framework.WebPlatform.Formatting.CssWriter.WritePrelude),
[WriteBlockStart](cref:Meziantou.Framework.WebPlatform.Formatting.CssWriter.WriteBlockStart), [WriteDeclaration](cref:Meziantou.Framework.WebPlatform.Formatting.CssWriter.WriteDeclaration), [WriteBlockEnd](cref:Meziantou.Framework.WebPlatform.Formatting.CssWriter.WriteBlockEnd)...). [ShouldWrite](cref:Meziantou.Framework.WebPlatform.Formatting.CssWriter.ShouldWrite) skips a rule and its child rules, and [Depth](cref:Meziantou.Framework.WebPlatform.Formatting.CssWriter.Depth) is
the number of blocks around the item being written.
