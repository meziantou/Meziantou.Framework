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
    UseSimplePunctuation = true,
    EmojiShortcodeMode = EmojiShortcodeMode.GitHub,
    UnknownElementHandling = UnknownElementHandling.StripKeepContent,
});
```

### Replace SmartyPants punctuation with simple ASCII

You can opt in to convert smart punctuation in regular text nodes:

```csharp
var markdown = HtmlToMarkdown.Convert(
    "<p>“Hello” ‘Hello’ — – … « »</p>",
    new HtmlToMarkdownOptions { UseSimplePunctuation = true });

// "Hello" 'Hello' \-\-\- \-\- ... \<\< \>\>
```

### Emoji shortcode replacement

```csharp
var markdown = HtmlToMarkdown.Convert("<p>I ❤️ Markdown</p>", new HtmlToMarkdownOptions
{
    EmojiShortcodeMode = EmojiShortcodeMode.GitHub, // => I :heart: Markdown
});

var unicodeMarkdown = HtmlToMarkdown.Convert("<p>I ❤️ Markdown</p>", new HtmlToMarkdownOptions
{
    EmojiShortcodeMode = EmojiShortcodeMode.Unicode, // => I :red_heart: Markdown
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

`script`, `style`, and `noscript` elements are always stripped, including when they appear
inside an element that is passed through as raw HTML.

Unknown elements are handled using `UnknownElementHandling`:

- `PassThrough` (default): keep raw HTML
- `Strip`: remove the element and its content
- `StripKeepContent`: remove the element but keep converted child content

> [!WARNING]
> This library is a converter, not an HTML sanitizer. With `PassThrough`, an unknown element
> is emitted as raw HTML with its attributes intact, so event handlers such as `onclick` and
> `onerror` and URLs such as `javascript:` survive the conversion. If you convert untrusted
> HTML and render the resulting Markdown with a renderer that allows raw HTML, run a
> sanitizer over the input or the output, or use `Strip` or `StripKeepContent`.
