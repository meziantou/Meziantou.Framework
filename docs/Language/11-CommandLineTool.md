---
title: Command-line tool
description: Dump the syntax tree of a CSS, INI, JSON, TOML, XML, regular expression or shell file as JSON, without writing code.
---

`Meziantou.Framework.Language.Tool` parses a file with the language packages and writes its syntax tree and its
diagnostics as JSON. Use it to see how a document is parsed before you write code against its tree, to check a file in a
script, or to compare two parses.

## Install the tool

Install it as a global tool:

<InstallDotnetTool Id="Meziantou.Framework.Language.Tool" />

## Dump a file

The `syntax` command parses `--input` and writes the JSON to the standard output. The language is detected from the
extension of the file:

```bash
Meziantou.Framework.Language.Tool syntax --input app.csproj
```

Add `--output` to write the JSON to a file instead:

```bash
Meziantou.Framework.Language.Tool syntax --input appsettings.json --output tree.json
```

Without `--input`, the tool reads the standard input. It cannot detect the language then, so set it with `--language`:

```bash
echo '{"a":[1,2]}' | Meziantou.Framework.Language.Tool syntax --language json
```

| Extensions | Language |
| --- | --- |
| `.css` | CSS |
| `.ini`, `.editorconfig`, `.gitconfig`, `.npmrc` | INI |
| `.json`, `.jsonc`, `.json5`, `.webmanifest` | JSON |
| `.toml` | TOML |
| `.xml`, `.xsd`, `.xsl`, `.xslt`, `.svg`, `.rss`, `.atom`, `.config`, `.csproj`, `.vbproj`, `.fsproj`, `.props`, `.targets`, `.nuspec`, `.resx`, `.plist`, `.xaml` | XML |
| `.sh`, `.bash`, `.zsh` | sh, bash, zsh |
| `.ps1`, `.psm1`, `.psd1` | PowerShell 7 and later (`pwsh`) |
| `.bat`, `.cmd` | cmd |

`--language` overrides the detection. Its values are `css`, `ini`, `json`, `toml`, `xml`, the regular expression
dialects `regex` (.NET), `regex-dotnet`, `regex-javascript`, `regex-pcre`, `regex-ere` and `regex-bre`, and the shells
`sh`, `bash`, `zsh`, `powershell`, `pwsh` and `cmd`. A regular expression is never detected from an extension: set
`--language` to parse one.

```bash
Meziantou.Framework.Language.Tool syntax --language regex-pcre --input pattern.txt
```

## Read the output

The JSON is written on a single line, with the same shape for every language. This file:

<!-- ref:../../tests/Meziantou.Framework.Language.Tool.Tests/Examples/Comment.json;format=md-fenced-code-block;language=json -->
```json
true // yes
```
<!-- endref -->

gives this tree, formatted here for reading:

<!-- ref:../../tests/Meziantou.Framework.Language.Tool.Tests/Examples/Comment.output.json;format=md-fenced-code-block;language=json -->
```json
{
  "language": "json",
  "root": {
    "kind": "JsonDocument",
    "type": "JsonDocumentSyntax",
    "span": {
      "start": 0,
      "length": 12
    },
    "fullSpan": {
      "start": 0,
      "length": 12
    },
    "text": "true // yes\n",
    "children": [
      {
        "kind": "JsonTrueLiteral",
        "type": "JsonLiteralSyntax",
        "span": {
          "start": 0,
          "length": 4
        },
        "fullSpan": {
          "start": 0,
          "length": 12
        },
        "text": "true // yes\n",
        "children": [
          {
            "kind": "TrueKeyword",
            "type": "SyntaxToken",
            "text": "true",
            "span": {
              "start": 0,
              "length": 4
            },
            "fullSpan": {
              "start": 0,
              "length": 12
            },
            "trailingTrivia": [
              {
                "kind": "WhitespaceTrivia",
                "text": " ",
                "span": {
                  "start": 4,
                  "length": 1
                }
              },
              {
                "kind": "SingleLineCommentTrivia",
                "text": "// yes",
                "span": {
                  "start": 5,
                  "length": 6
                }
              },
              {
                "kind": "EndOfLineTrivia",
                "text": "\n",
                "span": {
                  "start": 11,
                  "length": 1
                }
              }
            ]
          }
        ]
      },
      {
        "kind": "EndOfFileToken",
        "type": "SyntaxToken",
        "text": "",
        "span": {
          "start": 12,
          "length": 0
        },
        "fullSpan": {
          "start": 12,
          "length": 0
        }
      }
    ]
  }
}
```
<!-- endref -->

- `language` is `css`, `ini`, `json`, `toml`, `xml`, `regex` or `shell`, and `dialect` gives the dialect of a regular
  expression or a script.
- Each node has its `kind`, its `type` (its class name), its `span` and `fullSpan`, its `text`, and its `children`: its
  nodes and tokens in the order of the text. A token has the `type` `SyntaxToken`, and its trivia in `leadingTrivia` and
  `trailingTrivia`.
- `fullSpan` covers the trivia and `span` does not. The `text` of a node is its full text, with its trivia; the `text` of
  a token is its text without its trivia.
- Empty collections and default values are left out, and `valueText` appears only when it differs from `text`.
- A regular expression adds its `captures`, and its `patternOptions` when an option is in effect.

## Keep only what you need

Four options remove parts of the output:

| Option | Removes |
| --- | --- |
| `--no-tokens` | The tokens of each node, and their trivia |
| `--no-trivia` | The trivia of each token |
| `--no-text` | The text of each node, token and trivia |
| `--no-spans` | The spans of each node, token and trivia |

With `--no-tokens --no-spans`, the output shows the shape of the tree and the diagnostics. This file has a missing value:

<!-- ref:../../tests/Meziantou.Framework.Language.Tool.Tests/Examples/MissingValue.json;format=md-fenced-code-block;language=json -->
```json
{"port": }
```
<!-- endref -->

```bash
Meziantou.Framework.Language.Tool syntax --input MissingValue.json --no-tokens --no-spans
```

<!-- ref:../../tests/Meziantou.Framework.Language.Tool.Tests/Examples/MissingValue.output.json;format=md-fenced-code-block;language=json -->
```json
{
  "language": "json",
  "diagnostics": [
    {
      "id": "JSON0007",
      "severity": "Error",
      "message": "Expected a JSON value."
    }
  ],
  "root": {
    "kind": "JsonDocument",
    "type": "JsonDocumentSyntax",
    "text": "{\"port\": }\n",
    "children": [
      {
        "kind": "JsonObject",
        "type": "JsonObjectSyntax",
        "text": "{\"port\": }\n",
        "children": [
          {
            "kind": "JsonMember",
            "type": "JsonMemberSyntax",
            "text": "\"port\": ",
            "children": [
              {
                "kind": "JsonSkippedText",
                "type": "JsonSkippedTextSyntax",
                "isMissing": true,
                "text": ""
              }
            ]
          }
        ]
      }
    ]
  }
}
```
<!-- endref -->

The missing value is a node with `isMissing`, and the diagnostic gives its identifier, severity and message. With the
spans, it also gives its `span` and its `lineSpan`, with zero-based lines and characters.

## Exit code

A document with errors is not a failure: its diagnostics are in the output, and the exit code is 0. The exit code is not 0
only for a usage or input/output problem, such as a missing file or a language that cannot be detected.
