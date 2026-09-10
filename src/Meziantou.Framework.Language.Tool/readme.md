# Meziantou.Framework.Language.Tool

`Meziantou.Framework.Language.Tool` dumps a syntax tree as JSON, so a parse can be inspected, diffed, or piped into a script without writing any C#. It parses with `Meziantou.Framework.Language.Json`, `Meziantou.Framework.Language.Xml`, `Meziantou.Framework.Language.Regex`, and `Meziantou.Framework.Language.Shell`.

## Install

```bash
dotnet tool install --global Meziantou.Framework.Language.Tool
```

## Example

The `syntax` command dumps a syntax tree. It is the only command today; the tool is laid out this way so other commands can join it.

Dump a file, detecting the language from its extension:

```bash
Meziantou.Framework.Language.Tool syntax --input app.csproj
```

Write the dump to a file instead of the standard output:

```bash
Meziantou.Framework.Language.Tool syntax --input appsettings.json --output tree.json
```

Read from the standard input, where the language cannot be detected and must be set:

```bash
echo '{"a":[1,2]}' | Meziantou.Framework.Language.Tool syntax --language json
```

Parse a regular expression in a given dialect, or a script in a given shell dialect:

```bash
Meziantou.Framework.Language.Tool syntax --language regex-pcre --input pattern.txt
Meziantou.Framework.Language.Tool syntax --language zsh --input script
```

Keep only the shape of the tree:

```bash
Meziantou.Framework.Language.Tool syntax --input page.svg --no-tokens --no-text
```

## Output

The JSON is written on a single line. The document is the same shape for every language, because the four libraries share one syntax tree:

```jsonc
{
  "language": "shell",   // json | xml | regex | shell
  "dialect": "bash",     // regex and shell only
  "diagnostics": [
    { "id": "…", "severity": "Error", "message": "…",
      "span": { "start": 3, "length": 1 },
      "lineSpan": { "start": { "line": 0, "character": 3 }, "end": { "line": 0, "character": 4 } } }
  ],
  "root": {
    "kind": "Command",
    "type": "ShellCommandSyntax",
    "span": { "start": 0, "length": 7 },
    "fullSpan": { "start": 0, "length": 8 },
    "text": "echo hi",
    "children": [
      { "kind": "IdentifierToken", "type": "SyntaxToken", "text": "echo",
        "span": { "start": 0, "length": 4 }, "fullSpan": { "start": 0, "length": 4 },
        "trailingTrivia": [ { "kind": "WhitespaceTrivia", "text": " ", "span": { "start": 4, "length": 1 } } ] }
    ]
  }
}
```

`children` holds the node's nodes **and** tokens in source order, the way the tree itself stores them. A token is told apart by its `type`, which is always `SyntaxToken`; a node's `type` is its own class name.

`span` and `fullSpan` differ the way they do everywhere in the tree: `fullSpan` covers the trivia, `span` does not. So a node's `text` — its full text — is sliced by `fullSpan`, while a token's `text` excludes its trivia and is sliced by `span`.

Empty collections and default values are left out, and `valueText` appears only when it differs from `text`.

A parse problem is reported in `diagnostics` and does not fail the run: the four parsers never throw, and a tree is produced for invalid input too. The exit code is non-zero only for a usage or I/O problem.

Regular expressions add `captures`, and `patternOptions` when any option is in effect. They are never detected from an extension, so `--language regex…` is required for them.

<!-- help -->
## Help

```
Description:
  Inspect JSON, XML, regular expression, and shell documents

Usage:
  Meziantou.Framework.Language.Tool [command] [options]

Options:
  -?, -h, --help  Show help and usage information
  --version       Show version information

Commands:
  syntax  Dump the syntax tree of a JSON, XML, regular expression, or shell document as JSON
```

### syntax

```
Description:
  Dump the syntax tree of a JSON, XML, regular expression, or shell document as JSON

Usage:
  Meziantou.Framework.Language.Tool syntax [options]

Options:
  --input <input>        Path to the file to parse. If omitted, reads from stdin
  --output <output>      Path to the JSON file to write. If omitted, writes to stdout
  --language <language>  Language to parse the input as. If omitted, it is detected from the extension of --input, which is why it is required when reading from stdin. One of: json, xml, regex, regex-dotnet, regex-javascript, regex-pcre, regex-ere, regex-bre, sh, bash, zsh, powershell, pwsh, cmd
  --no-tokens            Omit the tokens of each node, and the trivia they carry
  --no-trivia            Omit the leading and trailing trivia of each token
  --no-text              Omit the source text of each node, token, and trivia
  --no-spans             Omit the source spans of each node, token, and trivia
  -?, -h, --help         Show help and usage information
```
<!-- help -->