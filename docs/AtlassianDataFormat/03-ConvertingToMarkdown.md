---
title: Converting to Markdown
description: Choose how panels, collapsible sections, tables, media, mentions and unknown nodes are written, and resolve media URLs and display names.
---

`AdfToMarkdown.Convert` accepts the JSON as a `string`, a `JsonElement` or a `JsonNode`, or an `AdfDocument`, and an
optional `AdfToMarkdownOptions`. `AdfDocument.ToMarkdown` does the same for a parsed document.

## Options

Every option has a default that produces plain Markdown. Set the ones you need:

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tests/AdfToMarkdownTests.cs;region=AdfToMarkdown_Options;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = AdfToMarkdown.Convert(json, new AdfToMarkdownOptions
{
    PanelStyle = AdfPanelStyle.GitHubAlert,      // > [!WARNING]
    ExpandStyle = AdfExpandStyle.HtmlDetails,    // <details><summary>…</summary>
    TableStyle = AdfTableStyle.Auto,             // pipe table, or HTML when cells span rows/columns
    MediaRendering = AdfMediaRendering.Link,
    EmojiRendering = AdfEmojiRendering.ShortName,
    TaskListStyle = AdfTaskListStyle.Checkbox,
    DecisionListStyle = AdfDecisionListStyle.BulletList,
    UnknownNodeHandling = AdfUnknownNodeHandling.KeepContent,
    MentionFormat = "@{text}",
    StatusFormat = "**[{text}]**",
    DateFormat = "yyyy-MM-dd",
    HeadingStyle = AdfHeadingStyle.Atx,
    EmphasisMarker = AdfEmphasisMarker.Asterisk,
    CodeBlockStyle = AdfCodeBlockStyle.Fenced,
    LineBreakStyle = AdfLineBreakStyle.Backslash,
});
```
<!-- endref -->

| Option | Default | Other values |
| --- | --- | --- |
| `HeadingStyle` | `Atx`: `#` prefixes. | `Setext`: underlined headings, for levels 1 and 2 only; deeper levels stay ATX. |
| `EmphasisMarker` | `Asterisk`: `*` and `**`. | `Underscore`: `_` and `__`. |
| `CodeBlockStyle` | `Fenced`, with the language of the block. | `Indented`: four spaces, without the language. |
| `CodeBlockFenceCharacter` | `` ` `` | Any character, such as `~`. |
| `UnorderedListMarker` | `-` | Any character, such as `*`. |
| `ThematicBreak` | `---` | Any text, written for a `rule`. |
| `LineBreakStyle` | `TrailingSpaces`: two spaces before the line feed. | `Backslash`: a `\` before the line feed. |
| `PanelStyle` | `Blockquote`: a quote that starts with an emoji for the panel type. | `GitHubAlert`, `PlainText`, `Html`. |
| `ExpandStyle` | `Blockquote`: a quote whose first line is the bold title. | `HtmlDetails`: `<details>`; `Heading`: a level 3 heading. |
| `TableStyle` | `PipeTable` | `Html`, `Auto`. |
| `MediaRendering` | `Image` | `Link`, `AltText`, `Skip`. |
| `EmojiRendering` | `Text`: the emoji characters. | `ShortName`, such as `:smile:`. |
| `TaskListStyle` | `Checkbox`: `- [ ]` and `- [x]`. | `PlainText`: a bullet list. |
| `DecisionListStyle` | `BulletList` | `PlainText`: a paragraph per decision. |
| `UnknownNodeHandling` | `Skip` | `KeepContent`, `Throw`. |
| `MentionFormat` | `@{text}` | See [Mentions, statuses and dates](#mentions-statuses-and-dates). |
| `StatusFormat` | `` `{text}` `` | |
| `DateFormat` | `yyyy-MM-dd` | |
| `MediaUrlResolver`, `MentionResolver` | None. | See [Media and mentions](#media-and-mentions). |

## Panels

A panel is an info, note, tip, success, warning or error box. By default it becomes a quote that starts with an emoji.
`GitHubAlert` writes the alerts that GitHub renders as colored boxes:

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tests/AdfToMarkdownTests.cs;region=AdfToMarkdown_Panels;format=md-fenced-code-block;dedent=true -->
```csharp
var json = """
    {
      "version": 1,
      "type": "doc",
      "content": [
        {
          "type": "panel",
          "attrs": { "panelType": "warning" },
          "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "Back up first." } ] } ]
        }
      ]
    }
    """;

Assert.Equal("> ⚠️ Back up first.", AdfToMarkdown.Convert(json));

var markdown = AdfToMarkdown.Convert(json, new AdfToMarkdownOptions { PanelStyle = AdfPanelStyle.GitHubAlert });
Assert.Equal("> [!WARNING]\n> Back up first.", markdown);
```
<!-- endref -->

`GitHubAlert` maps info and note panels to `[!NOTE]`, tip and success to `[!TIP]`, warning to `[!WARNING]` and error
to `[!CAUTION]`. A panel of another type, such as a custom panel, falls back to `Blockquote`. `PlainText` writes a
quote without a marker, and `Html` a `<div>` that carries the panel type.

## Collapsible sections

An expand is a section with a title that the reader opens. `Blockquote`, the default, writes the title in bold at the
top of a quote. `HtmlDetails` writes a `<details>` element that most Markdown viewers render as a collapsible section,
and `Heading` writes the title as a level 3 heading followed by the content.

## Tables

`PipeTable`, the default, writes a GitHub table:

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tests/AdfToMarkdownTests.cs;region=AdfToMarkdown_Tables;format=md-fenced-code-block;dedent=true -->
```csharp
var json = """
    {
      "version": 1,
      "type": "doc",
      "content": [
        {
          "type": "table",
          "content": [
            {
              "type": "tableRow",
              "content": [
                { "type": "tableHeader", "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "Name" } ] } ] },
                { "type": "tableHeader", "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "Role" } ] } ] }
              ]
            },
            {
              "type": "tableRow",
              "content": [
                { "type": "tableCell", "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "Alex" } ] } ] },
                { "type": "tableCell", "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "Developer" } ] } ] }
              ]
            }
          ]
        }
      ]
    }
    """;

Assert.Equal("| Name | Role |\n| --- | --- |\n| Alex | Developer |", AdfToMarkdown.Convert(json));
```
<!-- endref -->

- A pipe table needs a header row: when the first row of the table is not one, an empty header row is written so no
  row is lost.
- A pipe table cell holds one line. A cell with several paragraphs, a list or other blocks is flattened, its blocks
  separated by `<br>`.
- A pipe table has no spans: a cell that spans several rows or columns takes one cell, and short rows are padded with
  empty cells.
- `Html` writes an HTML table, which keeps the spans and the blocks of the cells. `Auto` writes a pipe table when every
  cell holds only inline content and no cell spans, and an HTML table otherwise.

## Mentions, statuses and dates

Mentions, status lozenges and dates are inline nodes with their own format:

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tests/AdfToMarkdownTests.cs;region=AdfToMarkdown_InlineNodes;format=md-fenced-code-block;dedent=true -->
```csharp
var json = """
    {
      "version": 1,
      "type": "doc",
      "content": [
        {
          "type": "paragraph",
          "content": [
            { "type": "mention", "attrs": { "id": "557058:1", "text": "@Alex" } },
            { "type": "text", "text": " moved it to " },
            { "type": "status", "attrs": { "text": "In progress", "color": "blue" } },
            { "type": "text", "text": " on " },
            { "type": "date", "attrs": { "timestamp": "1767225600000" } }
          ]
        }
      ]
    }
    """;

Assert.Equal("@Alex moved it to `In progress` on 2026-01-01", AdfToMarkdown.Convert(json));

var markdown = AdfToMarkdown.Convert(json, new AdfToMarkdownOptions
{
    MentionFormat = "[{text}](https://example.atlassian.net/people/{id})",
    StatusFormat = "**{text}** ({color})",
    DateFormat = "MMMM d, yyyy",
});
Assert.Equal("[Alex](https://example.atlassian.net/people/557058:1) moved it to **In progress** (blue) on January 1, 2026", markdown);
```
<!-- endref -->

- In `MentionFormat`, `{text}` is the display name of the user, without its leading `@`, and `{id}` the account
  identifier.
- In `StatusFormat`, `{text}` is the text of the lozenge and `{color}` its color. The text is inserted as it is, not
  escaped, because the default format puts it in a code span.
- `DateFormat` is a `DateTimeOffset` format string, applied with the invariant culture to the date in UTC.

## Media and mentions

A media item is an attachment or an image. By default it becomes an image, or a link when it is not an image, and its
alternative text when no URL is known. `Link` always writes a link, `AltText` the alternative text, and `Skip` nothing.

Only media of type `external` carries a URL in the document: the files stored by Atlassian only carry an identifier
and a collection, and getting their URL needs an authenticated call to the media API. Documents returned by the APIs
also often omit the display name of a mention. Give both with a callback:

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tests/AdfToMarkdownTests.cs;region=AdfToMarkdown_Resolvers;format=md-fenced-code-block;dedent=true -->
```csharp
var json = """
    {
      "version": 1,
      "type": "doc",
      "content": [
        { "type": "paragraph", "content": [ { "type": "mention", "attrs": { "id": "123" } } ] },
        {
          "type": "mediaSingle",
          "content": [ { "type": "media", "attrs": { "type": "file", "id": "abc", "collection": "files", "alt": "diagram.png" } } ]
        }
      ]
    }
    """;

var displayNames = new Dictionary<string, string> { ["123"] = "Alex" };
var markdown = AdfToMarkdown.Convert(json, new AdfToMarkdownOptions
{
    MediaUrlResolver = media => $"https://media.example.com/{media.Collection}/{media.Id}",
    MentionResolver = mention => displayNames.GetValueOrDefault(mention.Id),
});

Assert.Equal("@Alex\n\n![diagram.png](https://media.example.com/files/abc)", markdown);
```
<!-- endref -->

A resolver returns `null` when it does not know the answer: the converter then uses the URL or the text stored in the
document, and for a mention without a text, its identifier.

## Unknown nodes

A node whose type the library does not know, such as `unsupportedBlock` or a type added after this version, is dropped
by default. `KeepContent` drops the node but converts its content, and `Throw` throws an `AdfException`:

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tests/AdfToMarkdownTests.cs;region=AdfToMarkdown_UnknownNodes;format=md-fenced-code-block;dedent=true -->
```csharp
var json = """
    {
      "version": 1,
      "type": "doc",
      "content": [
        { "type": "paragraph", "content": [ { "type": "text", "text": "Before" } ] },
        { "type": "somethingNew", "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "Inside" } ] } ] }
      ]
    }
    """;

// By default, the node and its content are dropped
Assert.Equal("Before", AdfToMarkdown.Convert(json));

var markdown = AdfToMarkdown.Convert(json, new AdfToMarkdownOptions { UnknownNodeHandling = AdfUnknownNodeHandling.KeepContent });
Assert.Equal("Before\n\nInside", markdown);

Assert.Throws<AdfException>(() => AdfToMarkdown.Convert(json, new AdfToMarkdownOptions { UnknownNodeHandling = AdfUnknownNodeHandling.Throw }));
```
<!-- endref -->

Use `Throw` in a test to learn which node types your documents contain.

## Other nodes and marks

- **Text** is escaped, so characters such as `*` or `#` read as text. The text of a code mark is written as it is,
  inside a code span.
- **Cards** (`inlineCard`, `blockCard`, `embedCard`) become a link, named after the card when the document gives its
  name, or the bare URL otherwise.
- **Extensions** become their text; a `bodiedExtension` becomes its content.
- **Layouts** are flattened: the columns are written one after the other.
- **Marks without a Markdown equivalent** (`underline`, `textColor`, `backgroundColor`, `annotation`, `border`,
  `alignment`, `indentation`, `breakout`) are dropped, and their text is kept. `subsup` becomes `<sub>` or `<sup>`.
