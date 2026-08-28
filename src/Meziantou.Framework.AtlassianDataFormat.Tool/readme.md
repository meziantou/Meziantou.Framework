# Meziantou.Framework.AtlassianDataFormat.Tool

`Meziantou.Framework.AtlassianDataFormat.Tool` converts Atlassian Document Format (ADF) documents — the JSON rich-text format used by Jira, Confluence, and Bitbucket — to Markdown using `Meziantou.Framework.AtlassianDataFormat`.

## Install

```bash
dotnet tool install --global Meziantou.Framework.AtlassianDataFormat.Tool
```

## Example

Convert a file to another file:

```bash
Meziantou.Framework.AtlassianDataFormat.Tool --input issue.json --output issue.md
```

Convert a file and write the result to the standard output:

```bash
Meziantou.Framework.AtlassianDataFormat.Tool --input issue.json
```

Read the ADF document from the standard input, such as the description of a Jira issue:

```bash
curl -s -u "$JIRA_USER:$JIRA_TOKEN" "https://example.atlassian.net/rest/api/3/issue/ABC-1" \
  | jq .fields.description \
  | Meziantou.Framework.AtlassianDataFormat.Tool
```

Configure the conversion:

```bash
Meziantou.Framework.AtlassianDataFormat.Tool --input issue.json --panel-style GitHubAlert --expand-style HtmlDetails --table-style Auto
```

<!-- help -->
## Help

```
Description:
  Convert Atlassian Document Format (ADF) documents to Markdown using Meziantou.Framework.AtlassianDataFormat

Usage:
  Meziantou.Framework.AtlassianDataFormat.Tool [options]

Options:
  --input <input>                                            Path to the ADF JSON file to convert. If omitted, reads from stdin
  --output <output>                                          Path to the Markdown file to write. If omitted, writes to stdout
  --heading-style <Atx|Setext>                               Style used for headings [default: Atx]
  --emphasis-marker <Asterisk|Underscore>                    Marker used for emphasis [default: Asterisk]
  --code-block-style <Fenced|Indented>                       Style used for code blocks [default: Fenced]
  --code-block-fence-character <code-block-fence-character>  Fence character used for fenced code blocks [default: `]
  --unordered-list-marker <unordered-list-marker>            Marker used for unordered lists [default: -]
  --thematic-break <thematic-break>                          Text used for horizontal rules [default: ---]
  --line-break-style <Backslash|TrailingSpaces>              Style used for line breaks [default: TrailingSpaces]
  --panel-style <Blockquote|GitHubAlert|Html|PlainText>      Style used for panels [default: Blockquote]
  --expand-style <Blockquote|Heading|HtmlDetails>            Style used for collapsible sections [default: Blockquote]
  --table-style <Auto|Html|PipeTable>                        Style used for tables [default: PipeTable]
  --media-rendering <AltText|Image|Link|Skip>                Rendering used for media items [default: Image]
  --emoji-rendering <ShortName|Text>                         Rendering used for emoji [default: Text]
  --task-list-style <Checkbox|PlainText>                     Style used for task lists [default: Checkbox]
  --decision-list-style <BulletList|PlainText>               Style used for decision lists [default: BulletList]
  --unknown-node-handling <KeepContent|Skip|Throw>           Handling of nodes whose type is not part of the supported schema [default: Skip]
  --mention-format <mention-format>                          Format used for mentions, where {text} is the display name and {id} the account identifier [default: @{text}]
  --status-format <status-format>                            Format used for status lozenges, where {text} is the text and {color} the color [default: `{text}`]
  --date-format <date-format>                                Format used for dates, applied with the invariant culture [default: yyyy-MM-dd]
  -?, -h, --help                                             Show help and usage information
  --version                                                  Show version information
```
<!-- help -->