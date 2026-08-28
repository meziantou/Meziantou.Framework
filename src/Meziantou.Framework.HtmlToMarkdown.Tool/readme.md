# Meziantou.Framework.HtmlToMarkdown.Tool

`Meziantou.Framework.HtmlToMarkdown.Tool` converts HTML to Markdown using `Meziantou.Framework.HtmlToMarkdown`.

## Install

```bash
dotnet tool install --global Meziantou.Framework.HtmlToMarkdown.Tool
```

## Example

Convert a file to another file:

```bash
Meziantou.Framework.HtmlToMarkdown.Tool --input page.html --output page.md
```

Convert a file and write the result to the standard output:

```bash
Meziantou.Framework.HtmlToMarkdown.Tool --input page.html
```

Read the HTML from the standard input:

```bash
echo "<h1>Title</h1>" | Meziantou.Framework.HtmlToMarkdown.Tool
```

Configure the conversion:

```bash
Meziantou.Framework.HtmlToMarkdown.Tool --input page.html --heading-style Setext --emphasis-marker Underscore --emoji-shortcode-mode GitHub
```

<!-- help -->
## Help

```
Description:
  Convert HTML to Markdown using Meziantou.Framework.HtmlToMarkdown

Usage:
  Meziantou.Framework.HtmlToMarkdown.Tool [options]

Options:
  --input <input>                                                  Path to the HTML file to convert. If omitted, reads from stdin
  --output <output>                                                Path to the Markdown file to write. If omitted, writes to stdout
  --emphasis-marker <Asterisk|Underscore>                          Marker used for emphasis [default: Asterisk]
  --heading-style <Atx|Setext>                                     Style used for headings [default: Atx]
  --code-block-style <Fenced|Indented>                             Style used for code blocks [default: Fenced]
  --code-block-fence-character <code-block-fence-character>        Fence character used for fenced code blocks [default: `]
  --unordered-list-marker <unordered-list-marker>                  Marker used for unordered lists [default: -]
  --thematic-break <thematic-break>                                Text used for horizontal rules [default: ---]
  --line-break-style <Backslash|TrailingSpaces>                    Style used for line breaks [default: TrailingSpaces]
  --simple-punctuation                                             Convert smart punctuation characters to simple ASCII punctuation
  --emoji-shortcode-mode <GitHub|None|Unicode>                     Mode used to convert emoji to shortcodes [default: None]
  --unknown-element-handling <PassThrough|Strip|StripKeepContent>  Handling of unknown HTML elements [default: PassThrough]
  -?, -h, --help                                                   Show help and usage information
  --version                                                        Show version information
```
<!-- help -->