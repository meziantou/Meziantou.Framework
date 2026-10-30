---
title: Text formatting
description: Strikethrough, subscript and highlight, typographic quotes and dashes, emojis, abbreviations, CJK and right-to-left text.
---

## Strikethrough and other emphasis

CommonMark has two kinds of emphasis: `*emphasis*` and `**strong**`. `UseEmphasisExtras` adds strikethrough,
subscript, superscript, inserted and marked text:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestEmphasisExtraOptions.cs;region=MarkdownExtensions_UseEmphasisExtras;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseEmphasisExtras()
    .Build();

var html = MarkdownConverter.ToHtml("~~old~~ H~2~O x^2^ ++inserted++ ==marked==", pipeline);
Assert.Equal("<p><del>old</del> H<sub>2</sub>O x<sup>2</sup> <ins>inserted</ins> <mark>marked</mark></p>\n", html);
```
<!-- endref -->

| Syntax | HTML | `EmphasisExtraOptions` flag |
| --- | --- | --- |
| `~~text~~` | `<del>` | `Strikethrough` |
| `~text~` | `<sub>` | `Subscript` |
| `^text^` | `<sup>` | `Superscript` |
| `++text++` | `<ins>` | `Inserted` |
| `==text==` | `<mark>` | `Marked` |

Pass the flags you want to enable only some of them. With strikethrough alone, as on GitHub, a single `~` stays a
character:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestEmphasisExtraOptions.cs;region=EmphasisExtraOptions_Strikethrough;format=md-fenced-code-block;dedent=true -->
```csharp
// Only strikethrough: a single ~ stays a literal character
var pipeline = new MarkdownPipelineBuilder()
    .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
    .Build();

var html = MarkdownConverter.ToHtml("~~old~~ about ~20 items", pipeline);
Assert.Equal("<p><del>old</del> about ~20 items</p>\n", html);
```
<!-- endref -->

## Typographic punctuation

`UseSmartyPants` replaces straight quotes with curly quotes, `--` and `---` with dashes, `...` with an ellipsis, and
`<<` `>>` with angle quotes:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestSmartyPants.cs;region=MarkdownExtensions_UseSmartyPants;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseSmartyPants()
    .Build();

var html = MarkdownConverter.ToHtml("\"Hello\" -- it's done... <<right>>", pipeline);
Assert.Equal("<p>&ldquo;Hello&rdquo; &ndash; it's done&hellip; &laquo;right&raquo;</p>\n", html);
```
<!-- endref -->

The replacements are HTML entities. `SmartyPantOptions.Mapping` changes them, for instance to follow the typographic
rules of a language:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestSmartyPants.cs;region=SmartyPantOptions_Mapping;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new SmartyPantOptions();
options.Mapping[SmartyPantType.LeftDoubleQuote] = "«&nbsp;";
options.Mapping[SmartyPantType.RightDoubleQuote] = "&nbsp;»";

var pipeline = new MarkdownPipelineBuilder()
    .UseSmartyPants(options)
    .Build();

var html = MarkdownConverter.ToHtml("\"Bonjour\"", pipeline);
Assert.Equal("<p>«&nbsp;Bonjour&nbsp;»</p>\n", html);
```
<!-- endref -->

The keys of the mapping are the values of `SmartyPantType`: `Quote`, `LeftQuote`, `RightQuote`, `DoubleQuote`,
`LeftDoubleQuote`, `RightDoubleQuote`, `LeftAngleQuote`, `RightAngleQuote`, `Ellipsis`, `Dash2` and `Dash3`. The values
are written to the HTML as they are.

## Emojis

`UseEmojiAndSmiley` replaces shortcodes, such as `:tada:`, and smileys, such as `:)`, with emojis. Pass
`enableSmileys: false` to replace the shortcodes only:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCustomEmojis.cs;region=MarkdownExtensions_UseEmojiAndSmiley;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseEmojiAndSmiley()
    .Build();

var html = MarkdownConverter.ToHtml("Well done :tada: :)", pipeline);
Assert.Equal("<p>Well done 🎉 😃</p>\n", html);
```
<!-- endref -->

To add shortcodes, or to replace the list, create an `EmojiMapping` (namespace
[Meziantou.Framework.Markdown.Extensions.Emoji](cref:Meziantou.Framework.Markdown.Extensions.Emoji)) from a dictionary of shortcodes and a dictionary of smileys:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCustomEmojis.cs;region=EmojiMapping_Custom;format=md-fenced-code-block;dedent=true -->
```csharp
// Start from the default shortcodes to keep them, and add yours
var shortcodes = EmojiMapping.GetDefaultEmojiShortcodeToUnicode();
shortcodes[":shipit:"] = "🐿️";
var mapping = new EmojiMapping(shortcodes, EmojiMapping.GetDefaultSmileyToEmojiShortcode());

var pipeline = new MarkdownPipelineBuilder()
    .UseEmojiAndSmiley(mapping)
    .Build();

var html = MarkdownConverter.ToHtml(":shipit: :tada:", pipeline);
Assert.Equal("<p>🐿️ 🎉</p>\n", html);
```
<!-- endref -->

## Abbreviations

`UseAbbreviations` reads the definitions of abbreviations, written `*[ABBR]: Definition` on their own line, and renders
each occurrence of the abbreviation as an `<abbr>` with the definition as its title. The definitions are not rendered:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseAbbreviations;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseAbbreviations()
    .Build();

var html = MarkdownConverter.ToHtml("*[HTML]: Hyper Text Markup Language\n\nThe HTML output.", pipeline);
Assert.Equal("<p>The <abbr title=\"Hyper Text Markup Language\">HTML</abbr> output.</p>\n", html);
```
<!-- endref -->

## Chinese, Japanese and Korean text

CommonMark decides whether `*` and `_` open or close emphasis from the characters around them, with rules designed for
languages that put spaces between words. In Chinese, Japanese and Korean text, the emphasis next to punctuation is then
often not recognized. `UseCjkFriendlyEmphasis` adapts these rules to these languages:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCjkFriendlyEmphasis.cs;region=MarkdownExtensions_UseCjkFriendlyEmphasis;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = "**この文を強調できますか？**残念ながら。";

// CommonMark does not close the emphasis after the full-width question mark
Assert.Equal("<p>**この文を強調できますか？**残念ながら。</p>\n", MarkdownConverter.ToHtml(markdown));

var pipeline = new MarkdownPipelineBuilder().UseCjkFriendlyEmphasis().Build();
Assert.Equal("<p><strong>この文を強調できますか？</strong>残念ながら。</p>\n", MarkdownConverter.ToHtml(markdown, pipeline));
```
<!-- endref -->

## Right-to-left text

`UseGlobalization` adds `dir="rtl"` to the headings, paragraphs and other elements whose text is written from right to
left, such as Arabic or Hebrew text:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseGlobalization;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseGlobalization()
    .Build();

var html = MarkdownConverter.ToHtml("# שלום\n\nHello", pipeline);
Assert.Equal("<h1 dir=\"rtl\">שלום</h1>\n<p>Hello</p>\n", html);
```
<!-- endref -->
