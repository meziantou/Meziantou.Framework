---
title: Command-line tool
description: Convert ADF documents to Markdown from the command line, from a file or the standard input, with the options of the library.
---

`Meziantou.Framework.AtlassianDataFormat.Tool` converts ADF documents to Markdown without writing code: use it in
scripts, for instance to export the description of a Jira issue.

## Install the tool

Install it as a global tool:

<InstallDotnetTool Id="Meziantou.Framework.AtlassianDataFormat.Tool" />

## Convert a file

`--input` is the ADF document, and `--output` the Markdown file to write:

```bash
Meziantou.Framework.AtlassianDataFormat.Tool --input issue.json --output issue.md
```

With this document:

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tool.Tests/Examples/issue.json;format=md-fenced-code-block;language=json -->
```json
{
  "version": 1,
  "type": "doc",
  "content": [
    {
      "type": "heading",
      "attrs": { "level": 2 },
      "content": [ { "type": "text", "text": "Steps to reproduce" } ]
    },
    {
      "type": "orderedList",
      "content": [
        {
          "type": "listItem",
          "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "Open the " }, { "type": "text", "text": "settings", "marks": [ { "type": "strong" } ] } ] } ]
        },
        {
          "type": "listItem",
          "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "Click " }, { "type": "text", "text": "Save", "marks": [ { "type": "code" } ] } ] } ]
        }
      ]
    },
    {
      "type": "panel",
      "attrs": { "panelType": "warning" },
      "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "The application stops responding." } ] } ]
    },
    {
      "type": "expand",
      "attrs": { "title": "Environment" },
      "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "Version 2.1 on Windows 11" } ] } ]
    }
  ]
}
```
<!-- endref -->

the tool writes:

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tool.Tests/Examples/issue.md;format=md-fenced-code-block;language=markdown -->
```markdown
## Steps to reproduce

1. Open the **settings**
2. Click `Save`

> ⚠️ The application stops responding.

> **Environment**
>
> Version 2.1 on Windows 11
```
<!-- endref -->

- Without `--output`, the Markdown is written to the standard output.
- The folder of the output file is created if needed, and the file is written in UTF-8 without a byte order mark.
- The Markdown ends with a line terminator. An empty input gives an empty output.

## Read the standard input

Without `--input`, the tool reads the document from the standard input. Pipe the description of a Jira issue to it:

```bash
curl -s -u "$JIRA_USER:$JIRA_TOKEN" "https://example.atlassian.net/rest/api/3/issue/ABC-1" | jq .fields.description | Meziantou.Framework.AtlassianDataFormat.Tool
```

## Options

Every option of the [conversion](03-ConvertingToMarkdown.md#options) is a command-line option with the same default.
The same document, converted with GitHub alerts and HTML collapsible sections:

```bash
Meziantou.Framework.AtlassianDataFormat.Tool --input issue.json --panel-style GitHubAlert --expand-style HtmlDetails
```

<!-- ref:../../tests/Meziantou.Framework.AtlassianDataFormat.Tool.Tests/Examples/issue.github.md;format=md-fenced-code-block;language=markdown -->
```markdown
## Steps to reproduce

1. Open the **settings**
2. Click `Save`

> [!WARNING]
> The application stops responding.

<details>
<summary>Environment</summary>

Version 2.1 on Windows 11

</details>
```
<!-- endref -->

| Option | Property of [AdfToMarkdownOptions](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions) |
| --- | --- |
| `--heading-style <Atx\|Setext>` | [HeadingStyle](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.HeadingStyle) |
| `--emphasis-marker <Asterisk\|Underscore>` | [EmphasisMarker](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.EmphasisMarker) |
| `--code-block-style <Fenced\|Indented>` | [CodeBlockStyle](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.CodeBlockStyle) |
| `--code-block-fence-character <character>` | [CodeBlockFenceCharacter](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.CodeBlockFenceCharacter) |
| `--unordered-list-marker <character>` | [UnorderedListMarker](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.UnorderedListMarker) |
| `--thematic-break <text>` | [ThematicBreak](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.ThematicBreak) |
| `--line-break-style <Backslash\|TrailingSpaces>` | [LineBreakStyle](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.LineBreakStyle) |
| `--panel-style <Blockquote\|GitHubAlert\|Html\|PlainText>` | [PanelStyle](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.PanelStyle) |
| `--expand-style <Blockquote\|Heading\|HtmlDetails>` | [ExpandStyle](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.ExpandStyle) |
| `--table-style <Auto\|Html\|PipeTable>` | [TableStyle](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.TableStyle) |
| `--media-rendering <AltText\|Image\|Link\|Skip>` | [MediaRendering](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.MediaRendering) |
| `--emoji-rendering <ShortName\|Text>` | [EmojiRendering](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.EmojiRendering) |
| `--task-list-style <Checkbox\|PlainText>` | [TaskListStyle](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.TaskListStyle) |
| `--decision-list-style <BulletList\|PlainText>` | [DecisionListStyle](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.DecisionListStyle) |
| `--unknown-node-handling <KeepContent\|Skip\|Throw>` | [UnknownNodeHandling](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.UnknownNodeHandling) |
| `--mention-format <format>` | [MentionFormat](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.MentionFormat) |
| `--status-format <format>` | [StatusFormat](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.StatusFormat) |
| `--date-format <format>` | [DateFormat](cref:Meziantou.Framework.AtlassianDataFormat.AdfToMarkdownOptions.DateFormat) |

`--help` lists the options with their defaults. The media and mention resolvers have no command-line option: media
stored by Atlassian is written as its alternative text, and a mention without a display name as its identifier.

## Errors

The tool exits with code 1, and writes the reason to the standard error, when the input file does not exist or the
input is not an ADF document. With `--unknown-node-handling Throw`, an unknown node is such an error.
