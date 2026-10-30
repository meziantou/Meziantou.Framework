---
title: Rendering the Markdown
description: Render the converted Markdown back to HTML with Meziantou.Framework.Markdown, and enable the extensions it uses.
---

The Markdown that the converter writes follows CommonMark, plus four common extensions: pipe tables, task lists,
strikethrough (`~~`) and definition lists. A renderer that only implements CommonMark shows these as text. With
[Meziantou.Framework.Markdown](../Markdown/01-Introduction.md), enable them in the pipeline:

<!-- ref:../../tests/Meziantou.Framework.HtmlToMarkdown.Tests/MarkdownRoundTrippingTests.cs;region=HtmlToMarkdown_RoundTrip;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = HtmlToMarkdown.Convert("<table><tr><th>Name</th><th>Price</th></tr><tr><td>Apple</td><td><del>1.50</del> 1.20</td></tr></table>");
Assert.Equal("""
    | Name | Price |
    | --- | --- |
    | Apple | ~~1.50~~ 1.20 |
    """, markdown);

// Pipe tables and strikethrough are extensions of CommonMark: enable them to render the Markdown
var pipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .Build();
var html = MarkdownConverter.ToHtml(markdown, pipeline);
Assert.Contains("<td><del>1.50</del> 1.20</td>", html, StringComparison.Ordinal);
```
<!-- endref -->

`UseAdvancedExtensions` enables all four, with other extensions: see
[Extensions](../Markdown/04-Extensions/01-Overview.md) to enable them one by one.

| The converter writes | Extension of Meziantou.Framework.Markdown |
| --- | --- |
| Pipe tables, from `table` | [Pipe tables](../Markdown/04-Extensions/02-Tables.md#pipe-tables) |
| Task lists, from checkboxes | [Task lists](../Markdown/04-Extensions/05-Blocks.md#task-lists) |
| `~~`, from `del`, `s` and `strike` | [Strikethrough](../Markdown/04-Extensions/04-TextFormatting.md#strikethrough-and-other-emphasis) |
| Definition lists, from `dl` | [Definition lists](../Markdown/04-Extensions/05-Blocks.md#definition-lists) |
| Shortcodes, with `EmojiShortcodeMode` | [Emojis](../Markdown/04-Extensions/04-TextFormatting.md#emojis), not enabled by `UseAdvancedExtensions` |

## Round trip

Converting HTML to Markdown and rendering it again usually gives an equivalent HTML for the
[converted elements](03-ConvertedElements.md): the same structure and the same text. It does not give the same markup:

- the attributes that Markdown cannot write (`class`, `id`, `style`...) are lost, and the containers (`div`,
  `section`...) are replaced by their content;
- the whitespace is collapsed, outside of the code blocks;
- a list can change between tight and loose, so its items gain or lose their `p` elements;
- the renderer writes its own markup, such as the `thead` and `tbody` of a table, or the attributes of a task list
  checkbox.

The unknown elements written as raw HTML are rendered again as they are, when the renderer allows raw HTML. Read
[Security](../Markdown/10-Security.md) before you render Markdown that comes from untrusted HTML.
