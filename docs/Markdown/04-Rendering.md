---
title: Rendering
description: Render a document as HTML or as plain text, change the URLs, the attributes and the ids of the elements, and write some nodes yourself.
---

## Render HTML

[MarkdownHtmlRenderer.ToHtml](cref:Meziantou.Framework.Markdown.MarkdownHtmlRenderer.ToHtml(Meziantou.Framework.Markdown.MarkdownSyntaxTree))
renders the HTML of the CommonMark reference implementation. The parser follows CommonMark 0.31.2, and renders the
HTML of every example of the specification.

[MarkdownHtmlRendererOptions](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions) changes the HTML of
the links: [BaseUrl](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.BaseUrl) makes the relative
URLs absolute, [LinkRewriter](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.LinkRewriter) changes
the URLs, [LinkRel](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.LinkRel) sets the `rel`
attribute of the links, such as `nofollow`, and
[EscapeNonAsciiUrlCharacters](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.EscapeNonAsciiUrlCharacters)
keeps the characters of the URLs that are not ASCII:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlRenderer_LinkOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = MarkdownSyntaxTree.ParseText("[Next page](next.md) ![Logo](logo.png) <https://example.org>\n");
var options = new MarkdownHtmlRendererOptions
{
    BaseUrl = new Uri("https://example.com/docs/"),
    LinkRewriter = url => url.EndsWith(".md", StringComparison.Ordinal) ? url[..^3] + ".html" : url,
    LinkRel = "nofollow noopener",
};

Assert.Equal("<p><a href=\"https://example.com/docs/next.html\" rel=\"nofollow noopener\">Next page</a> <img src=\"https://example.com/docs/logo.png\" alt=\"Logo\" /> <a href=\"https://example.org\" rel=\"nofollow noopener\">https://example.org</a></p>\n", MarkdownHtmlRenderer.ToHtml(tree, options));

// Characters that are not ASCII are percent-encoded, unless the options keep them
var image = MarkdownSyntaxTree.ParseText("![](photo-été.png)\n");
Assert.Equal("<p><img src=\"photo-%C3%A9t%C3%A9.png\" alt=\"\" /></p>\n", MarkdownHtmlRenderer.ToHtml(image));
Assert.Equal("<p><img src=\"photo-été.png\" alt=\"\" /></p>\n", MarkdownHtmlRenderer.ToHtml(image, new MarkdownHtmlRendererOptions { EscapeNonAsciiUrlCharacters = false }));
```
<!-- endref -->

[PragmaLines](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.PragmaLines) gives the element of
the first block of each line the id `pragma-line-N`, where N is the number of the line, for an editor to scroll its
preview to a line.
[FindClosestLine](cref:Meziantou.Framework.Markdown.MarkdownDocumentSyntax.FindClosestLine(System.Int32)) finds
the line of the block that is the closest to a line:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlRenderer_PragmaLines;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = MarkdownSyntaxTree.ParseText("# Title\n\nSome text.\n\n- First\n- Second\n");

Assert.Equal("<h1 id=\"pragma-line-0\">Title</h1>\n<p id=\"pragma-line-2\">Some text.</p>\n<ul id=\"pragma-line-4\">\n<li>First</li>\n<li id=\"pragma-line-5\">Second</li>\n</ul>\n", MarkdownHtmlRenderer.ToHtml(tree, new MarkdownHtmlRendererOptions { PragmaLines = true }));

// The editor shows line 3, a blank line: scroll the preview to the closest element
Assert.Equal(4, tree.GetRoot().FindClosestLine(3));
```
<!-- endref -->

The ids that the document sets are the same in every document: the identifiers of the headings, the ids of the
[generic attributes](05-Extensions/15-GenericAttributes.md), and `fn:N` and `fnref:N` for the
[footnotes](05-Extensions/14-Footnotes.md). When several documents are rendered on one page, such as the comments of a
discussion, their ids collide, and the link of a footnote goes to the note of another document.
[IdPrefix](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.IdPrefix) adds a prefix to these ids and to the
links that the renderer writes to them, as GitHub adds `user-content-`. The links that the text writes, such as `[a](#b)`,
and the pragma lines keep their ids:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlRendererOptions_IdPrefix;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.AutoIdentifiers | MarkdownExtensions.Footnotes };
var tree = MarkdownSyntaxTree.ParseText("# Notes\n\nSee[^1].\n\n[^1]: A note.\n", options);

// Several documents rendered on one page, such as comments, have different ids
var html = MarkdownHtmlRenderer.ToHtml(tree, new MarkdownHtmlRendererOptions { IdPrefix = "comment-1-" });
Assert.Equal("<h1 id=\"comment-1-notes\">Notes</h1>\n<p>See<a id=\"comment-1-fnref:1\" href=\"#comment-1-fn:1\" class=\"footnote-ref\"><sup>1</sup></a>.</p>\n<div class=\"footnotes\">\n<hr />\n<ol>\n<li id=\"comment-1-fn:1\">\n<p>A note.<a href=\"#comment-1-fnref:1\" class=\"footnote-back-ref\">&#8617;</a></p>\n</li>\n</ol>\n</div>\n", html);
```
<!-- endref -->

## Render plain text

[MarkdownPlainTextRenderer.ToPlainText](cref:Meziantou.Framework.Markdown.MarkdownPlainTextRenderer.ToPlainText(Meziantou.Framework.Markdown.MarkdownSyntaxTree))
writes the text of the document, as `MarkdownConverter.ToPlainText` of `Meziantou.Framework.Markdown` 1.x does: the text of
the HTML without its tags, not escaped. Each block ends its line, HTML is not written, an image is its text, a footnote
reference is its number, a task is `[x]` or `[ ]`, and the cells of a table are followed by a space. Unlike 1.x, the text
after a table starts on a new line, the characters of SmartyPants are not entities, and an alert has no title.

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownPlainTextRenderer_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = MarkdownSyntaxTree.ParseText("# Title\n\nSome **bold** text and [a link](https://example.com).\n\n- First\n- Second");

Assert.Equal("Title\nSome bold text and a link.\nFirst\nSecond\n", MarkdownPlainTextRenderer.ToPlainText(tree));
```
<!-- endref -->

## Write some nodes yourself

A [MarkdownHtmlNodeRenderer](cref:Meziantou.Framework.Markdown.MarkdownHtmlNodeRenderer) writes the HTML of some nodes
instead of the renderer, such as the code blocks of a language or the custom containers of a name. Add it to
[NodeRenderers](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.NodeRenderers): each block and each inline
is offered to the node renderers in order, and the first one whose
[TryWrite](cref:Meziantou.Framework.Markdown.MarkdownHtmlNodeRenderer.TryWrite(Meziantou.Framework.Markdown.MarkdownSyntaxNode,Meziantou.Framework.Markdown.MarkdownHtmlWriter))
returns `true` writes it. The others are written as usual. The parts of a block that are written with it, such as the
items of a list and the rows of a table, are not offered. A paragraph written without `<p>`, the only block of a grid
table cell or of a definition, is offered too: its default rendering is its inlines.

The node renderer writes with a [MarkdownHtmlWriter](cref:Meziantou.Framework.Markdown.MarkdownHtmlWriter):

- [WriteText](cref:Meziantou.Framework.Markdown.MarkdownHtmlWriter.WriteText(System.ReadOnlySpan{System.Char})) escapes
  text, and [WriteAttributeValue](cref:Meziantou.Framework.Markdown.MarkdownHtmlWriter.WriteAttributeValue(System.ReadOnlySpan{System.Char}))
  escapes the value of an attribute.
- [WriteRawHtml](cref:Meziantou.Framework.Markdown.MarkdownHtmlWriter.WriteRawHtml(System.ReadOnlySpan{System.Char}))
  writes HTML as it is.
- [WriteUrl](cref:Meziantou.Framework.Markdown.MarkdownHtmlWriter.WriteUrl(System.String)) writes a URL as the links
  have it, with [BaseUrl](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.BaseUrl) and
  [LinkRewriter](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.LinkRewriter).
- [WriteAttributes](cref:Meziantou.Framework.Markdown.MarkdownHtmlWriter.WriteAttributes(Meziantou.Framework.Markdown.MarkdownSyntaxNode,System.String))
  writes the attributes of the element of the node: its [generic attributes](05-Extensions/15-GenericAttributes.md), and
  its pragma line.
- [WriteBlocks](cref:Meziantou.Framework.Markdown.MarkdownHtmlWriter.WriteBlocks(System.Collections.Generic.IEnumerable{Meziantou.Framework.Markdown.MarkdownBlockSyntax}))
  and [WriteInlines](cref:Meziantou.Framework.Markdown.MarkdownHtmlWriter.WriteInlines(System.Collections.Generic.IEnumerable{Meziantou.Framework.Language.SyntaxNodeOrToken}))
  write the content of the node, offering its nodes to the node renderers.
- [EnsureNewLine](cref:Meziantou.Framework.Markdown.MarkdownHtmlWriter.EnsureNewLine) starts a new line, as the renderer
  does between blocks.

This node renderer writes the code blocks of the language `chart` as a `<canvas>` that a script draws:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlNodeRenderer_Chart;format=md-fenced-code-block;dedent=true -->
```csharp
// Writes the code blocks of the language "chart" as a <canvas> that a script draws
private sealed class ChartRenderer : MarkdownHtmlNodeRenderer
{
    public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
    {
        if (node is not MarkdownCodeBlockSyntax { Info: "chart" } code)
        {
            return false;
        }

        writer.EnsureNewLine();
        writer.WriteRawHtml("<canvas class=\"chart\" data-spec=\"");
        writer.WriteAttributeValue(code.Code);
        writer.WriteRawHtml("\"></canvas>");

        // Plain text has the text of the chart
        if (writer.IsPlainText)
        {
            writer.WriteText(code.Code);
        }

        writer.EnsureNewLine();
        return true;
    }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlRenderer_NodeRenderers;format=md-fenced-code-block;dedent=true -->
````csharp
var tree = MarkdownSyntaxTree.ParseText("```chart\nbar: 1, 2, 3\n```\n\n```cs\nvar a = 1;\n```\n");
var options = new MarkdownHtmlRendererOptions { NodeRenderers = [new ChartRenderer()] };

// The chart is written by the node renderer, the other code block as usual
Assert.Equal("<canvas class=\"chart\" data-spec=\"bar: 1, 2, 3\n\"></canvas>\n<pre><code class=\"language-cs\">var a = 1;\n</code></pre>\n", MarkdownHtmlRenderer.ToHtml(tree, options));

// The plain text has the text of the chart, without its HTML
Assert.Equal("bar: 1, 2, 3\nvar a = 1;\n", MarkdownPlainTextRenderer.ToPlainText(tree, options));
````
<!-- endref -->

[WriteDefault](cref:Meziantou.Framework.Markdown.MarkdownHtmlWriter.WriteDefault(Meziantou.Framework.Markdown.MarkdownSyntaxNode))
writes a node as the renderer writes it, to add HTML around it. These node renderers write the
[custom containers](05-Extensions/19-CustomContainers.md) named `details` as a `<details>` element, and the tables in a
`<div>` that scrolls:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlNodeRenderer_Default;format=md-fenced-code-block;dedent=true -->
```csharp
// Writes the custom containers named "details" as a <details> element, whose arguments are the summary
private sealed class DetailsRenderer : MarkdownHtmlNodeRenderer
{
    public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
    {
        if (node is not MarkdownCustomContainerSyntax { Name: "details" } container)
        {
            return false;
        }

        writer.EnsureNewLine();
        writer.WriteRawHtml("<details");
        writer.WriteAttributes(container);
        writer.WriteRawHtml("><summary>");
        writer.WriteText(container.Arguments);
        writer.WriteRawHtml("</summary>\n");
        writer.WriteBlocks(container.Blocks);
        writer.EnsureNewLine();
        writer.WriteRawHtml("</details>\n");
        return true;
    }
}

// Writes the tables as usual, in a <div> that scrolls
private sealed class ScrollableTableRenderer : MarkdownHtmlNodeRenderer
{
    public override bool TryWrite(MarkdownSyntaxNode node, MarkdownHtmlWriter writer)
    {
        if (node is not MarkdownTableSyntax)
        {
            return false;
        }

        writer.EnsureNewLine();
        writer.WriteRawHtml("<div class=\"table-scroll\">\n");
        writer.WriteDefault(node);
        writer.EnsureNewLine();
        writer.WriteRawHtml("</div>\n");
        return true;
    }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlRenderer_NodeRenderersDefault;format=md-fenced-code-block;dedent=true -->
```csharp
var parseOptions = new MarkdownParseOptions { Extensions = MarkdownExtensions.PipeTables | MarkdownExtensions.CustomContainers };
var tree = MarkdownSyntaxTree.ParseText(":::details Show the prices\n| Fruit | Price |\n|---|---|\n| Apple | *1* |\n:::\n", parseOptions);
var options = new MarkdownHtmlRendererOptions { NodeRenderers = [new DetailsRenderer(), new ScrollableTableRenderer()] };

Assert.Equal("<details><summary>Show the prices</summary>\n<div class=\"table-scroll\">\n<table>\n<thead>\n<tr>\n<th>Fruit</th>\n<th>Price</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>Apple</td>\n<td><em>1</em></td>\n</tr>\n</tbody>\n</table>\n</div>\n</details>\n", MarkdownHtmlRenderer.ToHtml(tree, options));
```
<!-- endref -->

The renderer still writes what a node adds to the blocks around it: the checkbox of a task list item starts the HTML of
its first paragraph, and the links back to the references of a footnote follow the HTML of its last paragraph.

[MarkdownPlainTextRenderer](cref:Meziantou.Framework.Markdown.MarkdownPlainTextRenderer) uses the node renderers too.
Then [IsPlainText](cref:Meziantou.Framework.Markdown.MarkdownHtmlWriter.IsPlainText) is `true`: the text is written as it
is, and the HTML, the attributes and the URLs are not written.

The HTML of [WriteRawHtml](cref:Meziantou.Framework.Markdown.MarkdownHtmlWriter.WriteRawHtml(System.ReadOnlySpan{System.Char}))
is not checked: the [options that protect against untrusted Markdown](07-Security.md) apply to the Markdown, not to
the HTML of node renderers. Write the text of the document with `WriteText` or `WriteAttributeValue`.

One node renderer writes every document, possibly on several threads at once: keep no state between calls.
