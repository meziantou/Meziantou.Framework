namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class MarkdownHighlighterTests
{

    [Fact]
    public void Heading_H1()
    {
        AssertHighlighter("markdown",
"""
# Heading 1
""",
"""
<span class="hljs-section"># Heading 1</span>
""");
    }

    [Fact]
    public void Heading_H2()
    {
        AssertHighlighter("markdown",
"""
## Heading 2
""",
"""
<span class="hljs-section">## Heading 2</span>
""");
    }

    [Fact]
    public void Heading_H3()
    {
        AssertHighlighter("markdown",
"""
### Heading 3
""",
"""
<span class="hljs-section">### Heading 3</span>
""");
    }

    [Fact]
    public void Heading_H4()
    {
        AssertHighlighter("markdown",
"""
#### Heading 4
""",
"""
<span class="hljs-section">#### Heading 4</span>
""");
    }

    [Fact]
    public void Heading_H5()
    {
        AssertHighlighter("markdown",
"""
##### Heading 5
""",
"""
<span class="hljs-section">##### Heading 5</span>
""");
    }

    [Fact]
    public void Heading_H6()
    {
        AssertHighlighter("markdown",
"""
###### Heading 6
""",
"""
<span class="hljs-section">###### Heading 6</span>
""");
    }

    [Fact]
    public void Heading_SetextH1()
    {
        AssertHighlighter("markdown",
"""
Heading 1
=========
""",
"""
<span class="hljs-section">Heading 1
=========</span>
""");
    }

    [Fact]
    public void Heading_SetextH2()
    {
        AssertHighlighter("markdown",
"""
Heading 2
---------
""",
"""
<span class="hljs-section">Heading 2
---------</span>
""");
    }

    [Fact]
    public void Heading_TrailingHash()
    {
        AssertHighlighter("markdown",
"""
## Heading ##
""",
"""
<span class="hljs-section">## Heading ##</span>
""");
    }

    [Fact]
    public void Heading_WithLink()
    {
        AssertHighlighter("markdown",
"""
## [Link in heading](https://example.com)
""",
"""
<span class="hljs-section">## [<span class="hljs-string">Link in heading</span>](<span class="hljs-link">https://example.com</span>)</span>
""");
    }

    [Fact]
    public void Emphasis_BoldStars()
    {
        AssertHighlighter("markdown",
"""
**bold text**
""",
"""
<span class="hljs-strong">**bold text**</span>
""");
    }

    [Fact]
    public void Emphasis_BoldUnderscores()
    {
        AssertHighlighter("markdown",
"""
__bold text__
""",
"""
<span class="hljs-strong">__bold text__</span>
""");
    }

    [Fact]
    public void Emphasis_ItalicStars()
    {
        AssertHighlighter("markdown",
"""
*italic text*
""",
"""
<span class="hljs-emphasis">*italic text*</span>
""");
    }

    [Fact]
    public void Emphasis_ItalicUnderscores()
    {
        AssertHighlighter("markdown",
"""
_italic text_
""",
"""
<span class="hljs-emphasis">_italic text_</span>
""");
    }

    [Fact]
    public void Emphasis_BoldItalic()
    {
        AssertHighlighter("markdown",
"""
***bold italic***
""",
"""
<span class="hljs-strong">**<span class="hljs-emphasis">*bold italic*</span>**</span>
""");
    }

    [Fact]
    public void Emphasis_Strikethrough()
    {
        AssertHighlighter("markdown",
"""
~~strikethrough~~
""",
"""
~~strikethrough~~
""");
    }

    [Fact]
    public void Emphasis_InlineCode()
    {
        AssertHighlighter("markdown",
"""
Use the `printf` function.
""",
"""
Use the <span class="hljs-code">`printf`</span> function.
""");
    }

    [Fact]
    public void Emphasis_InlineCodeDouble()
    {
        AssertHighlighter("markdown",
"""
Use ``something with ` backtick``.
""",
"""
Use <span class="hljs-code">``something with `</span> backtick``.
""");
    }

    [Fact]
    public void Emphasis_Combined()
    {
        AssertHighlighter("markdown",
"""
This is **bold and *italic***.
""",
"""
This is <span class="hljs-strong">**bold and <span class="hljs-emphasis">*italic*</span>**</span>.
""");
    }

    [Fact]
    public void List_BulletDash()
    {
        AssertHighlighter("markdown",
"""
- one
- two
- three
""",
"""
<span class="hljs-bullet">-</span> one
<span class="hljs-bullet">-</span> two
<span class="hljs-bullet">-</span> three
""");
    }

    [Fact]
    public void List_BulletStar()
    {
        AssertHighlighter("markdown",
"""
* one
* two
* three
""",
"""
<span class="hljs-bullet">*</span> one
<span class="hljs-bullet">*</span> two
<span class="hljs-bullet">*</span> three
""");
    }

    [Fact]
    public void List_BulletPlus()
    {
        AssertHighlighter("markdown",
"""
+ one
+ two
+ three
""",
"""
<span class="hljs-bullet">+</span> one
<span class="hljs-bullet">+</span> two
<span class="hljs-bullet">+</span> three
""");
    }

    [Fact]
    public void List_Ordered()
    {
        AssertHighlighter("markdown",
"""
1. one
2. two
3. three
""",
"""
<span class="hljs-bullet">1.</span> one
<span class="hljs-bullet">2.</span> two
<span class="hljs-bullet">3.</span> three
""");
    }

    [Fact]
    public void List_OrderedStartAt()
    {
        AssertHighlighter("markdown",
"""
10. ten
11. eleven
12. twelve
""",
"""
<span class="hljs-bullet">10.</span> ten
<span class="hljs-bullet">11.</span> eleven
<span class="hljs-bullet">12.</span> twelve
""");
    }

    [Fact]
    public void List_Nested()
    {
        AssertHighlighter("markdown",
"""
- one
  - one.a
  - one.b
- two
""",
"""
<span class="hljs-bullet">-</span> one
<span class="hljs-bullet">  -</span> one.a
<span class="hljs-bullet">  -</span> one.b
<span class="hljs-bullet">-</span> two
""");
    }

    [Fact]
    public void List_NestedMixed()
    {
        AssertHighlighter("markdown",
"""
1. first
   - sub-bullet
   - sub-bullet
2. second
""",
"""
<span class="hljs-bullet">1.</span> first
<span class="hljs-bullet">   -</span> sub-bullet
<span class="hljs-bullet">   -</span> sub-bullet
<span class="hljs-bullet">2.</span> second
""");
    }

    [Fact]
    public void List_TaskList()
    {
        AssertHighlighter("markdown",
"""
- [ ] todo
- [x] done
- [ ] another todo
""",
"""
<span class="hljs-bullet">-</span> [ ] todo
<span class="hljs-bullet">-</span> [x] done
<span class="hljs-bullet">-</span> [ ] another todo
""");
    }

    [Fact]
    public void List_WithParagraph()
    {
        AssertHighlighter("markdown",
"""
- item one

  Paragraph belonging to item one.

- item two
""",
"""
<span class="hljs-bullet">-</span> item one

  Paragraph belonging to item one.

<span class="hljs-bullet">-</span> item two
""");
    }

    [Fact]
    public void Link_Inline()
    {
        AssertHighlighter("markdown",
"""
[example](https://example.com)
""",
"""
[<span class="hljs-string">example</span>](<span class="hljs-link">https://example.com</span>)
""");
    }

    [Fact]
    public void Link_InlineTitle()
    {
        AssertHighlighter("markdown",
"""
[example](https://example.com "Example homepage")
""",
"""
[<span class="hljs-string">example</span>](<span class="hljs-link">https://example.com &quot;Example homepage&quot;</span>)
""");
    }

    [Fact]
    public void Link_Reference()
    {
        AssertHighlighter("markdown",
"""
[example][1]

[1]: https://example.com
""",
"""
[<span class="hljs-string">example</span>][<span class="hljs-symbol">1</span>]

[<span class="hljs-symbol">1</span>]: <span class="hljs-link">https://example.com</span>
""");
    }

    [Fact]
    public void Link_ReferenceTitle()
    {
        AssertHighlighter("markdown",
"""
[example][site]

[site]: https://example.com "Example"
""",
"""
[<span class="hljs-string">example</span>][<span class="hljs-symbol">site</span>]

[<span class="hljs-symbol">site</span>]: <span class="hljs-link">https://example.com &quot;Example&quot;</span>
""");
    }

    [Fact]
    public void Link_Shortcut()
    {
        AssertHighlighter("markdown",
"""
[example]

[example]: https://example.com
""",
"""
[example]

[<span class="hljs-symbol">example</span>]: <span class="hljs-link">https://example.com</span>
""");
    }

    [Fact]
    public void Link_Autolink()
    {
        AssertHighlighter("markdown",
"""
<https://example.com>
""",
"""
<span class="language-xml">&lt;https://example.com&gt;</span>
""");
    }

    [Fact]
    public void Link_AutolinkEmail()
    {
        AssertHighlighter("markdown",
"""
<alice@example.com>
""",
"""
<span class="language-xml">&lt;alice@example.com&gt;</span>
""");
    }

    [Fact]
    public void Link_BareUrl()
    {
        AssertHighlighter("markdown",
"""
See https://example.com for more.
""",
"""
See https://example.com for more.
""");
    }

    [Fact]
    public void Image_Inline()
    {
        AssertHighlighter("markdown",
"""
![alt text](https://example.com/img.png)
""",
"""
![<span class="hljs-string">alt text</span>](<span class="hljs-link">https://example.com/img.png</span>)
""");
    }

    [Fact]
    public void Image_Title()
    {
        AssertHighlighter("markdown",
"""
![alt text](https://example.com/img.png "An image")
""",
"""
![<span class="hljs-string">alt text</span>](<span class="hljs-link">https://example.com/img.png &quot;An image&quot;</span>)
""");
    }

    [Fact]
    public void Image_Reference()
    {
        AssertHighlighter("markdown",
"""
![alt text][img]

[img]: https://example.com/img.png
""",
"""
![<span class="hljs-string">alt text</span>][<span class="hljs-symbol">img</span>]

[<span class="hljs-symbol">img</span>]: <span class="hljs-link">https://example.com/img.png</span>
""");
    }

    [Fact]
    public void Image_AsLink()
    {
        AssertHighlighter("markdown",
"""
[![alt](img.png)](https://example.com)
""",
"""
[<span class="hljs-string">![alt</span>](<span class="hljs-link">img.png</span>)](<span class="hljs-link">https://example.com</span>)
""");
    }

    [Fact]
    public void CodeBlock_Indented()
    {
        AssertHighlighter("markdown",
"""
    function f() {
      return 42;
    }
""",
"""
<span class="hljs-code">    function f() {
      return 42;
    }</span>
""");
    }

    [Fact]
    public void CodeBlock_FencedBackticks()
    {
        AssertHighlighter("markdown",
"""
```
plain code
multiple lines
```
""",
"""
<span class="hljs-code">```
plain code
multiple lines
```</span>
""");
    }

    [Fact]
    public void CodeBlock_FencedTildes()
    {
        AssertHighlighter("markdown",
"""
~~~
plain code
~~~
""",
"""
<span class="hljs-code">~~~
plain code
~~~</span>
""");
    }

    [Fact]
    public void CodeBlock_FencedJsLang()
    {
        AssertHighlighter("markdown",
"""
```js
const x = 42;
console.log(x);
```
""",
"""
<span class="hljs-code">```js
const x = 42;
console.log(x);
```</span>
""");
    }

    [Fact]
    public void CodeBlock_FencedCsharpLang()
    {
        AssertHighlighter("markdown",
"""
```csharp
var greeting = "hello";
Console.WriteLine(greeting);
```
""",
"""
<span class="hljs-code">```csharp
var greeting = &quot;hello&quot;;
Console.WriteLine(greeting);
```</span>
""");
    }

    [Fact]
    public void CodeBlock_FencedJsonLang()
    {
        AssertHighlighter("markdown",
"""
```json
{
  "name": "demo"
}
```
""",
"""
<span class="hljs-code">```json
{
  &quot;name&quot;: &quot;demo&quot;
}
```</span>
""");
    }

    [Fact]
    public void CodeBlock_FencedBashLang()
    {
        AssertHighlighter("markdown",
"""
```bash
set -euo pipefail
echo "hi"
```
""",
"""
<span class="hljs-code">```bash
set -euo pipefail
echo &quot;hi&quot;
```</span>
""");
    }

    [Fact]
    public void CodeBlock_NestedBackticks()
    {
        AssertHighlighter("markdown",
"""
````
This fence allows ``` triple backticks ``` inside.
````
""",
"""
<span class="hljs-code">````
This fence allows ``` triple backticks ``` inside.
````</span>
""");
    }

    [Fact]
    public void BlockQuote_Single()
    {
        AssertHighlighter("markdown",
"""
> a quoted line
""",
"""
<span class="hljs-quote">&gt; a quoted line</span>
""");
    }

    [Fact]
    public void BlockQuote_MultiLine()
    {
        AssertHighlighter("markdown",
"""
> first quoted line
> second quoted line
""",
"""
<span class="hljs-quote">&gt; first quoted line</span>
<span class="hljs-quote">&gt; second quoted line</span>
""");
    }

    [Fact]
    public void BlockQuote_Nested()
    {
        AssertHighlighter("markdown",
"""
> outer quote
> > inner quote
""",
"""
<span class="hljs-quote">&gt; outer quote</span>
<span class="hljs-quote">&gt; &gt; inner quote</span>
""");
    }

    [Fact]
    public void BlockQuote_WithMarkdown()
    {
        AssertHighlighter("markdown",
"""
> **important**: read the [docs](https://example.com).
""",
"""
<span class="hljs-quote">&gt; <span class="hljs-strong">**important**</span>: read the [<span class="hljs-string">docs</span>](<span class="hljs-link">https://example.com</span>).</span>
""");
    }

    [Fact]
    public void BlockQuote_Multiparagraph()
    {
        AssertHighlighter("markdown",
"""
> first paragraph
>
> second paragraph
""",
"""
<span class="hljs-quote">&gt; first paragraph</span>
<span class="hljs-quote">&gt;
&gt; second paragraph</span>
""");
    }

    [Fact]
    public void Table_Basic()
    {
        AssertHighlighter("markdown",
"""
| Name  | Age |
|-------|-----|
| Alice | 30  |
| Bob   | 25  |
""",
"""
| Name  | Age |
|-------|-----|
| Alice | 30  |
| Bob   | 25  |
""");
    }

    [Fact]
    public void Table_Align()
    {
        AssertHighlighter("markdown",
"""
| Left | Center | Right |
|:-----|:------:|------:|
| a    | b      | c     |
""",
"""
| Left | Center | Right |
|:-----|:------:|------:|
| a    | b      | c     |
""");
    }

    [Fact]
    public void Table_WithInline()
    {
        AssertHighlighter("markdown",
"""
| Name      | Status         |
|-----------|----------------|
| `service` | **online**     |
| `db`      | *maintenance*  |
""",
"""
| Name      | Status         |
|-----------|----------------|
| <span class="hljs-code">`service`</span> | <span class="hljs-strong">**online**</span>     |
| <span class="hljs-code">`db`</span>      | <span class="hljs-emphasis">*maintenance*</span>  |
""");
    }

    [Fact]
    public void Table_CompactPipes()
    {
        AssertHighlighter("markdown",
"""
Name | Age
---- | ---
Alice | 30
""",
"""
Name | Age
---- | ---
Alice | 30
""");
    }

    [Fact]
    public void HorizontalRule_Dashes()
    {
        AssertHighlighter("markdown",
"""
---
""",
"""
---
""");
    }

    [Fact]
    public void HorizontalRule_Stars()
    {
        AssertHighlighter("markdown",
"""
***
""",
"""
<span class="hljs-strong">**<span class="hljs-emphasis">*</span></span>
""");
    }

    [Fact]
    public void HorizontalRule_Underscores()
    {
        AssertHighlighter("markdown",
"""
___
""",
"""
<span class="hljs-strong">__<span class="hljs-emphasis">_</span></span>
""");
    }

    [Fact]
    public void HorizontalRule_WithSpaces()
    {
        AssertHighlighter("markdown",
"""
- - -
""",
"""
<span class="hljs-bullet">-</span> - -
""");
    }

    [Fact]
    public void InlineHtml_Br()
    {
        AssertHighlighter("markdown",
"""
first line<br>second line
""",
"""
first line<span class="language-xml"><span class="hljs-tag">&lt;<span class="hljs-name">br</span>&gt;</span></span>second line
""");
    }

    [Fact]
    public void InlineHtml_Span()
    {
        AssertHighlighter("markdown",
"""
See <span style="color:red">this</span> for details.
""",
"""
See <span class="language-xml"><span class="hljs-tag">&lt;<span class="hljs-name">span</span> <span class="hljs-attr">style</span>=<span class="hljs-string">&quot;color:red&quot;</span>&gt;</span></span>this<span class="language-xml"><span class="hljs-tag">&lt;/<span class="hljs-name">span</span>&gt;</span></span> for details.
""");
    }

    [Fact]
    public void InlineHtml_Details()
    {
        AssertHighlighter("markdown",
"""
<details>
  <summary>Click to expand</summary>
  Hidden content here.
</details>
""",
"""
<span class="language-xml"><span class="hljs-tag">&lt;<span class="hljs-name">details</span>&gt;</span></span>
  <span class="language-xml"><span class="hljs-tag">&lt;<span class="hljs-name">summary</span>&gt;</span></span>Click to expand<span class="language-xml"><span class="hljs-tag">&lt;/<span class="hljs-name">summary</span>&gt;</span></span>
  Hidden content here.
<span class="language-xml"><span class="hljs-tag">&lt;/<span class="hljs-name">details</span>&gt;</span></span>
""");
    }

    [Fact]
    public void InlineHtml_Comment()
    {
        AssertHighlighter("markdown",
"""
<!-- a comment that the renderer skips -->
""",
"""
&lt;!-- a comment that the renderer skips --&gt;
""");
    }

    [Fact]
    public void GfmExtras_Footnote()
    {
        AssertHighlighter("markdown",
"""
Here is some text[^1].

[^1]: This is the footnote.
""",
"""
Here is some text[^1].

[<span class="hljs-symbol">^1</span>]: <span class="hljs-link">This is the footnote.</span>
""");
    }

    [Fact]
    public void GfmExtras_DefinitionList()
    {
        AssertHighlighter("markdown",
"""
Term
: Definition for the term.
: Second definition for the term.
""",
"""
Term
: Definition for the term.
: Second definition for the term.
""");
    }

    [Fact]
    public void GfmExtras_EmojiShortcode()
    {
        AssertHighlighter("markdown",
"""
:smile: :tada: :rocket:
""",
"""
:smile: :tada: :rocket:
""");
    }

    [Fact]
    public void GfmExtras_AlertNote()
    {
        AssertHighlighter("markdown",
"""
> [!NOTE]
> Some helpful information.
""",
"""
<span class="hljs-quote">&gt; [!NOTE]</span>
<span class="hljs-quote">&gt; Some helpful information.</span>
""");
    }

    [Fact]
    public void GfmExtras_AlertWarning()
    {
        AssertHighlighter("markdown",
"""
> [!WARNING]
> Watch out!
""",
"""
<span class="hljs-quote">&gt; [!WARNING]</span>
<span class="hljs-quote">&gt; Watch out!</span>
""");
    }

    [Fact]
    public void GfmExtras_AlertImportant()
    {
        AssertHighlighter("markdown",
"""
> [!IMPORTANT]
> Pay attention to this.
""",
"""
<span class="hljs-quote">&gt; [!IMPORTANT]</span>
<span class="hljs-quote">&gt; Pay attention to this.</span>
""");
    }

    [Fact]
    public void FrontMatter_Yaml()
    {
        AssertHighlighter("markdown",
"""
---
title: My Post
date: 2026-05-26
tags: [markdown, demo]
---

# Body
""",
"""
---
title: My Post
date: 2026-05-26
<span class="hljs-section">tags: [markdown, demo]
---</span>

<span class="hljs-section"># Body</span>
""");
    }

    [Fact]
    public void FrontMatter_Toml()
    {
        AssertHighlighter("markdown",
"""
+++
title = "My Post"
date = 2026-05-26
+++

# Body
""",
"""
+++
title = &quot;My Post&quot;
date = 2026-05-26
+++

<span class="hljs-section"># Body</span>
""");
    }

    [Fact]
    public void EscapedChar_Backslash()
    {
        AssertHighlighter("markdown",
"""
A literal \*not italic\* example.
""",
"""
A literal \<span class="hljs-emphasis">*not italic\*</span> example.
""");
    }

    [Fact]
    public void EscapedChar_Bracket()
    {
        AssertHighlighter("markdown",
"""
Use \[brackets\] like this.
""",
"""
Use \[brackets\] like this.
""");
    }

    [Fact]
    public void EscapedChar_Backtick()
    {
        AssertHighlighter("markdown",
"""
A literal \` backtick.
""",
"""
A literal \` backtick.
""");
    }

    [Fact]
    public void Composite_Readme()
    {
        AssertHighlighter("markdown",
"""
# My Project

[![Build](https://img.shields.io/badge/build-passing-green)](https://example.com)

> A short tagline goes here.

## Features

- Fast
- **Reliable**
- _Documented_

## Install

```bash
npm install my-project
```

## Usage

```js
import { greet } from "my-project";
greet("Alice");
```

## License

[MIT](LICENSE)
""",
"""
<span class="hljs-section"># My Project</span>

[<span class="hljs-string">![Build</span>](<span class="hljs-link">https://img.shields.io/badge/build-passing-green</span>)](<span class="hljs-link">https://example.com</span>)

<span class="hljs-quote">&gt; A short tagline goes here.</span>

<span class="hljs-section">## Features</span>

<span class="hljs-bullet">-</span> Fast
<span class="hljs-bullet">-</span> <span class="hljs-strong">**Reliable**</span>
<span class="hljs-bullet">-</span> <span class="hljs-emphasis">_Documented_</span>

<span class="hljs-section">## Install</span>

<span class="hljs-code">```bash
npm install my-project
```</span>

<span class="hljs-section">## Usage</span>

<span class="hljs-code">```js
import { greet } from &quot;my-project&quot;;
greet(&quot;Alice&quot;);
```</span>

<span class="hljs-section">## License</span>

[<span class="hljs-string">MIT</span>](<span class="hljs-link">LICENSE</span>)
""");
    }

    [Fact]
    public void Composite_ChangeLog()
    {
        AssertHighlighter("markdown",
"""
# Changelog

All notable changes are documented here.

## [1.2.0] - 2026-05-26

### Added
- New `--verbose` flag.

### Changed
- `--quiet` is now the default.

### Fixed
- Crash when input is empty (#42).

[1.2.0]: https://example.com/releases/1.2.0
""",
"""
<span class="hljs-section"># Changelog</span>

All notable changes are documented here.

<span class="hljs-section">## [1.2.0] - 2026-05-26</span>

<span class="hljs-section">### Added</span>
<span class="hljs-bullet">-</span> New <span class="hljs-code">`--verbose`</span> flag.

<span class="hljs-section">### Changed</span>
<span class="hljs-bullet">-</span> <span class="hljs-code">`--quiet`</span> is now the default.

<span class="hljs-section">### Fixed</span>
<span class="hljs-bullet">-</span> Crash when input is empty (#42).

[<span class="hljs-symbol">1.2.0</span>]: <span class="hljs-link">https://example.com/releases/1.2.0</span>
""");
    }

    [Fact]
    public void Composite_BlogPost()
    {
        AssertHighlighter("markdown",
"""
---
title: "Hello, World"
date: 2026-05-26
author: Alice
tags:
  - intro
  - meta
---

# Hello, World

This is my **first** post.

## Background

I started this blog because:

1. I wanted a place to write.
2. RSS still matters.
3. Markdown is great.

> "The best time to plant a tree was 20 years ago. The second best time is now."

![Cover](cover.jpg "Cover image")

More details on [the about page](/about).
""",
"""
---
title: &quot;Hello, World&quot;
date: 2026-05-26
author: Alice
tags:
<span class="hljs-bullet">  -</span> intro
<span class="hljs-section">  - meta
---</span>

<span class="hljs-section"># Hello, World</span>

This is my <span class="hljs-strong">**first**</span> post.

<span class="hljs-section">## Background</span>

I started this blog because:

<span class="hljs-bullet">1.</span> I wanted a place to write.
<span class="hljs-bullet">2.</span> RSS still matters.
<span class="hljs-bullet">3.</span> Markdown is great.

<span class="hljs-quote">&gt; &quot;The best time to plant a tree was 20 years ago. The second best time is now.&quot;</span>

![<span class="hljs-string">Cover</span>](<span class="hljs-link">cover.jpg &quot;Cover image&quot;</span>)

More details on [<span class="hljs-string">the about page</span>](<span class="hljs-link">/about</span>).
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("markdown",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyText()
    {
        AssertHighlighter("markdown",
"""
Just a plain paragraph with no markup.
""",
"""
Just a plain paragraph with no markup.
""");
    }

    [Fact]
    public void SpecialEdge_OnlyWhitespace()
    {
        AssertHighlighter("markdown",
"""


""",
"""


""");
    }

    [Fact]
    public void SpecialEdge_TrailingNewline()
    {
        AssertHighlighter("markdown",
"""
# Title

""",
"""
<span class="hljs-section"># Title</span>

""");
    }

    [Fact]
    public void SpecialEdge_LineBreakTrailing2Spaces()
    {
        AssertHighlighter("markdown",
"""
first line
second line
""",
"""
first line
second line
""");
    }

    [Fact]
    public void SpecialEdge_LineBreakBackslash()
    {
        AssertHighlighter("markdown",
"""
first line\
second line
""",
"""
first line\
second line
""");
    }
}
