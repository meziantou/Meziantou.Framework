---
title: Migrating from 1.x
description: Replace the pipeline, the renderers and the syntax tree of version 1.x, a fork of Markdig, with the API of version 2.0, and review the changes of the HTML.
---

Version 1.x of `Meziantou.Framework.Markdown` was a fork of [Markdig](https://github.com/xoofx/markdig). Version 2.0
replaces it with a new parser, whose tree keeps every character of the text. The package keeps its name and its
namespace, but the API is new: the pipeline, the renderers and the syntax tree of 1.x are gone. The extensions of 1.x
are built-in flags, and the HTML is the same, except where this page says otherwise.

## Convert Markdown

| 1.x | 2.0 |
| --- | --- |
| `MarkdownConverter.ToHtml(markdown)` | [MarkdownConverter.ToHtml(markdown)](cref:Meziantou.Framework.Markdown.MarkdownConverter.ToHtml(System.String)) |
| `MarkdownConverter.ToHtml(markdown, pipeline)` | [MarkdownConverter.ToHtml(markdown, parseOptions, rendererOptions)](cref:Meziantou.Framework.Markdown.MarkdownConverter.ToHtml(System.String,Meziantou.Framework.Markdown.MarkdownParseOptions,Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions)) |
| `MarkdownConverter.ToPlainText(markdown, pipeline)` | [MarkdownConverter.ToPlainText](cref:Meziantou.Framework.Markdown.MarkdownConverter.ToPlainText(System.String,Meziantou.Framework.Markdown.MarkdownParseOptions,Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions)) or [MarkdownPlainTextRenderer](cref:Meziantou.Framework.Markdown.MarkdownPlainTextRenderer) |
| `MarkdownConverter.Parse(markdown, pipeline)` | [MarkdownSyntaxTree.ParseText(markdown, options)](cref:Meziantou.Framework.Markdown.MarkdownSyntaxTree.ParseText(System.String,Meziantou.Framework.Markdown.MarkdownParseOptions,System.String)) |
| `document.ToHtml(pipeline)` | [MarkdownHtmlRenderer.ToHtml(tree, options)](cref:Meziantou.Framework.Markdown.MarkdownHtmlRenderer.ToHtml(Meziantou.Framework.Markdown.MarkdownSyntaxTree,Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions)) |
| `MarkdownConverter.Normalize` | [MarkdownFormatter](cref:Meziantou.Framework.Markdown.MarkdownFormatter): see [Formatting](06-Formatting.md) |
| `MarkdownConverter.Parse(markdown, trackTrivia: true)` and the roundtrip renderer | Every tree keeps its text: `tree.GetRoot().ToFullString()` |
| `MarkdownEscaper` | [MarkdownEscaper](cref:Meziantou.Framework.Markdown.MarkdownEscaper), with the same methods |

The overloads that write to a `TextWriter` have no equivalent: write the string that the methods return.

## Replace the pipeline

[MarkdownParseOptions](cref:Meziantou.Framework.Markdown.MarkdownParseOptions) replaces the extensions of the
pipeline that change how the text is read, and
[MarkdownHtmlRendererOptions](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions) the ones that change how
the HTML is written. Both are immutable records: create them once and reuse them.

| `MarkdownPipelineBuilder` method | 2.0 |
| --- | --- |
| `UseAdvancedExtensions` | [MarkdownExtensions.Advanced](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Advanced) |
| `UsePipeTables` | [PipeTables](cref:Meziantou.Framework.Markdown.MarkdownExtensions.PipeTables), and [PipeTableOptions](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.PipeTableOptions); [Tables](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Tables) for the tables of GitHub Flavored Markdown |
| `UseGridTables` | [GridTables](cref:Meziantou.Framework.Markdown.MarkdownExtensions.GridTables) |
| `UseTaskLists` | [TaskLists](cref:Meziantou.Framework.Markdown.MarkdownExtensions.TaskLists) |
| `UseAutoLinks` | [MarkdigAutolinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.MarkdigAutolinks), and [AutolinkOptions](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.AutolinkOptions); [Autolinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Autolinks) for the autolinks of GitHub Flavored Markdown |
| `UseEmphasisExtras` | [EmphasisExtras](cref:Meziantou.Framework.Markdown.MarkdownExtensions.EmphasisExtras), or each of its flags |
| `UseAlertBlocks` | [Alerts](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Alerts), and [AllowNestedAlerts](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.AllowNestedAlerts) |
| `UseAbbreviations`, `UseCitations`, `UseCustomContainers`, `UseDefinitionLists`, `UseDiagrams`, `UseFigures`, `UseFooters`, `UseFootnotes`, `UseListExtras`, `UseMathematics`, `UseSmartyPants`, `UseYamlFrontMatter`, `UseBootstrap`, `UseGlobalization`, `UseCjkFriendlyEmphasis` | The flag of the same name in [MarkdownExtensions](cref:Meziantou.Framework.Markdown.MarkdownExtensions) |
| `UseAutoIdentifiers` | [AutoIdentifiers](cref:Meziantou.Framework.Markdown.MarkdownExtensions.AutoIdentifiers), and [AutoIdentifierOptions](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.AutoIdentifierOptions) |
| `UseEmojiAndSmiley` | [Emoji](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Emoji), and [EmojiMapping](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.EmojiMapping) |
| `UseJiraLinks` | [JiraLinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.JiraLinks), and [MarkdownParseOptions.JiraLinks](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.JiraLinks) |
| `UseMediaLinks` | [MediaLinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.MediaLinks), and [MarkdownHtmlRendererOptions.MediaLinks](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.MediaLinks) |
| `UseGenericAttributes` | [GenericAttributes](cref:Meziantou.Framework.Markdown.MarkdownExtensions.GenericAttributes); the attribute filter is [AttributeFilter](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.AttributeFilter) |
| `UseSoftlineBreakAsHardlineBreak` | [HardLineBreaks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.HardLineBreaks) |
| `DisableHtml` | [DisableHtml](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.DisableHtml) |
| `MaximumNestingDepth` | [MaximumNestingDepth](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.MaximumNestingDepth): a deeper block or inline is text, where 1.x threw an `ArgumentException` |
| `UseReferralLinks` | [LinkRel](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.LinkRel) |
| `UseNonAsciiNoEscape` | [EscapeNonAsciiUrlCharacters](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.EscapeNonAsciiUrlCharacters) set to `false` |
| `UsePragmaLines` | [PragmaLines](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.PragmaLines) |
| `UsePreciseSourceLocation` | Every node has its span: see [Source text and positions](../Language/03-Concepts/04-SourceTextAndPositions.md) |

The extension points of 1.x, `IMarkdownExtension`, the block and inline parsers, and the object renderers, have no
equivalent. To change the document before it is rendered, rewrite the tree with a
[MarkdownSyntaxRewriter](cref:Meziantou.Framework.Markdown.MarkdownSyntaxRewriter); to read it, visit it with a
[MarkdownSyntaxWalker](cref:Meziantou.Framework.Markdown.MarkdownSyntaxWalker). See [The syntax tree](03-SyntaxTree.md).

## Changes of the HTML

Where GitHub Flavored Markdown defines a construct, 2.0 writes the HTML of its specification:

- The cells of the tables are aligned with `align="center"`, not `style="text-align: center;"`. The widths of the
  columns of a grid table keep their `<col style="width:...%" />`.
- The items of a task list have no class, and their checkbox is `<input disabled="" type="checkbox">` or
  `<input checked="" disabled="" type="checkbox">`.

2.0 follows CommonMark 0.31.2 and its reference implementation, commonmark.js, where 1.x differed:

- `<!x>` is a declaration, and `<ab:c>` is an autolink.
- A line of whitespace ends an HTML block of type 6 or 7.
- A hard line break in a setext heading is written as `<br />`.
- `> a` followed by `>-` is a setext heading.
- The labels of the links are compared as CommonMark says, with the Unicode full case folding and whitespace only:
  `[ﬀ]` matches `[FF]`, but `[Itém]` does not match `[Item]`. This also applies to the links to headings of the
  auto-identifiers.
- The identifiers made as GitHub makes them keep the combining marks and the letters outside the Basic Multilingual
  Plane, as GitHub does: `# हिन्दी` has the identifier `हिन्दी`, not `हनद`.
- A GFM extended email autolink can start with `mailto:` or `xmpp:`, which is part of the link.
- In URLs, `~` and `'` are kept, the hosts that are not ASCII are percent-encoded instead of converted to punycode,
  and a `%` that is not followed by two hexadecimal digits is encoded as `%25`.
- The text of an image whose description ends with a line ending keeps it.
- A lone surrogate is read as U+FFFD to decide whether a delimiter can open or close an emphasis.
- `a*-b*` is not an emphasis: the punctuation exemption of Markdig (`−`, `-`, `+`, `†`, `‡`) only applies to the
  delimiters of the extra emphasis.

The extensions changed in rare constructs:

- A run of delimiters longer than the maximum of its extra emphasis splits as CommonMark does for `***a***`: `+++c+++`
  is `+<ins>c</ins>+`, where 1.x wrote `+<ins>c+</ins>`. This also applies to `::`, quotes and carets.
- Pipe tables keep a link whose text has a pipe in one cell. A row whose last pipes are in a link is written after the
  table, where 1.x wrote it before.
- In a grid table, the content of a cell is a document of its own: the footnotes and the abbreviations of the outer
  document do not apply to it, and the definitions of the cell are not visible outside it. Invalid lines before a line
  that starts another block end the paragraph.
- In a footnote, a list item continued by an indented block quote keeps it, as outside footnotes.
- Generic attributes apply to the fenced blocks of every extension, whatever the order of the extensions.
- An indented `:::x` line after a paragraph of a definition starts a custom container.
- Citations and SmartyPants, which `UseAdvancedExtensions` does not enable together, can pair runs of three quotes
  differently.
- The alerts have no `RenderKind` callback: rewrite the tree, or the HTML, to change their title.

The plain text of [MarkdownPlainTextRenderer](cref:Meziantou.Framework.Markdown.MarkdownPlainTextRenderer) starts the
text after a table on a new line, ends with a line ending, writes the characters of SmartyPants decoded instead of as
entities, and does not write the title of an alert.
[MarkdownEscaper.EscapeLinkTitle](cref:Meziantou.Framework.Markdown.MarkdownEscaper.EscapeLinkTitle(System.String))
writes every line ending as an entity.
