# CSS formatting

`CssStyleSheet.ToCss(CssFormatter?)`, `CssRule.ToCss(CssFormatter?)` and `CssStyleDeclaration.ToCss(CssFormatter?)`
return the text of a style sheet, a rule or a declaration block (e.g. the `style` of an element), and the
`ToCss(TextWriter, CssFormatter?)` overloads write it to a `TextWriter` without building a string. The formatters are in
the `Meziantou.Framework.WebPlatform.Formatting` namespace:

| Formatter | Output |
|---|---|
| `CssFormatter.Default` (when no formatter is given) | The [CSSOM serialization](https://drafts.csswg.org/cssom/#serialize-a-css-rule): the same output as `CssRule.CssText` and `CssStyleDeclaration.CssText`; the rules of a style sheet are separated by a line feed |
| `CssMinifyFormatter` | A minified text that is parsed to the same rules |
| `CssPrettyFormatter` | An indented text, to read or compare style sheets |

```csharp
using Meziantou.Framework.WebPlatform.Formatting;

var document = HtmlDocumentParser.ParseDocument(html);
var sheet = document.StyleSheets[0];
var minified = sheet.ToCss(new CssMinifyFormatter());
var indented = sheet.ToCss(new CssPrettyFormatter { Indentation = "\t" });
```

A formatter is immutable and can be shared between threads. The style sheet is never modified.

To format the content of the `style` elements and the `style` attributes when formatting HTML, set
`HtmlFormatter.StyleElementFormatter` and `HtmlFormatter.StyleAttributeFormatter` (see
[html-formatting.md](html-formatting.md#css-in-style-elements-and-attributes)).

The object model is formatted, not the text it was parsed from. As in the browsers, the comments, the invalid rules
and the declarations of the unsupported properties are not in the object model, so they are not written, and the values
are written in their serialized form: the longhands are combined into their shorthand, `#f00` is written
`rgb(255, 0, 0)`, `calc(1px + 2px)` is written `calc(3px)`...

## Minification

`CssMinifyFormatter` removes the whitespace that is not needed (around the commas, the brackets, the combinators other
than the descendant combinator, the colons of the media features...) and the semicolon of the last declaration of a
block. The values of the custom properties are written as is. Each other transformation can be disabled:

| Option | Default | Description |
|---|---|---|
| `RemoveEmptyRules` | `true` | Removes the style rules without declarations, and the `@media`, `@supports`, `@container`, `@scope` and `@starting-style` rules without rules. The empty `@layer` and `@keyframes` rules are kept: they declare a layer or replace the keyframes of the same name. |
| `ShortenColors` | `true` | Writes the opaque `rgb()` colors in their hexadecimal notation (`rgb(255, 0, 0)` is written `#f00`). |
| `RemoveLeadingZeros` | `true` | Removes the leading zero of the numbers (`0.5s` is written `.5s`). |

```css
a>b,c:hover{color:#f00;margin:0px auto!important}@media (min-width:100px){a{transition:color .5s}}
```

## Pretty printing

`CssPrettyFormatter` writes each rule and each declaration on its own line, indented by its depth (`Indentation`, two
spaces by default, and `NewLine`, `"\n"` by default). Each selector of the selector list of a style rule is written on
its own line, and an empty block is written on the line of its prelude. The indentation stops growing after
`MaxIndentationDepth` levels (64 by default), so the size of the output stays linear in the size of deeply nested input.

```css
h1,
h2 > a {
  color: red;
}
@media print {
  a {
    color: blue;
    & .b {
      color: green;
    }
  }
  b {}
}
```

## Custom formatters

Derive from `CssFormatter` and `CssWriter`. The writer walks the rules and calls a virtual method for each part of a
rule: `WritePrelude` (the selectors or the at-keyword and the prelude of an at-rule), `WriteStatementEnd` (the semicolon
of `@import`, `@namespace` and the `@layer` statements), `WriteBlockStart`, `WriteDeclaration` (a declaration or a
descriptor) and `WriteBlockEnd`. The default implementation of each method is the CSSOM serialization, including the
whitespace before the rule or the declaration, so an override can call the base method with other arguments.
`ShouldWrite` skips a rule and its child rules, and `Depth` is the number of blocks that contain the item being written.

```csharp
sealed class NoPrintFormatter : CssFormatter
{
    protected override CssWriter CreateWriter(TextWriter output) => new Writer(output);

    private sealed class Writer(TextWriter output) : CssWriter(output)
    {
        protected override bool ShouldWrite(CssRule rule) => rule is not CssMediaRule { Media.MediaText: "print" };
    }
}
```
