---
title: Formatting options
description: Choose the syntax of headings, emphasis, code blocks, lists, horizontal rules and line breaks, and convert punctuation and emoji.
---

`HtmlToMarkdownOptions` chooses the Markdown syntax of the elements that Markdown can write in several ways. Create it
once and pass it to every call of `HtmlToMarkdown.Convert`.

| Option | Default | Values |
| --- | --- | --- |
| `HeadingStyle` | `Atx` | `Atx` (`# Title`), `Setext` (title underlined with `=` or `-`) |
| `EmphasisMarker` | `Asterisk` | `Asterisk` (`*em*`, `**strong**`), `Underscore` (`_em_`, `__strong__`) |
| `CodeBlockStyle` | `Fenced` | `Fenced`, `Indented` (four spaces, no language) |
| `CodeBlockFenceCharacter` | `` ` `` | `` ` `` or `~` |
| `UnorderedListMarker` | `-` | `-`, `*` or `+` |
| `ThematicBreak` | `---` | The text of a horizontal rule (`hr`) |
| `LineBreakStyle` | `TrailingSpaces` | `TrailingSpaces`, `Backslash` |
| `UseSimplePunctuation` | `false` | `true` replaces the typographic punctuation with ASCII characters |
| `EmojiShortcodeMode` | `None` | `None`, `GitHub` (`:heart:`), `Unicode` (`:red_heart:`) |
| `UnknownElementHandling` | `PassThrough` | `PassThrough`, `Strip`, `StripKeepContent`: see [Unknown elements](05-UnknownElementsAndUntrustedHtml.md) |

## Headings

`HeadingStyle.Setext` underlines the `h1` headings with `=` and the `h2` headings with `-`. Markdown has no Setext
syntax for the other levels: they stay ATX headings.

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_SetextHeadings;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = HtmlToMarkdown.Convert("<h1>Title</h1><h2>Section</h2><h3>Subsection</h3>", new HtmlToMarkdownOptions
{
    HeadingStyle = HeadingStyle.Setext,
});
Assert.Equal("""
    Title
    =====

    Section
    -------

    ### Subsection
    """, markdown);
```
<!-- endref -->

## Emphasis, lists and horizontal rules

`EmphasisMarker`, `UnorderedListMarker` and `ThematicBreak` follow the style of the documents you add the Markdown to:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_MarkdownStyle;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = HtmlToMarkdown.Convert("<p><strong>Note</strong>: <em>read</em> this.</p><hr><ul><li>One</li><li>Two</li></ul>", new HtmlToMarkdownOptions
{
    EmphasisMarker = EmphasisMarker.Underscore,
    ThematicBreak = "* * *",
    UnorderedListMarker = '+',
});
Assert.Equal("""
    __Note__: _read_ this.

    * * *

    + One
    + Two
    """, markdown);
```
<!-- endref -->

`ThematicBreak` is written as is: give a valid Markdown horizontal rule, such as `---`, `***`, `___` or `* * *`.
`UnorderedListMarker` and `CodeBlockFenceCharacter` throw an `ArgumentOutOfRangeException` when they are set to another
character:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_InvalidOptionValue;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HtmlToMarkdownOptions();
Assert.Throws<ArgumentOutOfRangeException>("value", () => options.UnorderedListMarker = '#');
Assert.Throws<ArgumentOutOfRangeException>("value", () => options.CodeBlockFenceCharacter = '"');
```
<!-- endref -->

## Code blocks

`CodeBlockStyle.Indented` writes the code indented by four spaces. An indented block has no language: use the fenced
style, the default, to keep it. [Code blocks](03-ConvertedElements.md#code-blocks) shows both styles.

## Line breaks

A `br` becomes two spaces at the end of the line by default. The spaces are invisible and some editors remove them:
`LineBreakStyle.Backslash` writes a `\` instead.

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_LineBreaks;format=md-fenced-code-block;dedent=true -->
```csharp
var html = "<p>Line 1<br>Line 2</p>";

// Two trailing spaces by default
var markdown = HtmlToMarkdown.Convert(html);
Assert.Equal("Line 1  \nLine 2", markdown);

var backslash = HtmlToMarkdown.Convert(html, new HtmlToMarkdownOptions { LineBreakStyle = LineBreakStyle.Backslash });
Assert.Equal("Line 1\\\nLine 2", backslash);
```
<!-- endref -->

In a table cell, a `br` stays `<br>` whatever the option: a row of a pipe table cannot hold a line break.

## Simple punctuation

`UseSimplePunctuation` replaces the typographic quotes, dashes, ellipsis and guillemets of the text with ASCII
characters: `“` and `”` with `"`, `‘` and `’` with `'`, `–` with `--`, `—` with `---`, `…` with `...`, `«` with `<<` and
`»` with `>>`:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_SimplePunctuation;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = HtmlToMarkdown.Convert(
    "<p>“Hello” ‘Hello’ — – … « »</p>",
    new HtmlToMarkdownOptions { UseSimplePunctuation = true });
Assert.Equal("""
    "Hello" 'Hello' \-\-\- \-\- ... \<\< \>\>
    """, markdown);
```
<!-- endref -->

The replacement happens before the text is escaped, so the dashes and the guillemets are escaped and render as the
characters `-`, `<` and `>`. Only the text is changed: the code blocks and code spans keep their characters.

## Emoji shortcodes

`EmojiShortcodeMode` replaces the emoji of the text with shortcodes, such as the ones of GitHub:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_EmojiShortcodes;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = HtmlToMarkdown.Convert("<p>I ❤️ Markdown</p>", new HtmlToMarkdownOptions
{
    EmojiShortcodeMode = EmojiShortcodeMode.GitHub,
});
Assert.Equal("I :heart: Markdown", markdown);

var unicodeMarkdown = HtmlToMarkdown.Convert("<p>I ❤️ Markdown</p>", new HtmlToMarkdownOptions
{
    EmojiShortcodeMode = EmojiShortcodeMode.Unicode,
});
Assert.Equal("I :red_heart: Markdown", unicodeMarkdown);
```
<!-- endref -->

- `GitHub` uses the shortcodes of GitHub. It also finds an emoji written with or without its variation selector (the
  invisible character that asks for the emoji presentation).
- `Unicode` uses shortcodes made from the Unicode names of the emoji.

An emoji that has no shortcode stays as it is. As with the punctuation, the code blocks and code spans are not changed.
To render the shortcodes as emoji, the Markdown renderer must support them: see
[Rendering the Markdown](06-RenderingTheMarkdown.md).
