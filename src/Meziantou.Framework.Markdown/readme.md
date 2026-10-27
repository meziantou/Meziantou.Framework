# Meziantou.Framework.Markdown

`Meziantou.Framework.Markdown` is a fast, [CommonMark](https://commonmark.org/) compliant, extensible Markdown processor
for .NET. It parses Markdown into an abstract syntax tree with precise source locations, and renders it to HTML, plain
text, or normalized Markdown. It can also parse and render a document without losing any whitespace (roundtrip).

The library is derived from [Markdig](https://github.com/xoofx/markdig) by Alexandre Mutel (BSD-2-Clause). See
[Differences from Markdig](#differences-from-markdig) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Install the package

```bash
dotnet add package Meziantou.Framework.Markdown
```

## Table of contents

- [Convert Markdown to HTML](#convert-markdown-to-html)
- [Configure the pipeline](#configure-the-pipeline)
- [Extensions](#extensions)
- [Security](#security)
- [Work with the syntax tree](#work-with-the-syntax-tree)
- [Other output formats](#other-output-formats)
- [Roundtrip](#roundtrip)
- [Differences from Markdig](#differences-from-markdig)

## Convert Markdown to HTML

`MarkdownConverter` (in the `Meziantou.Framework.Markdown` namespace) is the entry point. Without a pipeline, it uses the plain CommonMark parser:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_ToHtml;format=md-fenced-code-block;dedent=true -->
```csharp
var html = MarkdownConverter.ToHtml("This is a text with some *emphasis*");
Assert.Equal("<p>This is a text with some <em>emphasis</em></p>\n", html);
```
<!-- endref -->

To write to a `TextWriter` instead of allocating a string, use the `ToHtml(string, TextWriter, ...)` overload.

## Configure the pipeline

A `MarkdownPipeline` describes which parsers and renderers are active. Build it once and reuse it: a pipeline is
immutable and can be shared between threads.

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_Pipeline;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .Build();

var html = MarkdownConverter.ToHtml("| a | b |\n|---|---|\n| 1 | 2 |", pipeline);
Assert.StartsWith("<table>", html);
```
<!-- endref -->

`UseAdvancedExtensions()` enables alert blocks, abbreviations, auto-identifiers, citations, custom containers,
definition lists, extra emphasis, figures, footers, footnotes, grid tables, mathematics, media links, pipe tables, extra
list types, task lists, diagrams, auto-links, and generic attributes.

Each extension can also be enabled on its own:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_PipelineExtensions;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UsePipeTables()
    .UseTaskLists()
    .UseAutoIdentifiers()
    .Build();

var html = MarkdownConverter.ToHtml("# Getting started", pipeline);
Assert.Equal("<h1 id=\"getting-started\">Getting started</h1>\n", html);
```
<!-- endref -->

## Extensions

| Method | Description |
|---|---|
| `UseAlertBlocks` | GitHub alerts: `> [!NOTE]`, `> [!TIP]`, `> [!IMPORTANT]`, `> [!WARNING]`, `> [!CAUTION]` |
| `UseAbbreviations` | `*[HTML]: Hyper Text Markup Language` definitions, rendered as `<abbr>` |
| `UseAutoIdentifiers` | Generates an `id` for each heading |
| `UseAutoLinks` | Turns `http://`, `https://`, `ftp://`, `mailto:` and `www.` text into links |
| `UseBootstrap` | Adds Bootstrap classes to tables, figures, and blockquotes |
| `UseCitations` | `""Title""` rendered as `<cite>` |
| `UseCjkFriendlyEmphasis` | Emphasis rules adapted to Chinese, Japanese, and Korean text |
| `UseCustomContainers` | `:::` fenced blocks rendered as `<div>`, and `::inline::` rendered as `<span>` |
| `UseDefinitionLists` | Definition lists (`<dl>`) |
| `UseDiagrams` | `mermaid` and `nomnoml` code blocks rendered as diagram containers |
| `UseEmojiAndSmiley` | `:smile:` shortcodes and `:)` smileys |
| `UseEmphasisExtras` | `~~strikethrough~~`, `~subscript~`, `^superscript^`, `++inserted++`, `==marked==` |
| `UseFigures` | `^^^` blocks rendered as `<figure>` |
| `UseFooters` | `^^` blocks rendered as `<footer>` |
| `UseFootnotes` | `[^1]` footnotes |
| `UseGenericAttributes` | `{#id .class key=value}` attributes on blocks and inlines. By default, only the attributes that describe the content are written, such as `title`, `lang`, `alt`, `colspan`, `target`, `role` and `aria-*` (the list is below); pass a filter to allow more. Enable it last. |
| `UseGlobalization` | Adds `dir="rtl"` to right-to-left content |
| `UseGridTables` | Pandoc grid tables |
| `UseJiraLinks` | `PROJECT-123` references rendered as links to a Jira instance |
| `UseListExtras` | Alphabetical (`a.`) and roman (`i.`) ordered lists |
| `UseMathematics` | `$inline$` and `$$block$$` math |
| `UseMediaLinks` | Links to YouTube, Vimeo, and audio or video files rendered as embedded players |
| `UseNonAsciiNoEscape` | Keeps non-ASCII characters unescaped in URLs |
| `UsePipeTables` | GitHub-style pipe tables. `PipeTableOptions.UseGfmRules` enables strict GFM parsing |
| `UsePragmaLines` | Adds `id="pragma-line-N"` to the outermost block that starts on each line, to synchronize an editor and a preview |
| `UsePreciseSourceLocation` | Computes the exact source span of every inline |
| `UseReferralLinks` | Adds `rel` values, such as `nofollow`, to links |
| `UseSmartyPants` | Typographic quotes, dashes, and ellipses |
| `UseSoftlineBreakAsHardlineBreak` | Renders every line break as `<br />` |
| `UseTaskLists` | `- [ ]` and `- [x]` task lists |
| `UseYamlFrontMatter` | Parses a leading YAML front matter block and excludes it from the output |
| `DisableHtml` | Treats raw HTML as text |
| `DisableHeadings` | Disables ATX and setext headings |
| `EnableTrackTrivia` | Keeps whitespace and trivia for [roundtrip](#roundtrip) |
| `ConfigureNewLine` | Sets the line ending written by the renderers |

The `.md` files in the [test specifications](../../tests/Meziantou.Framework.Markdown.Tests/Specs) describe the exact
syntax and output of each extension.

## Security

CommonMark allows raw HTML, and it is copied to the output unchanged. Links are not filtered either: a
`[link](javascript:alert(1))` produces a `javascript:` URL. Before you render Markdown from an untrusted source:

- call `DisableHtml()` on the pipeline builder, so that raw HTML is written as escaped text,
- keep the default attribute filter of `UseGenericAttributes`. It only allows attributes that describe the content: `style`, `data-*` and the directive attributes of client-side frameworks (`x-init`, `hx-get`...) can run script or cover the page,
- sanitize the generated HTML (for example with `Meziantou.Framework.HtmlSanitizer`) to remove unsafe URLs and attributes.

The default attribute filter of `UseGenericAttributes` allows the attributes that only describe the content: the
general attributes that `Meziantou.Framework.HtmlSanitizer` allows, other than `class`. They are `abbr`, `align`, `alt`,
`axis`, `bgcolor`, `border`, `cellpadding`, `cellspacing`, `clear`, `color`, `cols`, `colspan`, `compact`, `coords`,
`datetime`, `decoding`, `dir`, `face`, `headers`, `height`, `hidden`, `hreflang`, `hspace`, `ismap`, `lang`,
`language`, `loading`, `nohref`, `nowrap`, `open`, `rel`, `rev`, `reversed`, `role`, `rows`, `rowspan`, `rules`,
`scope`, `scrolling`, `shape`, `size`, `span`, `start`, `summary`, `tabindex`, `target`, `title`, `translate`, `type`,
`valign`, `value`, `vspace`, `width` and `aria-*`. `{#id}` and `{.class}` are always written. When the Markdown is
trusted, pass a filter to allow more attributes (`GenericAttributesExtension` is in the
`Meziantou.Framework.Markdown.Extensions.GenericAttributes` namespace):

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_GenericAttributesFilter;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .UseGenericAttributes(name => GenericAttributesExtension.IsSafeAttributeName(name) || name.StartsWith("data-", StringComparison.OrdinalIgnoreCase))
    .Build();

var html = MarkdownConverter.ToHtml("Text {data-id=1 style=color:red}", pipeline);
Assert.Equal("<p data-id=\"1\">Text</p>\n", html);
```
<!-- endref -->

Nesting depth is limited so that hostile input cannot overflow the stack: a document nested too deeply throws an
`ArgumentException` instead. Catch it when you process untrusted input.

## Work with the syntax tree

`MarkdownConverter.Parse` returns a `MarkdownDocument`. Blocks and inlines are `MarkdownObject` instances, and each one
has a `Span` (offsets in the source text), a `Line`, and a `Column`. Lines and columns are zero-based. Blocks such as
`HeadingBlock` are in the `Meziantou.Framework.Markdown.Syntax` namespace, and inlines such as `LinkInline` in
`Meziantou.Framework.Markdown.Syntax.Inlines`.

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_SyntaxTree;format=md-fenced-code-block;dedent=true -->
```csharp
var document = MarkdownConverter.Parse("# Title\n\nSee [the docs](https://example.com).");

foreach (var heading in document.Descendants<HeadingBlock>())
{
    Assert.Equal(1, heading.Level);
    Assert.Equal(0, heading.Line);
}

foreach (var link in document.Descendants<LinkInline>())
{
    Assert.Equal("https://example.com", link.Url);
}
```
<!-- endref -->

You can change the tree and render it with the same pipeline:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_ChangeTree;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = "See [the docs](http://example.com).";
var pipeline = new MarkdownPipelineBuilder().Build();
var document = MarkdownConverter.Parse(markdown, pipeline);
foreach (var link in document.Descendants<LinkInline>())
{
    link.Url = link.Url?.Replace("http://", "https://", StringComparison.Ordinal);
}

var html = document.ToHtml(pipeline);
Assert.Equal("<p>See <a href=\"https://example.com\">the docs</a>.</p>\n", html);
```
<!-- endref -->

## Other output formats

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_OtherFormats;format=md-fenced-code-block;dedent=true -->
```csharp
// Plain text, without any markup
var text = MarkdownConverter.ToPlainText("Some **bold** text");
Assert.Equal("Some bold text\n", text);

// Normalized Markdown
var normalized = MarkdownConverter.Normalize("Title\n=====");
Assert.Equal("# Title", normalized);
```
<!-- endref -->

For other formats, implement `IMarkdownRenderer` (or derive from `TextRendererBase<T>`) and call
`MarkdownConverter.Convert(markdown, renderer, pipeline)`.

## Roundtrip

When trivia tracking is enabled, the parser records all whitespace, line endings, and other characters that do not change
the meaning of the document. The `RoundtripRenderer` then writes the document back exactly as it was parsed, so you can
change a document without reformatting it. `RoundtripRenderer` is in the
`Meziantou.Framework.Markdown.Renderers.Roundtrip` namespace.

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_Roundtrip;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = "#   Title  \n\n*  item\n";
var document = MarkdownConverter.Parse(markdown, trackTrivia: true);

// Change the tree here...

using var writer = new StringWriter();
var renderer = new RoundtripRenderer(writer);
renderer.Write(document);
var output = writer.ToString();
Assert.Equal(markdown, output); // Same as markdown when nothing changed
```
<!-- endref -->

Trivia is not part of the CommonMark specification, so the implementation decides where it goes. Line endings are
attached to the first node that can hold them. Blank lines are stored in `Block.LinesBefore` and `Block.LinesAfter`.
Other properties are named `Trivia*` (for example `TriviaBefore`, `TriviaAfter`) on the blocks and inlines that need
them. These properties are only populated when trivia tracking is enabled.

With [`UseGenericAttributes`](#extensions), the attributes of a heading (`GetAttributes()`) that are changed
after parsing are written as `{#id .class name=value}`, in place of the first attributes of the source. Its other
attributes are written as empty attributes, `{}`, so that the lines stay the same. A heading without attributes gets them
at the end of its text. Attributes that the parsing added without them being in the source, such as an identifier
generated from the heading text, are not written while they are unchanged. The other blocks and the inlines do not write
their changed attributes.

The renderer throws an `InvalidOperationException` when the parser would not read the attributes back: a value that
cannot be written (an identifier with a space, a value with a line break), or a heading text that would read them, such
as an unfinished `{name=` or an unclosed code span, or whose last line would then start another block, such as `*`.

## Differences from Markdig

- The namespaces start with `Meziantou.Framework.Markdown` instead of `Markdig`.
- The static `Markdig.Markdown` class is named `MarkdownConverter`, so it does not share its name with the namespace.
- The package targets .NET 10 and later only. .NET Framework and .NET Standard are not supported.
- `HostProviderBuilder` is a static class.
- Generic attributes only write the attributes that describe the content by default (see [Security](#security)).
  Markdig writes every attribute; pass `_ => true` as the filter of `UseGenericAttributes` to do the same.
- The self pipeline extension (`UseSelfPipeline`) is removed: it let the Markdown document choose the extensions of the
  pipeline, including removing the ones that the host used to make the output safe.
