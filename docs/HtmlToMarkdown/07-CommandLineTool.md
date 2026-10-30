---
title: Command-line tool
description: Convert an HTML file or the standard input to Markdown from the command line, with the options of the library.
---

`Meziantou.Framework.HtmlToMarkdown.Tool` converts HTML to Markdown without writing code: use it in scripts, or to
convert pages saved from a browser. It needs .NET 10 or later.

## Install the tool

Install it as a global tool:

```bash
dotnet tool install --global Meziantou.Framework.HtmlToMarkdown.Tool
```

The command has the name of the package. To pin the version of a repository, install it as a local tool instead
(`dotnet new tool-manifest`, then `dotnet tool install Meziantou.Framework.HtmlToMarkdown.Tool`) and run it with
`dotnet tool run Meziantou.Framework.HtmlToMarkdown.Tool`.

## Convert a file

`--input` is the HTML file, and `--output` the Markdown file to write. The folder of the output file is created if
needed:

```bash
Meziantou.Framework.HtmlToMarkdown.Tool --input page.html --output page.md
```

The input can be a complete page: the tool converts the content of its `body`, and ignores the `head`. This page:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tool.Tests/Examples/page.html;format=md-fenced-code-block;language=html -->
```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Getting started</title>
  <style>body { font-family: sans-serif; }</style>
</head>
<body>
  <h1>Getting started</h1>
  <p>Install the package, then call <code>Convert</code>:</p>
  <pre><code class="language-bash">dotnet add package MyLibrary</code></pre>
  <ul>
    <li><a href="install.html">Installation</a></li>
    <li><a href="usage.html">Usage</a></li>
  </ul>
</body>
</html>
```
<!-- endref -->

gives this Markdown:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tool.Tests/Examples/page.md;format=md-fenced-code-block;language=markdown -->
````markdown
# Getting started

Install the package, then call `Convert`:

```bash
dotnet add package MyLibrary
```

- [Installation](install.html)
- [Usage](usage.html)
````
<!-- endref -->

The output file is written in UTF-8, without byte order mark, and ends with a line break. A fragment, without `html`
or `body` elements, is converted the same way.

## Use the standard input and output

Without `--output`, the Markdown is written to the standard output. Without `--input`, the HTML is read from the
standard input, so the tool can be part of a pipeline:

```bash
echo "<h1>Title</h1>" | Meziantou.Framework.HtmlToMarkdown.Tool
```

With `--input`, the standard input is not read.

## Set the formatting options

Each option of `HtmlToMarkdownOptions` has a command-line option, with the same values and the same defaults:

```bash
Meziantou.Framework.HtmlToMarkdown.Tool --input page.html --heading-style Setext --emphasis-marker Underscore --emoji-shortcode-mode GitHub
```

| Option | Values | Library option |
| --- | --- | --- |
| `--emphasis-marker` | `Asterisk` (default), `Underscore` | `EmphasisMarker` |
| `--heading-style` | `Atx` (default), `Setext` | `HeadingStyle` |
| `--code-block-style` | `Fenced` (default), `Indented` | `CodeBlockStyle` |
| `--code-block-fence-character` | `` ` `` (default), `~` | `CodeBlockFenceCharacter` |
| `--unordered-list-marker` | `-` (default), `*`, `+` | `UnorderedListMarker` |
| `--thematic-break` | The text of a horizontal rule, `---` by default | `ThematicBreak` |
| `--line-break-style` | `TrailingSpaces` (default), `Backslash` | `LineBreakStyle` |
| `--simple-punctuation` | A flag: replaces the typographic punctuation with ASCII characters | `UseSimplePunctuation` |
| `--emoji-shortcode-mode` | `None` (default), `GitHub`, `Unicode` | `EmojiShortcodeMode` |
| `--unknown-element-handling` | `PassThrough` (default), `Strip`, `StripKeepContent` | `UnknownElementHandling` |

[Formatting options](04-FormattingOptions.md) and [Unknown elements](05-UnknownElementsAndUntrustedHtml.md) describe
what they do. Quote the characters that your shell reads, such as `` ` `` and `*`: `--unordered-list-marker "*"`.

## Other options

| Option | Description |
| --- | --- |
| `--input <path>` | The HTML file to convert. When omitted, the standard input. |
| `--output <path>` | The Markdown file to write. When omitted, the standard output. |
| `-?`, `-h`, `--help` | Shows the help. |
| `--version` | Shows the version of the tool. |

## Errors

The tool exits with code 0 when it succeeds. A missing input file gives exit code 1 and a message on the standard
error. A value that is not valid for an option, such as `--unordered-list-marker ab`, gives a message on the standard
error and a non-zero exit code.
