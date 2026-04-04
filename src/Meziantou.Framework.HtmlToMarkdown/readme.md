# Meziantou.Framework.HtmlToMarkdown

A .NET library to convert HTML fragments to Markdown.

## Usage

### Basic conversion

```csharp
using Meziantou.Framework;

var html = "<h1>Title</h1><p>Hello <strong>world</strong></p>";
var markdown = HtmlToMarkdown.Convert(html);

// # Title
//
// Hello **world**
```

### Configure conversion options

```csharp
using Meziantou.Framework;

var html = "<h1>Title</h1><p>Line 1<br>Line 2</p>";

var markdown = HtmlToMarkdown.Convert(html, new HtmlToMarkdownOptions
{
    HeadingStyle = HeadingStyle.Setext,
    EmphasisMarker = EmphasisMarker.Underscore,
    CodeBlockStyle = CodeBlockStyle.Fenced,
    CodeBlockFenceCharacter = '~',
    UnorderedListMarker = '*',
    ThematicBreak = "***",
    LineBreakStyle = LineBreakStyle.Backslash,
    UnknownElementHandling = UnknownElementHandling.StripKeepContent,
});
```

## Supported HTML elements

The converter supports common Markdown-related elements, including:

- Headings (`h1`-`h6`)
- Paragraphs and line breaks (`p`, `br`)
- Emphasis (`strong`, `b`, `em`, `i`, `del`, `s`, `strike`)
- Links and images (`a`, `img`)
- Lists (`ul`, `ol`, `li`) including task list checkboxes (`input type="checkbox"`)
- Blockquotes (`blockquote`)
- Code (`code`, `pre`) with fenced or indented blocks
- Tables (`table`, `thead`, `tbody`, `tfoot`, `tr`, `th`, `td`) with alignment support
- Definition lists (`dl`, `dt`, `dd`)

`script`, `style`, and `noscript` elements are stripped.

Unknown elements are handled using `UnknownElementHandling`:

- `PassThrough` (default): keep raw HTML
- `Strip`: remove the element and its content
- `StripKeepContent`: remove the element but keep converted child content
