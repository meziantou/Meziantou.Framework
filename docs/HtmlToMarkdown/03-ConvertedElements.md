---
title: Converted elements
description: The Markdown that headings, paragraphs, emphasis, links, images, lists, code, tables and definition lists become.
---

## Blocks

Headings (`h1` to `h6`), paragraphs (`p`), block quotes (`blockquote`) and horizontal rules (`hr`) become the Markdown
blocks of the same kind, separated by an empty line:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_Blocks;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = HtmlToMarkdown.Convert("""
    <h2>Release notes</h2>
    <p>This version adds two features.</p>
    <blockquote><p>Upgrade before the end of the year.</p></blockquote>
    <hr>
    <h3>Changes</h3>
    """);
Assert.Equal("""
    ## Release notes

    This version adds two features.

    > Upgrade before the end of the year.

    ---

    ### Changes
    """, markdown);
```
<!-- endref -->

The whitespace of the HTML is collapsed, as a browser displays it: the line breaks and the indentation of the source do
not reach the Markdown, unless a style preserves them (see [Preserved whitespace](#preserved-whitespace)). A heading is
written on one line, so a `br` in a heading becomes a space.

## Preserved whitespace

A `white-space` or `white-space-collapse` declaration of a `style` attribute, on the element or on one of its ancestors,
keeps the whitespace that a browser keeps:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_CssWhiteSpace;format=md-fenced-code-block;dedent=true -->
```csharp
var html = "<p style=\"white-space: pre\">Name:  Alice\n  Role: admin</p>";

var markdown = HtmlToMarkdown.Convert(html);
Assert.Equal("Name: &nbsp;Alice  \n&nbsp;&nbsp;Role: admin", markdown);

var collapsed = HtmlToMarkdown.Convert(html, new HtmlToMarkdownOptions { UseCssWhiteSpace = false });
Assert.Equal("Name: Alice Role: admin", collapsed);
```
<!-- endref -->

- `pre`, `pre-wrap`, `break-spaces` (and `white-space-collapse: preserve`) keep the line breaks and the spaces.
  `pre-line` (`preserve-breaks`) keeps the line breaks only, and `white-space-collapse: preserve-spaces` the spaces
  only. `normal`, `nowrap` and `collapse` collapse the whitespace again inside a preserved element.
- A line break becomes a hard line break, written with the [LineBreakStyle](04-FormattingOptions.md#line-breaks), or
  `<br>` in a table cell.
- A run of spaces keeps one space and writes the others `&nbsp;`, because a Markdown renderer collapses consecutive
  spaces: all of them at the start of a line, where Markdown removes the indentation. A tab advances to the next
  multiple of 8 columns of the line. An empty line between two line breaks is written `&nbsp;`, so that it does not end
  the paragraph.
- An `!important` declaration wins over the other ones; otherwise the last valid declaration wins.
- The line breaks at the start and at the end of a block, and the text made only of whitespace between blocks, are
  removed: they render nothing.

The other CSS properties and the style sheets (`style` elements, `class`) are not read. Set
[UseCssWhiteSpace](cref:Meziantou.Framework.HtmlToMarkdownOptions.UseCssWhiteSpace) to `false` to collapse the whitespace
whatever the style.

## Inline elements

`strong` and `b` become `**`, `em` and `i` become `*`, `del`, `s` and `strike` become `~~`, and `code` becomes a code
span. `a` becomes a link, with its `title`, and `img` an image, with its `alt` text:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_Inlines;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = HtmlToMarkdown.Convert("""
    <p>
      <strong>Bold</strong>, <em>italic</em>, <del>deleted</del> and <code>code</code>.
      Read <a href="https://example.com/docs" title="Documentation">the docs</a>.
      <img src="logo.png" alt="Logo">
    </p>
    """);
Assert.Equal("""
    **Bold**, *italic*, ~~deleted~~ and `code`. Read [the docs](https://example.com/docs "Documentation"). ![Logo](logo.png)
    """, markdown);
```
<!-- endref -->

- A link without `href` gives its text only, and an image without `src` gives nothing.
- A URL that contains a space is written between `<` and `>`; the characters that would end the URL are escaped.
- Nested emphasis of the same kind, such as `<strong><b>text</b></strong>`, gives one marker.
- A code span uses more backticks than the longest run of backticks of its content.

`br` becomes a hard line break. [Line breaks](04-FormattingOptions.md#line-breaks) shows both syntaxes.

## Subscript, superscript, underline and keyboard

CommonMark has no syntax for `sub`, `sup`, `u` and `kbd`, but several Markdown dialects have one. By default, they are
[unknown elements](05-UnknownElementsAndUntrustedHtml.md). [SubscriptStyle](cref:Meziantou.Framework.SubscriptStyle),
[SuperscriptStyle](cref:Meziantou.Framework.SuperscriptStyle), [UnderlineStyle](cref:Meziantou.Framework.UnderlineStyle) and
[KeyboardStyle](cref:Meziantou.Framework.KeyboardStyle) choose the syntax of each element:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_TextLevelElements;format=md-fenced-code-block;dedent=true -->
```csharp
var html = "<p>H<sub>2</sub>O, E = mc<sup>2</sup>, <u>underlined</u>, press <kbd>Ctrl</kbd>+<kbd>C</kbd></p>";

var markdown = HtmlToMarkdown.Convert(html, new HtmlToMarkdownOptions
{
    SubscriptStyle = SubscriptStyle.Tilde,
    SuperscriptStyle = SuperscriptStyle.Caret,
    UnderlineStyle = UnderlineStyle.PlusSigns,
    KeyboardStyle = KeyboardStyle.CodeSpan,
});
Assert.Equal("H~2~O, E = mc^2^, ++underlined++, press `Ctrl`\\+`C`", markdown);

var inlineHtml = HtmlToMarkdown.Convert(html, new HtmlToMarkdownOptions
{
    SubscriptStyle = SubscriptStyle.Html,
    SuperscriptStyle = SuperscriptStyle.Html,
    UnderlineStyle = UnderlineStyle.Html,
    KeyboardStyle = KeyboardStyle.Html,
});
Assert.Equal("H<sub>2</sub>O, E = mc<sup>2</sup>, <u>underlined</u>, press <kbd>Ctrl</kbd>\\+<kbd>C</kbd>", inlineHtml);
```
<!-- endref -->

| Element | Values |
| --- | --- |
| `sub` | [UnknownElement](cref:Meziantou.Framework.SubscriptStyle.UnknownElement) (default), [Html](cref:Meziantou.Framework.SubscriptStyle.Html) (`<sub>2</sub>`), [Tilde](cref:Meziantou.Framework.SubscriptStyle.Tilde) (`~2~`, Pandoc and markdown-it-sub), [Text](cref:Meziantou.Framework.SubscriptStyle.Text) |
| `sup` | [UnknownElement](cref:Meziantou.Framework.SuperscriptStyle.UnknownElement) (default), [Html](cref:Meziantou.Framework.SuperscriptStyle.Html) (`<sup>2</sup>`), [Caret](cref:Meziantou.Framework.SuperscriptStyle.Caret) (`^2^`, Pandoc and markdown-it-sup), [Text](cref:Meziantou.Framework.SuperscriptStyle.Text) |
| `u` | [UnknownElement](cref:Meziantou.Framework.UnderlineStyle.UnknownElement) (default), [Html](cref:Meziantou.Framework.UnderlineStyle.Html) (`<u>text</u>`), [PlusSigns](cref:Meziantou.Framework.UnderlineStyle.PlusSigns) (`++text++`, markdown-it-ins), [Text](cref:Meziantou.Framework.UnderlineStyle.Text) |
| `kbd` | [UnknownElement](cref:Meziantou.Framework.KeyboardStyle.UnknownElement) (default), [Html](cref:Meziantou.Framework.KeyboardStyle.Html) (`<kbd>Ctrl</kbd>`), [CodeSpan](cref:Meziantou.Framework.KeyboardStyle.CodeSpan) (`` `Ctrl` ``), [Text](cref:Meziantou.Framework.KeyboardStyle.Text) |

- `Html` writes the tags without their attributes, around the converted content: unlike an unknown element passed
  through, the Markdown inside is still converted, whatever [UnknownElementHandling](cref:Meziantou.Framework.UnknownElementHandling) is.
- `Text` writes the converted content without any markup, and `CodeSpan` the text of the element.
- `~` and `^` do not allow spaces: the spaces of the content are escaped (`~a\ b~`). Content that the markers cannot
  hold, such as a code span, a link or a nested subscript, is written with the HTML tags instead.
- When a syntax is chosen, the characters of the text that would start it are escaped: every `~` with
  [Tilde](cref:Meziantou.Framework.SubscriptStyle.Tilde), every `^` with [Caret](cref:Meziantou.Framework.SuperscriptStyle.Caret), and `++` with
  [PlusSigns](cref:Meziantou.Framework.UnderlineStyle.PlusSigns).
- GitHub reads `~text~` as a strikethrough: use [Html](cref:Meziantou.Framework.SubscriptStyle.Html) for GitHub.

## Video, audio and iframes

`video`, `audio` and `iframe` elements are unknown elements by default.
[EmbeddedContentStyle](cref:Meziantou.Framework.EmbeddedContentStyle) writes them as raw HTML whatever
[UnknownElementHandling](cref:Meziantou.Framework.UnknownElementHandling) is ([Html](cref:Meziantou.Framework.EmbeddedContentStyle.Html)), as a link
([Link](cref:Meziantou.Framework.EmbeddedContentStyle.Link)), or removes them ([Remove](cref:Meziantou.Framework.EmbeddedContentStyle.Remove)):

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_EmbeddedContent;format=md-fenced-code-block;dedent=true -->
```csharp
var html = """
    <p>Demo: <video controls title="Product demo"><source src="demo.mp4" type="video/mp4"></video></p>
    <iframe src="https://example.com/map"></iframe>
    """;

var links = HtmlToMarkdown.Convert(html, new HtmlToMarkdownOptions { EmbeddedContentStyle = EmbeddedContentStyle.Link });
Assert.Equal("""
    Demo: [Product demo](demo.mp4)

    [https://example.com/map](https://example.com/map)
    """, links);

var removed = HtmlToMarkdown.Convert(html, new HtmlToMarkdownOptions { EmbeddedContentStyle = EmbeddedContentStyle.Remove });
Assert.Equal("Demo:", removed);
```
<!-- endref -->

The URL of the link is the `src` attribute, or, for a `video` or an `audio`, the `src` of its first `source` element.
Its text is the `title` attribute, or the URL. An element without a URL is removed: the fallback content of a `video`
("Your browser does not support the video") is not written.

## Escaped text

The characters of the text that Markdown would read as syntax are escaped with a backslash, so the Markdown renders the
text of the HTML:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_Escaping;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = HtmlToMarkdown.Convert("""
    <p>1. Use *.txt files</p>
    <p># is not a heading, and [this] is not a link</p>
    """);
Assert.Equal("""
    1\. Use \*.txt files

    \# is not a heading, and \[this\] is not a link
    """, markdown);
```
<!-- endref -->

`\`, `*`, `_`, `` ` ``, `[`, `]`, `<`, `>` and `|` are always escaped. `#`, `+` and `-` are escaped at the start of a
text, `-` and `~` in runs of two, a number followed by `.` or `)` at the start of a line, and `&` when it could start
an entity: it is written `&amp;`, so the text `&copy;` does not become `©`. The rules are conservative: the output can hold an escape that is
not needed, such as `\+` after a link, which renders the same.

## Lists and task lists

`ul` and `ol` become lists, and the lists nest. An ordered list keeps its `start` number. A checkbox (`input
type="checkbox"`) becomes the `[ ]` or `[x]` of a task list:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_Lists;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = HtmlToMarkdown.Convert("""
    <ul>
      <li>Fruits
        <ol start="3">
          <li>Apple</li>
          <li>Banana</li>
        </ol>
      </li>
      <li><input type="checkbox" checked> Done</li>
      <li><input type="checkbox"> To do</li>
    </ul>
    """);
Assert.Equal("""
    - Fruits
      3. Apple
      4. Banana
    - [x] Done
    - [ ] To do
    """, markdown);
```
<!-- endref -->

A list is tight (no empty line between the items), unless one of its items holds a block other than a nested list,
such as a `p`: the items are then separated by an empty line. A `start` that is not a valid Markdown list number (negative, or more than nine digits) is replaced by 1. The other
`input` elements are removed.

## Code blocks

`pre` becomes a code block. The `language-*` class, or the `data-language` attribute, of the `pre` or of its `code`
element gives the language of a fenced block:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_CodeBlocks;format=md-fenced-code-block;dedent=true -->
````csharp
var html = """
    <pre><code class="language-csharp">var x = 1;
    Console.WriteLine(x);
    </code></pre>
    """;

var fenced = HtmlToMarkdown.Convert(html);
Assert.Equal("""
    ```csharp
    var x = 1;
    Console.WriteLine(x);
    ```
    """, fenced);

var indented = HtmlToMarkdown.Convert(html, new HtmlToMarkdownOptions { CodeBlockStyle = CodeBlockStyle.Indented });
Assert.Equal("""
        var x = 1;
        Console.WriteLine(x);
    """, indented);
````
<!-- endref -->

- The content is the text of the block: the elements of a highlighted block (`<span class="keyword">`) are removed,
  their text is kept, and nothing is escaped.
- The fence is longer than any run of fence characters in the code, so the code cannot close it.
- A language made of other characters than letters, digits, `-`, `+`, `_`, `.` and `#` is dropped, so that it cannot
  end the fence line.

## Tables

`table` becomes a pipe table. The first row is the header; the alignment of its cells, from `style="text-align: …"` or
the `align` attribute, gives the alignment of the columns:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_Tables;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = HtmlToMarkdown.Convert("""
    <table>
      <thead>
        <tr><th>Name</th><th style="text-align: right">Price</th><th align="center">Stock</th></tr>
      </thead>
      <tbody>
        <tr><td>Apple</td><td>$1.20</td><td>Yes</td></tr>
        <tr><td>Banana<br>(organic)</td><td>$0.80</td><td>No</td></tr>
      </tbody>
    </table>
    """);
Assert.Equal("""
    | Name | Price | Stock |
    | --- | ---: | :---: |
    | Apple | $1.20 | Yes |
    | Banana<br>(organic) | $0.80 | No |
    """, markdown);
```
<!-- endref -->

- The rows of `thead` come first, then the rows of `tbody` (or the rows directly in the table), then the rows of
  `tfoot`. Without `thead`, the first row is the header.
- A row of a pipe table is one line: a `br`, or several blocks in a cell, become `<br>`.
- The rows that have fewer cells than the longest row are completed with empty cells.

### Spanned cells

A pipe table has no spans: by default, `colspan` and `rowspan` are ignored, and the cells after a spanning cell move to
the left. [TableSpanHandling](cref:Meziantou.Framework.TableSpanHandling) keeps the columns aligned. A spanning cell fills
every position it spans, and the following cells skip the positions that the cells of the previous rows span, as in
HTML. [EmptyCells](cref:Meziantou.Framework.TableSpanHandling.EmptyCells) writes the content in the first position and
leaves the others empty; [Duplicate](cref:Meziantou.Framework.TableSpanHandling.Duplicate) repeats it in every position:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_TableSpans;format=md-fenced-code-block;dedent=true -->
```csharp
var html = """
    <table>
      <tr><th>Fruit</th><th colspan="2">Price</th></tr>
      <tr><td rowspan="2">Apple</td><td>$1.20</td><td>each</td></tr>
      <tr><td>$10</td><td>per box</td></tr>
    </table>
    """;

var emptyCells = HtmlToMarkdown.Convert(html, new HtmlToMarkdownOptions { TableSpanHandling = TableSpanHandling.EmptyCells });
Assert.Equal("""
    | Fruit | Price |  |
    | --- | --- | --- |
    | Apple | $1.20 | each |
    |  | $10 | per box |
    """, emptyCells);

var duplicate = HtmlToMarkdown.Convert(html, new HtmlToMarkdownOptions { TableSpanHandling = TableSpanHandling.Duplicate });
Assert.Equal("""
    | Fruit | Price | Price |
    | --- | --- | --- |
    | Apple | $1.20 | each |
    | Apple | $10 | per box |
    """, duplicate);
```
<!-- endref -->

- The alignment of a spanning header cell applies to every column it spans.
- As in HTML, `colspan` is at most 1000, and a `rowspan` stops at the end of its `thead`, `tbody` or `tfoot`;
  `rowspan="0"` spans to that end.
- A span that would make the table larger than 1,000,000 cells (rows × columns) is ignored, so that a small document
  cannot produce a huge table.

### Complex tables

[ComplexTableHandling.Html](cref:Meziantou.Framework.ComplexTableHandling.Html) writes the tables that a pipe table cannot
represent as raw HTML, instead of flattening them:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_ComplexTables;format=md-fenced-code-block;dedent=true -->
```csharp
var html = """
    <table>
      <tr><th>Step</th><th>Details</th></tr>
      <tr><td>Install</td><td><ul><li>Download</li><li>Run the setup</li></ul></td></tr>
    </table>
    """;

var markdown = HtmlToMarkdown.Convert(html, new HtmlToMarkdownOptions { ComplexTableHandling = ComplexTableHandling.Html });
Assert.Equal("""
    <table>
      <tbody><tr><th>Step</th><th>Details</th></tr>
      <tr><td>Install</td><td><ul><li>Download</li><li>Run the setup</li></ul></td></tr>
    </tbody></table>
    """, markdown);
```
<!-- endref -->

A table is complex when:

- it has several header rows (`tr` in `thead`);
- a cell holds a heading, a list, a code block, a block quote, a table, a horizontal rule, a definition list or several
  paragraphs;
- a cell spans several columns or rows, and [TableSpanHandling](cref:Meziantou.Framework.TableSpanHandling) is
  [Ignore](cref:Meziantou.Framework.TableSpanHandling.Ignore). With the other values, the spans are written in the pipe table.

The other tables stay pipe tables. The HTML is the one of the table, with its attributes, without its `script`,
`style` and `noscript` elements, as for an [unknown element](05-UnknownElementsAndUntrustedHtml.md): sanitize untrusted
HTML first. A blank line would end the HTML block, so a blank line of a text, such as an empty line of a `pre`, gets
an empty comment (`<!---->`), which renders nothing. The blank lines of comments, attribute values and `textarea`
elements are removed. The same applies to the unknown elements and the embedded content written as raw HTML.

## Definition lists

`dl` becomes a definition list: each `dt` term on its line, followed by its `dd` definitions, prefixed with `:`:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_DefinitionLists;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = HtmlToMarkdown.Convert("""
    <dl>
      <dt>HTML</dt>
      <dd>The markup language of the web.</dd>
      <dt>CSS</dt>
      <dd>The style sheet language of the web.</dd>
    </dl>
    """);
Assert.Equal("""
    HTML
    :   The markup language of the web.

    CSS
    :   The style sheet language of the web.
    """, markdown);
```
<!-- endref -->

The lines that follow the first line of a definition are indented, so a definition can hold several paragraphs.

## Containers, scripts and styles

The structural elements are replaced by their content: `div`, `article`, `section`, `nav`, `aside`, `header`,
`footer`, `main`, `details`, `summary`, `figure` and `figcaption` give their blocks, and `span` its inline content.
`script`, `style` and `noscript` are removed with their content:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/HtmlToMarkdownConverterTests.cs;region=HtmlToMarkdown_Containers;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = HtmlToMarkdown.Convert("""
    <article>
      <header><h1>Title</h1></header>
      <section><p>Some <span class="highlight">text</span>.</p></section>
      <script>alert('removed')</script>
    </article>
    """);
Assert.Equal("""
    # Title

    Some text.
    """, markdown);
```
<!-- endref -->

The attributes of these elements (`class`, `id`...) are not kept. Any other element is an unknown element: see
[Unknown elements and untrusted HTML](05-UnknownElementsAndUntrustedHtml.md).
