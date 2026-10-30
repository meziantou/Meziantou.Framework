---
title: Extensions
description: Enable the syntaxes that CommonMark does not define, one by one, all at once or from a configuration string.
---

Without extensions, the parser follows the CommonMark specification. Extensions add the syntaxes that CommonMark does
not define, such as tables and footnotes, and change how some elements are rendered.

## Enable extensions

Each extension has a `Use…` method on [MarkdownPipelineBuilder](cref:Meziantou.Framework.Markdown.MarkdownPipelineBuilder). Call the ones you need, then [Build](cref:Meziantou.Framework.Markdown.MarkdownPipelineBuilder.Build):

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_PipelineExtensions;format=md-fenced-code-block;dedent=true -->
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

- **Enable only what your content uses.** Each extension adds a syntax: text that was plain before may become a table, a
  link or an emoji.
- **Call [UseGenericAttributes](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseGenericAttributes) after the other extensions.** It changes the parsers that the other extensions add.
- **Pass the options in the first call.** An extension is enabled once: most `Use…` methods do nothing when their
  extension is already enabled, and keep its options. [UseAlertBlocks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseAlertBlocks) and [UseAutoLinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseAutoLinks) replace it instead,
  [UseGenericAttributes](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseGenericAttributes) replaces its filter, and [UseReferralLinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseReferralLinks) adds the new `rel` values.

## Enable the common extensions at once

[UseAdvancedExtensions](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseAdvancedExtensions) enables alert blocks, abbreviations, auto-identifiers, citations, custom containers, definition
lists, extra emphasis, figures, footers, footnotes, grid tables, mathematics, media links, pipe tables, extra list types,
task lists, diagrams, auto-links and generic attributes:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_Pipeline;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .Build();

var html = MarkdownConverter.ToHtml("| a | b |\n|---|---|\n| 1 | 2 |", pipeline);
Assert.StartsWith("<table>", html);
```
<!-- endref -->

It does not enable emojis, SmartyPants, soft line breaks as hard breaks, CJK emphasis, globalization, Jira links,
referral links, Bootstrap classes, YAML front matter, pragma lines or [UseNonAsciiNoEscape](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseNonAsciiNoEscape).

[UseAdvancedExtensions](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseAdvancedExtensions) enables each extension with its default options, unless it is already enabled. To change the
options of one of them, enable it first:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_OptionsBeforeAdvancedExtensions;format=md-fenced-code-block;dedent=true -->
```csharp
// UseAdvancedExtensions keeps the extensions that are already enabled, with their options
var pipeline = new MarkdownPipelineBuilder()
    .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
    .UseAdvancedExtensions()
    .Build();

var html = MarkdownConverter.ToHtml("# Été", pipeline);
Assert.Equal("<h1 id=\"été\">Été</h1>\n", html);

// In the other order, UseAutoIdentifiers does nothing: the extension is already enabled with the default options
var ignored = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
    .Build();
Assert.Equal("<h1 id=\"ete\">Été</h1>\n", MarkdownConverter.ToHtml("# Été", ignored));
```
<!-- endref -->

## Enable extensions from a configuration string

[Configure](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Configure) enables the extensions named in a string, separated by `+`. Use it to choose the extensions in a setting of
your application:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_Configure;format=md-fenced-code-block;dedent=true -->
```csharp
// For instance, read from the configuration of the application
var extensions = "pipetables+tasklists+autoidentifiers";

var pipeline = new MarkdownPipelineBuilder()
    .Configure(extensions)
    .Build();

var html = MarkdownConverter.ToHtml("- [x] Done", pipeline);
Assert.Equal("<ul class=\"contains-task-list\">\n<li class=\"task-list-item\"><input disabled=\"disabled\" type=\"checkbox\" checked=\"checked\" /> Done</li>\n</ul>\n", html);
```
<!-- endref -->

The names are case-insensitive; an unknown name throws an `ArgumentException`.

| Name | Enables |
| --- | --- |
| `common` | Nothing more than CommonMark |
| `advanced` | [UseAdvancedExtensions()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseAdvancedExtensions) |
| `alerts` | [UseAlertBlocks()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseAlertBlocks) |
| `pipetables` | [UsePipeTables()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UsePipeTables) |
| `gfm-pipetables` | [UsePipeTables](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UsePipeTables) with `UseGfmRules = true` |
| `gridtables` | [UseGridTables()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseGridTables) |
| `emphasisextras` | [UseEmphasisExtras()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseEmphasisExtras) |
| `listextras` | [UseListExtras()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseListExtras) |
| `hardlinebreak` | [UseSoftlineBreakAsHardlineBreak()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseSoftlineBreakAsHardlineBreak) |
| `footnotes` | [UseFootnotes()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseFootnotes) |
| `footers` | [UseFooters()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseFooters) |
| `citations` | [UseCitations()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseCitations) |
| `attributes` | [UseGenericAttributes()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseGenericAttributes) |
| `abbreviations` | [UseAbbreviations()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseAbbreviations) |
| `emojis` | [UseEmojiAndSmiley()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseEmojiAndSmiley) |
| `definitionlists` | [UseDefinitionLists()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseDefinitionLists) |
| `customcontainers` | [UseCustomContainers()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseCustomContainers) |
| `figures` | [UseFigures()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseFigures) |
| `mathematics` | [UseMathematics()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseMathematics) |
| `bootstrap` | [UseBootstrap()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseBootstrap) |
| `medialinks` | [UseMediaLinks()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseMediaLinks) |
| `smartypants` | [UseSmartyPants()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseSmartyPants) |
| `autoidentifiers` | [UseAutoIdentifiers()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseAutoIdentifiers) |
| `tasklists` | [UseTaskLists()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseTaskLists) |
| `diagrams` | [UseDiagrams()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseDiagrams) |
| `nofollowlinks`, `noopenerlinks`, `noreferrerlinks` | `UseReferralLinks("nofollow")`, `"noopener"` or `"noreferrer"` |
| `nohtml` | [DisableHtml()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.DisableHtml) |
| `yaml` | [UseYamlFrontMatter()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseYamlFrontMatter) |
| `nonascii-noescape` | [UseNonAsciiNoEscape()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseNonAsciiNoEscape) |
| `autolinks` | [UseAutoLinks()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseAutoLinks) |
| `globalization` | [UseGlobalization()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseGlobalization) |
| `cjk-friendly-emphasis` | [UseCjkFriendlyEmphasis()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseCjkFriendlyEmphasis) |

## All the extensions

| Method | Syntax or effect | Page |
| --- | --- | --- |
| [UsePipeTables](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UsePipeTables) | GitHub-style tables: `\| a \| b \|` | [Tables](02-Tables.md) |
| [UseGridTables](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseGridTables) | Pandoc grid tables, drawn with `+`, `-` and `\|` | [Tables](02-Tables.md) |
| [UseBootstrap](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseBootstrap) | Bootstrap classes on tables, figures, images and quotes | [Tables](02-Tables.md#bootstrap-classes) |
| [UseAutoIdentifiers](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseAutoIdentifiers) | An `id` on each heading, and links to headings by their text | [Headings and links](03-HeadingsAndLinks.md) |
| [UseAutoLinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseAutoLinks) | Links from `https://…`, `www.…` and `mailto:` text | [Headings and links](03-HeadingsAndLinks.md#automatic-links) |
| [UseReferralLinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseReferralLinks) | `rel` values, such as `nofollow`, on links | [Headings and links](03-HeadingsAndLinks.md#rel-attributes) |
| [UseJiraLinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseJiraLinks) | Links from `PROJECT-123` references | [Headings and links](03-HeadingsAndLinks.md#jira-links) |
| [UseMediaLinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseMediaLinks) | Players for YouTube, Vimeo, audio and video links | [Headings and links](03-HeadingsAndLinks.md#embedded-media) |
| [UseNonAsciiNoEscape](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseNonAsciiNoEscape) | Non-ASCII characters of URLs written as they are | [Headings and links](03-HeadingsAndLinks.md#non-ascii-urls) |
| [UseEmphasisExtras](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseEmphasisExtras) | `~~deleted~~`, `~sub~`, `^sup^`, `++inserted++`, `==marked==` | [Text formatting](04-TextFormatting.md) |
| [UseSmartyPants](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseSmartyPants) | Typographic quotes, dashes and ellipses | [Text formatting](04-TextFormatting.md#typographic-punctuation) |
| [UseEmojiAndSmiley](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseEmojiAndSmiley) | `:tada:` shortcodes and `:)` smileys | [Text formatting](04-TextFormatting.md#emojis) |
| [UseAbbreviations](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseAbbreviations) | `*[HTML]: …` definitions rendered as `<abbr>` | [Text formatting](04-TextFormatting.md#abbreviations) |
| [UseCjkFriendlyEmphasis](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseCjkFriendlyEmphasis) | Emphasis rules adapted to Chinese, Japanese and Korean text | [Text formatting](04-TextFormatting.md#chinese-japanese-and-korean-text) |
| [UseGlobalization](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseGlobalization) | `dir="rtl"` on right-to-left content | [Text formatting](04-TextFormatting.md#right-to-left-text) |
| [UseSoftlineBreakAsHardlineBreak](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseSoftlineBreakAsHardlineBreak) | Every line break rendered as `<br />` | [Converting to HTML](../03-ConvertingToHtml.md#line-breaks-and-line-endings) |
| [UseAlertBlocks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseAlertBlocks) | GitHub alerts: `> [!NOTE]`, `> [!WARNING]`… | [Blocks](05-Blocks.md) |
| [UseCustomContainers](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseCustomContainers) | `:::` blocks rendered as `<div>`, `::text::` as `<span>` | [Blocks](05-Blocks.md#custom-containers) |
| [UseDefinitionLists](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseDefinitionLists) | Terms and definitions rendered as `<dl>` | [Blocks](05-Blocks.md#definition-lists) |
| [UseFigures](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseFigures), [UseFooters](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseFooters), [UseCitations](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseCitations) | `^^^` figures, `^^` footers, `""title""` citations | [Blocks](05-Blocks.md#figures-footers-and-citations) |
| [UseFootnotes](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseFootnotes) | `[^1]` footnotes | [Blocks](05-Blocks.md#footnotes) |
| [UseTaskLists](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseTaskLists) | `- [ ]` and `- [x]` items | [Blocks](05-Blocks.md#task-lists) |
| [UseListExtras](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseListExtras) | `a.` and `i.` ordered lists | [Blocks](05-Blocks.md#alphabetical-and-roman-lists) |
| [UseMathematics](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseMathematics) | `$inline$` and `$$block$$` mathematics | [Blocks](05-Blocks.md#mathematics) |
| [UseDiagrams](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseDiagrams) | `mermaid` and `nomnoml` code blocks | [Blocks](05-Blocks.md#diagrams) |
| [UseGenericAttributes](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseGenericAttributes) | `{#id .class name=value}` attributes | [Attributes](06-GenericAttributes.md) |
| [UseYamlFrontMatter](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseYamlFrontMatter) | A leading YAML block, excluded from the output | [YAML front matter](07-YamlFrontMatter.md) |
| [UsePragmaLines](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UsePragmaLines) | An `id` with the line number on each block | [Source positions](../06-SourcePositions.md#synchronize-an-editor-and-a-preview) |
| [UsePreciseSourceLocation](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UsePreciseSourceLocation) | The exact position of every inline | [Source positions](../06-SourcePositions.md#positions-of-inlines) |
| [DisableHtml](cref:Meziantou.Framework.Markdown.MarkdownExtensions.DisableHtml) | Raw HTML written as text | [Security](../10-Security.md) |
| [DisableHeadings](cref:Meziantou.Framework.Markdown.MarkdownExtensions.DisableHeadings) | No ATX (`#`) or setext headings | |
| [EnableTrackTrivia](cref:Meziantou.Framework.Markdown.MarkdownExtensions.EnableTrackTrivia) | Whitespace kept to write the document back | [Editing documents](../07-EditingDocuments.md#keep-the-formatting-roundtrip) |
| [ConfigureNewLine](cref:Meziantou.Framework.Markdown.MarkdownExtensions.ConfigureNewLine) | The line ending of the output | [Converting to HTML](../03-ConvertingToHtml.md#line-breaks-and-line-endings) |

The `.md` files of the
[test specifications](https://github.com/meziantou/Meziantou.Framework/tree/main/tests/Meziantou.Framework.Markdown.Tests/Specs)
give the exact syntax and output of each extension, with many examples.

## Pages

- [Tables](02-Tables.md): pipe tables, grid tables and their options.
- [Headings and links](03-HeadingsAndLinks.md): heading identifiers, automatic links, `rel` values, Jira links and
  embedded media.
- [Text formatting](04-TextFormatting.md): strikethrough and other emphasis, typographic punctuation, emojis and
  abbreviations.
- [Blocks](05-Blocks.md): alerts, containers, definition lists, figures, footnotes, task lists, mathematics and
  diagrams.
- [Attributes](06-GenericAttributes.md): add identifiers, classes and attributes to the elements.
- [YAML front matter](07-YamlFrontMatter.md): read the metadata at the top of a document.
