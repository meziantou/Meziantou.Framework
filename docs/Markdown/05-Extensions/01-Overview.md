---
title: Overview
description: Enable GitHub Flavored Markdown, or choose the extensions one by one.
---

CommonMark only defines the core syntax of Markdown: paragraphs, headings, lists, links, emphasis, code and HTML. The
extensions add the syntaxes that sites and tools use on top of it, such as tables, task lists, footnotes or alerts. None
is enabled by default, so that a document is read as CommonMark unless you ask for more.

## GitHub Flavored Markdown

[MarkdownParseOptions.GitHubFlavored](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.GitHubFlavored) parses the
extensions of [GitHub Flavored Markdown](https://github.github.com/gfm/), the Markdown of GitHub: tables, strikethrough,
task lists, extended autolinks, and the tag filter of the HTML renderer.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_GitHubFlavored;format=md-fenced-code-block;dedent=true -->
```csharp
var text = """
    | Task       | Done |
    | ---------- | :--: |
    | ~~Draft~~  | yes  |

    - [x] Read www.example.com
    """;
var tree = MarkdownSyntaxTree.ParseText(text, MarkdownParseOptions.GitHubFlavored);

Assert.Equal(text, tree.GetRoot().ToFullString());
Assert.Equal("""
    <table>
    <thead>
    <tr>
    <th>Task</th>
    <th align="center">Done</th>
    </tr>
    </thead>
    <tbody>
    <tr>
    <td><del>Draft</del></td>
    <td align="center">yes</td>
    </tr>
    </tbody>
    </table>
    <ul>
    <li><input checked="" disabled="" type="checkbox"> Read <a href="http://www.example.com">www.example.com</a></li>
    </ul>

    """.ReplaceLineEndings("\n"), MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

The parser follows the reference implementation, cmark-gfm, where the specification leaves a case open. The pages of the
extensions give the few differences.

## Choosing the extensions

[Extensions](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.Extensions) chooses the extensions one by one, from
the flags of [MarkdownExtensions](cref:Meziantou.Framework.Markdown.MarkdownExtensions):

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Extensions;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Strikethrough };
var tree = MarkdownSyntaxTree.ParseText("~~old~~ www.example.com", options);

// Strikethrough only: the address is text
Assert.Equal("<p><del>old</del> www.example.com</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

The extensions keep every character, as the rest of the syntax tree: their markers are tokens, and a document that uses
them is written back exactly.

| Flag | Syntax | Page |
| ---- | ------ | ---- |
| [Tables](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Tables) | `\| a \| b \|` rows with a delimiter row | [Tables](02-Tables.md) |
| [Strikethrough](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Strikethrough) | `~~text~~` | [Strikethrough](03-Strikethrough.md) |
| [TaskLists](cref:Meziantou.Framework.Markdown.MarkdownExtensions.TaskLists) | `- [x] item` | [Task lists](04-TaskLists.md) |
| [Autolinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Autolinks) | `www.example.com` | [Extended autolinks](05-Autolinks.md) |
| [TagFilter](cref:Meziantou.Framework.Markdown.MarkdownExtensions.TagFilter) | `<script>` written as text | [Tag filter](06-TagFilter.md) |
| [PipeTables](cref:Meziantou.Framework.Markdown.MarkdownExtensions.PipeTables) | `a \| b` rows, with or without outer pipes | [Pipe tables](07-PipeTables.md) |
| [GridTables](cref:Meziantou.Framework.Markdown.MarkdownExtensions.GridTables) | `+---+---+` tables with multi-line cells | [Grid tables](08-GridTables.md) |
| [EmphasisExtras](cref:Meziantou.Framework.Markdown.MarkdownExtensions.EmphasisExtras) | `H~2~O`, `2^10^`, `++text++`, `==text==` | [Extra emphasis](09-EmphasisExtras.md) |
| [HardLineBreaks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.HardLineBreaks) | every line break is `<br />` | [Hard line breaks](10-HardLineBreaks.md) |
| [YamlFrontMatter](cref:Meziantou.Framework.Markdown.MarkdownExtensions.YamlFrontMatter) | `---` YAML block at the start | [YAML front matter](11-YamlFrontMatter.md) |
| [Mathematics](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Mathematics) | `$x^2$`, `$$` blocks | [Mathematics](12-Mathematics.md) |
| [Abbreviations](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Abbreviations) | `*[HTML]: Hypertext Markup Language` | [Abbreviations](13-Abbreviations.md) |
| [Footnotes](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Footnotes) | `[^1]` and `[^1]: note` | [Footnotes](14-Footnotes.md) |
| [GenericAttributes](cref:Meziantou.Framework.Markdown.MarkdownExtensions.GenericAttributes) | `{#id .class key=value}` | [Generic attributes](15-GenericAttributes.md) |
| [Alerts](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Alerts) | `> [!NOTE]` | [Alerts](16-Alerts.md) |
| [Diagrams](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Diagrams) | ` ```mermaid ` code blocks | [Diagrams](17-Diagrams.md) |
| [AutoIdentifiers](cref:Meziantou.Framework.Markdown.MarkdownExtensions.AutoIdentifiers) | an `id` for each heading | [Heading identifiers](18-AutoIdentifiers.md) |
| [CustomContainers](cref:Meziantou.Framework.Markdown.MarkdownExtensions.CustomContainers) | `:::name` blocks | [Custom containers](19-CustomContainers.md) |
| [DefinitionLists](cref:Meziantou.Framework.Markdown.MarkdownExtensions.DefinitionLists) | `Term` then `:   definition` | [Definition lists](20-DefinitionLists.md) |
| [ListExtras](cref:Meziantou.Framework.Markdown.MarkdownExtensions.ListExtras) | `a.` and `iv.` ordered lists | [Lettered and roman lists](21-ListExtras.md) |
| [SmartyPants](cref:Meziantou.Framework.Markdown.MarkdownExtensions.SmartyPants) | `"quotes"`, `--`, `...` | [Typographic punctuation](22-SmartyPants.md) |
| [Emoji](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Emoji) | `:rocket:`, `:)` | [Emojis](23-Emoji.md) |
| [Figures](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Figures) | `^^^` blocks | [Figures](24-Figures.md) |
| [Footers](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Footers) | `^^ text` | [Footers](25-Footers.md) |
| [Citations](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Citations) | `""title""` | [Citations](26-Citations.md) |
| [MediaLinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.MediaLinks) | `![](video.mp4)` | [Media links](27-MediaLinks.md) |
| [JiraLinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.JiraLinks) | `PROJ-123` | [Jira links](28-JiraLinks.md) |
| [MarkdigAutolinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.MarkdigAutolinks) | `http://`, `ftp://`, `mailto:`, `tel:` | [Scheme autolinks](29-MarkdigAutolinks.md) |
| [CjkFriendlyEmphasis](cref:Meziantou.Framework.Markdown.MarkdownExtensions.CjkFriendlyEmphasis) | `**テスト。**です` | [CJK-friendly emphasis](30-CjkFriendlyEmphasis.md) |
| [Bootstrap](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Bootstrap) | Bootstrap classes in the HTML | [Bootstrap](31-Bootstrap.md) |
| [Globalization](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Globalization) | `dir="rtl"` in the HTML | [Right-to-left text](32-Globalization.md) |

[Advanced](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Advanced) enables the extensions of
`UseAdvancedExtensions` of `Meziantou.Framework.Markdown` 1.x: alerts, abbreviations, auto identifiers, citations,
custom containers, definition lists, the extra emphasis, figures, footers, footnotes, grid tables, mathematics, media
links, pipe tables, list extras, task lists, diagrams, scheme autolinks and generic attributes.

[DisableHtml](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.DisableHtml) reads HTML as text: the renderer
escapes it, and autolinks such as `<https://example.com>` are still links.

## Your own syntaxes

When no extension reads the syntax you need, such as `@mentions`, `%%highlights%%` or `!!! note` admonitions,
[write your own](33-CustomSyntaxes.md) and add it to
[SyntaxExtensions](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.SyntaxExtensions).
