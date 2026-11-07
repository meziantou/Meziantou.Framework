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
not reach the Markdown. A heading is written on one line, so a `br` in a heading becomes a space.

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
