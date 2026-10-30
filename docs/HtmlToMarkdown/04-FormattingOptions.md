---
title: Formatting options
description: Choose the syntax of headings, emphasis, code blocks, lists, horizontal rules and line breaks, and convert punctuation and emoji.
---

[HtmlToMarkdownOptions](cref:Meziantou.Framework.HtmlToMarkdownOptions) chooses the Markdown syntax of the elements that Markdown can write in several ways. Create it
once and pass it to every call of [HtmlToMarkdown.Convert](cref:Meziantou.Framework.HtmlToMarkdown.Convert).

| Option | Default | Values |
| --- | --- | --- |
| [HeadingStyle](cref:Meziantou.Framework.HeadingStyle) | [Atx](cref:Meziantou.Framework.HeadingStyle.Atx) | [Atx](cref:Meziantou.Framework.HeadingStyle.Atx) (`# Title`), [Setext](cref:Meziantou.Framework.HeadingStyle.Setext) (title underlined with `=` or `-`) |
| [EmphasisMarker](cref:Meziantou.Framework.EmphasisMarker) | [Asterisk](cref:Meziantou.Framework.EmphasisMarker.Asterisk) | [Asterisk](cref:Meziantou.Framework.EmphasisMarker.Asterisk) (`*em*`, `**strong**`), [Underscore](cref:Meziantou.Framework.EmphasisMarker.Underscore) (`_em_`, `__strong__`) |
| [CodeBlockStyle](cref:Meziantou.Framework.CodeBlockStyle) | [Fenced](cref:Meziantou.Framework.CodeBlockStyle.Fenced) | [Fenced](cref:Meziantou.Framework.CodeBlockStyle.Fenced), [Indented](cref:Meziantou.Framework.CodeBlockStyle.Indented) (four spaces, no language) |
| [CodeBlockFenceCharacter](cref:Meziantou.Framework.HtmlToMarkdownOptions.CodeBlockFenceCharacter) | `` ` `` | `` ` `` or `~` |
| [UnorderedListMarker](cref:Meziantou.Framework.HtmlToMarkdownOptions.UnorderedListMarker) | `-` | `-`, `*` or `+` |
| [ThematicBreak](cref:Meziantou.Framework.HtmlToMarkdownOptions.ThematicBreak) | `---` | The text of a horizontal rule (`hr`) |
| [LineBreakStyle](cref:Meziantou.Framework.LineBreakStyle) | [TrailingSpaces](cref:Meziantou.Framework.LineBreakStyle.TrailingSpaces) | [TrailingSpaces](cref:Meziantou.Framework.LineBreakStyle.TrailingSpaces), [Backslash](cref:Meziantou.Framework.LineBreakStyle.Backslash) |
| [UseSimplePunctuation](cref:Meziantou.Framework.HtmlToMarkdownOptions.UseSimplePunctuation) | `false` | `true` replaces the typographic punctuation with ASCII characters |
| [EmojiShortcodeMode](cref:Meziantou.Framework.EmojiShortcodeMode) | [None](cref:Meziantou.Framework.EmojiShortcodeMode.None) | [None](cref:Meziantou.Framework.EmojiShortcodeMode.None), [GitHub](cref:Meziantou.Framework.EmojiShortcodeMode.GitHub) (`:heart:`), [Unicode](cref:Meziantou.Framework.EmojiShortcodeMode.Unicode) (`:red_heart:`) |
| [UnknownElementHandling](cref:Meziantou.Framework.UnknownElementHandling) | [PassThrough](cref:Meziantou.Framework.UnknownElementHandling.PassThrough) | [PassThrough](cref:Meziantou.Framework.UnknownElementHandling.PassThrough), [Strip](cref:Meziantou.Framework.UnknownElementHandling.Strip), [StripKeepContent](cref:Meziantou.Framework.UnknownElementHandling.StripKeepContent): see [Unknown elements](05-UnknownElementsAndUntrustedHtml.md) |

## Headings

[HeadingStyle.Setext](cref:Meziantou.Framework.HeadingStyle.Setext) underlines the `h1` headings with `=` and the `h2` headings with `-`. Markdown has no Setext
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

[EmphasisMarker](cref:Meziantou.Framework.EmphasisMarker), [UnorderedListMarker](cref:Meziantou.Framework.HtmlToMarkdownOptions.UnorderedListMarker) and [ThematicBreak](cref:Meziantou.Framework.HtmlToMarkdownOptions.ThematicBreak) follow the style of the documents you add the Markdown to:

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

[ThematicBreak](cref:Meziantou.Framework.HtmlToMarkdownOptions.ThematicBreak) is written as is: give a valid Markdown horizontal rule, such as `---`, `***`, `___` or `* * *`.
[UnorderedListMarker](cref:Meziantou.Framework.HtmlToMarkdownOptions.UnorderedListMarker) and [CodeBlockFenceCharacter](cref:Meziantou.Framework.HtmlToMarkdownOptions.CodeBlockFenceCharacter) throw an `ArgumentOutOfRangeException` when they are set to another
character:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_InvalidOptionValue;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HtmlToMarkdownOptions();
Assert.Throws<ArgumentOutOfRangeException>("value", () => options.UnorderedListMarker = '#');
Assert.Throws<ArgumentOutOfRangeException>("value", () => options.CodeBlockFenceCharacter = '"');
```
<!-- endref -->

## Code blocks

[CodeBlockStyle.Indented](cref:Meziantou.Framework.CodeBlockStyle.Indented) writes the code indented by four spaces. An indented block has no language: use the fenced
style, the default, to keep it. [Code blocks](03-ConvertedElements.md#code-blocks) shows both styles.

## Line breaks

A `br` becomes two spaces at the end of the line by default. The spaces are invisible and some editors remove them:
[LineBreakStyle.Backslash](cref:Meziantou.Framework.LineBreakStyle.Backslash) writes a `\` instead.

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

[UseSimplePunctuation](cref:Meziantou.Framework.HtmlToMarkdownOptions.UseSimplePunctuation) replaces the typographic quotes, dashes, ellipsis and guillemets of the text with ASCII
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

[EmojiShortcodeMode](cref:Meziantou.Framework.EmojiShortcodeMode) replaces the emoji of the text with shortcodes, such as the ones of GitHub:

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

- [GitHub](cref:Meziantou.Framework.EmojiShortcodeMode.GitHub) uses the shortcodes of GitHub. It also finds an emoji written with or without its variation selector (the
  invisible character that asks for the emoji presentation).
- [Unicode](cref:Meziantou.Framework.EmojiShortcodeMode.Unicode) uses shortcodes made from the Unicode names of the emoji.

An emoji that has no shortcode stays as it is. As with the punctuation, the code blocks and code spans are not changed.
To render the shortcodes as emoji, the Markdown renderer must support them: see
[Rendering the Markdown](06-RenderingTheMarkdown.md).
