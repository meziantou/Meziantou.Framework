---
title: Blocks
description: Render GitHub alerts, custom containers, definition lists, figures, footnotes, task lists, mathematics and diagrams.
---

## Alerts

`UseAlertBlocks` renders the alerts of GitHub: a quote whose first line is `[!NOTE]`, `[!TIP]`, `[!IMPORTANT]`,
`[!WARNING]` or `[!CAUTION]`. The alert is a `<div>` with the classes `markdown-alert` and `markdown-alert-<kind>`, and a
title with the icon of GitHub:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseAlertBlocks;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseAlertBlocks()
    .Build();

var html = MarkdownConverter.ToHtml("> [!WARNING]\n> The file is overwritten.", pipeline);
Assert.StartsWith("<div class=\"markdown-alert markdown-alert-warning\">\n<p class=\"markdown-alert-title\"><svg ", html);
Assert.EndsWith("Warning</p>\n<p>The file is overwritten.</p>\n</div>\n", html);
```
<!-- endref -->

The library writes no CSS: style these classes in your page. To write another title, pass `renderKind`: it receives the
renderer and the kind of the alert (`WARNING` here):

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseAlertBlocksRenderKind;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseAlertBlocks(renderKind: (renderer, kind) =>
    {
        // kind is the text between "[!" and "]", such as "WARNING"
        renderer.Write("<p class=\"alert-title\">").WriteEscape(kind.ToString()).Write("</p>").WriteLine();
    })
    .Build();

var html = MarkdownConverter.ToHtml("> [!WARNING]\n> The file is overwritten.", pipeline);
Assert.Equal("<div class=\"markdown-alert markdown-alert-warning\">\n<p class=\"alert-title\">WARNING</p>\n<p>The file is overwritten.</p>\n</div>\n", html);
```
<!-- endref -->

By default, an alert must be a block of the document. Pass `allowNestedAlerts: true` to also accept alerts in quotes and
list items. An alert is never accepted inside another alert.

## Custom containers

`UseCustomContainers` renders the blocks fenced by `:::` as a `<div>` whose class is the word after the opening fence,
and the text between `::` as a `<span>`. Give the `<span>` a class with an [attribute](06-GenericAttributes.md):

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseCustomContainers;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseCustomContainers()
    .UseGenericAttributes() // To give a class to the inline container
    .Build();

var html = MarkdownConverter.ToHtml(":::warning\nCheck the settings first.\n:::\n\nSome ::highlighted text::{.highlight}.", pipeline);
Assert.Equal("<div class=\"warning\"><p>Check the settings first.</p>\n</div>\n<p>Some <span class=\"highlight\">highlighted text</span>.</p>\n", html);
```
<!-- endref -->

The text after the class on the opening line is not rendered, and is available as `CustomContainer.Arguments` in the
[syntax tree](../05-SyntaxTree.md). As with fenced code blocks, the fence can be longer than three `:`, and the closing
fence must have the same length.

## Definition lists

`UseDefinitionLists` renders a term followed by lines that start with `:` as a definition list:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseDefinitionLists;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseDefinitionLists()
    .Build();

var html = MarkdownConverter.ToHtml("CommonMark\n:   A strongly defined specification of Markdown.", pipeline);
Assert.Equal("<dl>\n<dt>CommonMark</dt>\n<dd>A strongly defined specification of Markdown.</dd>\n</dl>\n", html);
```
<!-- endref -->

A term can have several definitions, and a definition can contain several paragraphs and other blocks.

## Figures, footers and citations

- `UseFigures` renders the blocks between `^^^` lines as a `<figure>`. The text after the closing `^^^` is the
  `<figcaption>`.
- `UseFooters` renders the lines that start with `^^` as a `<footer>`.
- `UseCitations` renders the text between `""` and `""` as a `<cite>`.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseFiguresFootersAndCitations;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseFigures()
    .UseFooters()
    .UseCitations()
    .Build();

var figure = MarkdownConverter.ToHtml("^^^\n![The logo](logo.png)\n^^^ The logo of the project", pipeline);
Assert.Equal("<figure>\n<p><img src=\"logo.png\" alt=\"The logo\" /></p>\n<figcaption>The logo of the project</figcaption>\n</figure>\n", figure);

var footer = MarkdownConverter.ToHtml("^^ Written by the documentation team", pipeline);
Assert.Equal("<footer>Written by the documentation team</footer>\n", footer);

var citation = MarkdownConverter.ToHtml("As described in \"\"The Art of Computer Programming\"\".", pipeline);
Assert.Equal("<p>As described in <cite>The Art of Computer Programming</cite>.</p>\n", citation);
```
<!-- endref -->

## Footnotes

`UseFootnotes` renders the `[^label]` references as numbered links to the notes, and the `[^label]: text` definitions
as a list at the end of the document, with links back to the references:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseFootnotes;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseFootnotes()
    .Build();

var html = MarkdownConverter.ToHtml("Markdown was created in 2004[^1].\n\n[^1]: By John Gruber.", pipeline);
Assert.Equal("""
    <p>Markdown was created in 2004<a id="fnref:1" href="#fn:1" class="footnote-ref"><sup>1</sup></a>.</p>
    <div class="footnotes">
    <hr />
    <ol>
    <li id="fn:1">
    <p>By John Gruber.<a href="#fnref:1" class="footnote-back-ref">&#8617;</a></p>
    </li>
    </ol>
    </div>

    """.ReplaceLineEndings("\n"), html);
```
<!-- endref -->

The notes are numbered in the order of their first reference, whatever their label.

## Task lists

`UseTaskLists` renders the items that start with `[ ]` or `[x]` as disabled checkboxes:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseTaskLists;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseTaskLists()
    .Build();

var html = MarkdownConverter.ToHtml("- [x] Write the code\n- [ ] Write the documentation", pipeline);
Assert.Equal("""
    <ul class="contains-task-list">
    <li class="task-list-item"><input disabled="disabled" type="checkbox" checked="checked" /> Write the code</li>
    <li class="task-list-item"><input disabled="disabled" type="checkbox" /> Write the documentation</li>
    </ul>

    """.ReplaceLineEndings("\n"), html);
```
<!-- endref -->

In the syntax tree, the checkbox is a `TaskList` inline (namespace [Meziantou.Framework.Markdown.Extensions.TaskLists](cref:Meziantou.Framework.Markdown.Extensions.TaskLists))
whose `Checked` property gives its state.

## Alphabetical and roman lists

CommonMark ordered lists are numbered with digits. `UseListExtras` adds lists numbered with letters (`a.`, `A.`) and
roman numerals (`i.`, `I.`), rendered with the `type` attribute of `<ol>`:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseListExtras;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseListExtras()
    .Build();

Assert.Equal("<ol type=\"a\">\n<li>First</li>\n<li>Second</li>\n</ol>\n", MarkdownConverter.ToHtml("a. First\nb. Second", pipeline));
Assert.Equal("<ol type=\"i\">\n<li>First</li>\n<li>Second</li>\n</ol>\n", MarkdownConverter.ToHtml("i. First\nii. Second", pipeline));
```
<!-- endref -->

## Mathematics

`UseMathematics` reads the formulas between `$` (inline) and between `$$` lines (block). It does not render them: it
writes them with the `math` class and the `\(…\)` and `\[…\]` delimiters, which [MathJax](https://www.mathjax.org/)
and [KaTeX](https://katex.org/) find and render in the browser:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseMathematics;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseMathematics()
    .Build();

var html = MarkdownConverter.ToHtml("The area is $\\pi r^2$:\n\n$$\nA = \\pi r^2\n$$", pipeline);
Assert.Equal("<p>The area is <span class=\"math\">\\(\\pi r^2\\)</span>:</p>\n<div class=\"math\">\n\\[\nA = \\pi r^2\n\\]</div>\n", html);
```
<!-- endref -->

## Diagrams

`UseDiagrams` writes the code blocks of the `mermaid` language as `<pre class="mermaid">` and the ones of the `nomnoml`
language as `<div class="nomnoml">`, which [Mermaid](https://mermaid.js.org/) and [nomnoml](https://nomnoml.com/) find
and draw in the browser:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseDiagrams;format=md-fenced-code-block;dedent=true -->
````csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseDiagrams()
    .Build();

var html = MarkdownConverter.ToHtml("```mermaid\ngraph LR\n    Parse --> Render\n```", pipeline);
Assert.Equal("<pre class=\"mermaid\">graph LR\n    Parse --> Render\n</pre>\n", html);
````
<!-- endref -->

Only `<` and `&` are escaped in the diagram, so that arrows such as `-->` reach the diagram library unchanged.
