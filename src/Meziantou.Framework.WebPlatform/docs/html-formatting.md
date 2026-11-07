# HTML formatting

`Node.ToHtml(HtmlFormatter?)` returns the markup of a node, and `Node.ToHtml(TextWriter, HtmlFormatter?)` writes it to a
`TextWriter` without building a string. An element is written with its start tag, its descendants and its end tag (as
`OuterHtml`); a document or a document fragment is written as its children. The formatters are in the
`Meziantou.Framework.WebPlatform.Formatting` namespace:

| Formatter | Output |
|---|---|
| `HtmlFormatter.Default` (when no formatter is given) | The [HTML fragment serialization algorithm](https://html.spec.whatwg.org/multipage/parsing.html#serialising-html-fragments): the same output as `OuterHtml`, `GetHtml()` and `Document.GetHtml()` |
| `HtmlMinifyFormatter` | A minified markup that is parsed to the same document and rendered the same way |
| `HtmlPrettyFormatter` | An indented markup, to read or compare documents (the whitespace is not preserved) |

```csharp
using Meziantou.Framework.WebPlatform.Formatting;

var document = HtmlDocumentParser.ParseDocument(html);
var minified = document.ToHtml(new HtmlMinifyFormatter());
var indented = document.ToHtml(new HtmlPrettyFormatter { Indentation = "\t" });

using var writer = File.CreateText("index.html");
document.ToHtml(writer, new HtmlMinifyFormatter { PreservedStartTags = ["html", "head"] });
```

A formatter is immutable and can be shared between threads. The document is never modified.

## Minification

`HtmlMinifyFormatter` only minifies the HTML: the content of the `script` elements is written as is, and so are the
content of the `style` elements and the value of the `style` attributes unless a CSS formatter is set (see
[CSS in style elements and attributes](#css-in-style-elements-and-attributes)). Each transformation can be disabled:

| Option | Default | Description |
|---|---|---|
| `RemoveComments` | `true` | Removes the comments. |
| `CollapseWhitespace` | `true` | Replaces each sequence of whitespace by a single space and removes the spaces that are not rendered: at the start and the end of a line, or after another space. The whitespace of the `class` attributes is collapsed too. |
| `TrimTrailingWhitespaceInPreformattedText` | `true` | Removes the spaces and tabs at the end of the lines of the preformatted text (`pre`...), and the whitespace at the end of a preformatted element. |
| `OmitOptionalTags` | `true` | Omits the [optional tags](https://html.spec.whatwg.org/multipage/syntax.html#optional-tags) (`<html>`, `<head>`, `</p>`, `</li>`, `</td>`...). |
| `PreservedStartTags` | empty | The start tags that are always written (e.g. `html` and `head`, for the tools that expect them). |
| `RemoveAttributeQuotes` | `true` | Removes the quotes of the attribute values when they are not needed, uses single quotes when it avoids escaping, and writes an empty value as the attribute name only (`<script defer>`). |
| `RemoveScriptTypeAttribute` | `true` | Removes the `type` attribute of the classic scripts (`text/javascript`...). |

Only the characters that need it are escaped: `<` in text, the quote of an attribute value, and `&` when it could start a
character reference. The other characters, including U+00A0, are written as is.

### Whitespace

The whitespace that can be removed depends on the layout: the spaces around a block-level element are not rendered,
but the spaces around an inline element are. The minifier computes the styles of the elements (`display`,
`white-space-collapse`, and the `content` of `::before` and `::after`) from the user agent style sheet, the `style`
elements and the `style` attributes of the document (see [computed-style.md](computed-style.md)), so the style sheets
that change the layout are taken into account:

```html
<style>li { display: inline }</style>
<ul><li>a</li> <li>b</li></ul>  <!-- the space between the items is kept -->
```

- Every media query is considered to match (`@media print`, any width, dark mode...): the whitespace that is significant
  in any environment is kept. The rules of a `<link rel=stylesheet>` are only known once its style sheet is fetched (a
  document of a browsing context that has a `DocumentLoader`, see [computed-style.md](computed-style.md#linked-style-sheets)).
- The text whose whitespace is preserved (`pre`, `textarea`, `white-space: pre`...) and the content of the SVG and
  MathML elements are not modified.
- The whitespace at the edges of the content of an element hidden with `display: none` is kept, in case a script shows
  it (except in the `head`, which is never rendered). The whitespace at the edges of the written node is kept when it is
  not a document, as the content around it is unknown (e.g. the contents of a `template`).

## Pretty printing

`HtmlPrettyFormatter` writes each node on its own line, indented by its depth (`Indentation`, two spaces by default,
and `NewLine`, `"\n"` by default). The text is trimmed and its whitespace collapsed, and an element whose only content
is a text is written on a single line. The content of `pre`, `textarea`, `listing`, `script`, `style` and the other raw
text elements is written as is. The indentation stops growing after `MaxIndentationDepth` levels (64 by default), so
the size of the output stays linear in the size of deeply nested documents.

```html
<div class="a">
  <p>
    Hello
    <b>world</b>
  </p>
  <p>Single line</p>
</div>
```

The output is meant to be read or compared: two documents that only differ by their whitespace have the same pretty
output. Use it for the tests that compare HTML while ignoring the formatting, not to write the documents that are served.

## CSS in style elements and attributes

By default the content of the `style` elements and the value of the `style` attributes are written as is. Two
properties of `HtmlFormatter` rewrite them with a [CSS formatter](css-formatting.md), independently of each other:

| Property | Formats |
|---|---|
| `StyleElementFormatter` | The style sheet of the `style` elements |
| `StyleAttributeFormatter` | The declarations of the `style` attributes |

```csharp
var minified = document.ToHtml(new HtmlMinifyFormatter
{
    StyleElementFormatter = new CssMinifyFormatter(),
    StyleAttributeFormatter = new CssMinifyFormatter(),
});

// Indented style sheets, and normalized style attributes on a single line
var indented = document.ToHtml(new HtmlPrettyFormatter
{
    StyleElementFormatter = new CssPrettyFormatter(),
    StyleAttributeFormatter = CssFormatter.Default,
});
```

The CSS parser drops what browsers drop, so these things are not written: comments, invalid rules, and declarations of
unsupported properties (including vendor-prefixed ones). Values are written in their serialized form (see
[css-formatting.md](css-formatting.md)). This is why both options are opt-in.

### Style elements

The text of each HTML `style` element whose type is CSS (no `type` attribute, or `text/css`) is parsed, and the
resulting style sheet is formatted. With `HtmlPrettyFormatter`, the style sheet is written on the lines after the start
tag, indented by the depth of the element's content. The text is written as is when:

- the element is not a CSS style element (e.g. `type="text/less"`), or is an SVG `style` element;
- the element has children other than text nodes;
- the formatted text contains `</style`, which would end the element (e.g. the CSS string `"\3c/style>"` is
  serialized as `"</style>"`).

### Style attributes

The value of each `style` attribute (on the elements of all namespaces, including SVG and MathML) is parsed as a
declaration block, and the resulting declarations are formatted (`style="color : red; margin: 0 auto;"` is written
`style="color:red;margin:0px auto"` by `CssMinifyFormatter`). The quotes and the escaping of the value are chosen by the
HTML formatter as for any other attribute. The attribute is written even when no declaration is left (`style=""`), as
the selectors and the scripts can test its presence. A namespaced attribute whose local name is `style` is not formatted.

## Custom formatters

Derive from `HtmlFormatter` and `HtmlMarkupWriter`. The writer walks the tree (iteratively, so deep trees do not
overflow the stack) and calls a virtual method for each node; the default implementation of each method is the HTML
serialization algorithm. `WriteRawText` writes the text of the raw text elements (`script`, `style`...), and formats the
CSS of the `style` elements when `StyleElementFormatter` is set; `WriteAttributes` formats the `style` attributes when
`StyleAttributeFormatter` is set, so `WriteAttribute` receives the formatted value. `ShouldWrite` skips a node and its descendants, and `Depth` is the depth of the node being
written.

```csharp
sealed class NoCommentFormatter : HtmlFormatter
{
    protected override HtmlMarkupWriter CreateWriter(TextWriter output) => new Writer(output);

    private sealed class Writer(TextWriter output) : HtmlMarkupWriter(output)
    {
        protected override bool ShouldWrite(Node node) => node is not Comment;
    }
}
```

The building blocks of the serialization algorithm are public: `HtmlFormatter.IsVoidElement`,
`HtmlFormatter.HasRawTextContent` (the elements whose text is not escaped, e.g. `script`),
`HtmlFormatter.GetSerializedTagName`, `HtmlFormatter.GetSerializedAttributeName` (with the `xml:`, `xmlns:` and
`xlink:` prefixes) and `HtmlFormatter.WriteEscaped`.
