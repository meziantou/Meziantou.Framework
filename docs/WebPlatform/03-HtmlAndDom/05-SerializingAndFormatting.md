---
title: Serializing and formatting
description: Write a document or a node back as HTML, minified, indented, with formatted CSS or with your own rules.
---

## Write a node as HTML

`InnerHtml` and `OuterHtml` return the markup of an element, and `Document.GetHtml()` the markup of a whole document, as
a browser serializes them. `ToHtml(formatter)` writes a node with a formatter of the
[Meziantou.Framework.WebPlatform.Formatting](cref:Meziantou.Framework.WebPlatform.Formatting) namespace, and `ToHtml(TextWriter, formatter)` writes it to a `TextWriter`
without building a string:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/BrowsingContextTests.cs;region=WebPlatform_UpdateDom;format=md-fenced-code-block;dedent=true -->
```csharp
var document = HtmlDocumentParser.ParseDocument("""
    <ul>
      <li><a href="/a">A</a>
      <li><a href="/b">B</a>
    </ul>
    """);
var list = document.QuerySelector("ul")!;

// Create elements
var item = document.CreateElement<HtmlLIElement>();
var link = document.CreateElement<HtmlAnchorElement>();
link.Href = "/c";
link.TextContent = "C";
link.ClassList.Add("important");
link.SetAttribute("data-id", "3");
item.Append(link);
list.Append(item);

// Parse markup into an element, edit the styles, remove nodes
list.InsertAdjacentHtml("beforeend", "<li><a href=/d>D</a>");
link.Style.SetProperty("text-decoration", "underline");
document.QuerySelector("a[href='/b']")?.Remove();

// Serialize the document
var html = document.GetHtml();
var indented = document.ToHtml(new HtmlPrettyFormatter());
var minified = document.ToHtml(new HtmlMinifyFormatter());
Assert.StartsWith("<html><head></head><body><ul>", html, StringComparison.Ordinal);
Assert.Equal("""
    <html>
      <head></head>
      <body>
        <ul>
          <li>
            <a href="/a">A</a>
          </li>
          <li></li>
          <li>
            <a href="/c" class="important" data-id="3" style="text-decoration: underline;">C</a>
          </li>
          <li>
            <a href="/d">D</a>
          </li>
        </ul>
      </body>
    </html>
    """, indented);
Assert.Equal("""<ul><li><a href=/a>A</a><li><li><a href=/c class=important data-id=3 style="text-decoration: underline;">C</a><li><a href=/d>D</a></ul>""", minified);
```
<!-- endref -->

| Formatter | Output |
| --- | --- |
| `HtmlFormatter.Default` (no formatter) | The serialization of the browsers: the same markup as `OuterHtml` and `GetHtml()` |
| `HtmlMinifyFormatter` | A smaller markup that is parsed to the same document and rendered the same way |
| `HtmlPrettyFormatter` | An indented markup, to read or compare documents |

An element is written with its tags; a document or a fragment is written as its children. The formatters are immutable:
create them once and share them between threads. They never change the document.

## Minify

`HtmlMinifyFormatter` removes the comments, collapses the whitespace, omits the optional tags (`<html>`, `<head>`,
`</p>`, `</li>`...) and removes the quotes of the attributes that do not need them. Each transformation has an option:
`RemoveComments`, `CollapseWhitespace`, `TrimTrailingWhitespaceInPreformattedText`, `OmitOptionalTags`,
`RemoveAttributeQuotes` and `RemoveScriptTypeAttribute` (all true by default), and `PreservedStartTags` keeps the start
tags that some tools expect (`["html", "head"]`).

The whitespace that matters depends on the styles: a space between two inline elements is rendered, a space between two
blocks is not. The minifier computes the `display` and `white-space` of the elements from the style sheets of the
document, and keeps the whitespace that is significant for any media query. The content of `pre`, `textarea`, `script`
and `style`, and of the SVG and MathML elements, is not modified.

## Indent

`HtmlPrettyFormatter` writes each node on its own line, indented by its depth (`Indentation`, two spaces by default;
`NewLine`, `"\n"` by default). An element whose only content is a text is written on one line, and the content of `pre`,
`textarea`, `script` and `style` is written as it is. Two documents that differ only by their whitespace have the same
indented output: use it to compare HTML in tests, not to write the pages you serve.

## Format the CSS of the page

By default, the content of the `style` elements and the `style` attributes is written as it is. Set
`StyleElementFormatter` and `StyleAttributeFormatter` to format them with a [CSS formatter](../04-Css/05-FormattingCss.md):

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Formatting/HtmlStyleFormattingTests.cs;region=WebPlatform_FormatStyles;format=md-fenced-code-block;dedent=true -->
```csharp
var document = HtmlDocumentParser.ParseDocument("""
    <style>
      /* The title */
      h1 { color: #ff0000; margin: 0px 0px 0px 0px }
    </style>
    <h1 style="font-weight : bold ;">Title</h1>
    """);

var minified = document.ToHtml(new HtmlMinifyFormatter
{
    StyleElementFormatter = new CssMinifyFormatter(),
    StyleAttributeFormatter = new CssMinifyFormatter(),
});
Assert.Equal("<style>h1{color:#f00;margin:0px}</style><h1 style=font-weight:bold>Title</h1>", minified);
```
<!-- endref -->

The CSS is parsed then written back, so what a browser drops is dropped: the comments, the invalid rules and the
declarations of unsupported properties. This is why the two options are off by default.

## Write your own formatter

Derive from `HtmlFormatter` and `HtmlMarkupWriter`. The writer calls a virtual method for each node, whose default is the
serialization of the browsers; override the ones you need. `ShouldWrite` skips a node and its descendants:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Formatting/HtmlFormatterTests.cs;region=WebPlatform_CustomHtmlFormatter;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>Writes the markup of the nodes without their comments.</summary>
private sealed class NoCommentFormatter : HtmlFormatter
{
    protected override HtmlMarkupWriter CreateWriter(TextWriter output) => new Writer(output);

    private sealed class Writer(TextWriter output) : HtmlMarkupWriter(output)
    {
        protected override bool ShouldWrite(Node node) => node is not Comment;
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Formatting/HtmlFormatterTests.cs;region=WebPlatform_UseCustomHtmlFormatter;format=md-fenced-code-block;dedent=true -->
```csharp
var document = HtmlDocumentParser.ParseDocument("<!-- build 42 --><p>Hello <!-- todo -->world</p>");
var html = document.Body!.ToHtml(new NoCommentFormatter());
Assert.Equal("<body><p>Hello world</p></body>", html);
```
<!-- endref -->

The writer walks the tree without recursion, so deep documents do not overflow the stack. `WriteAttribute`,
`WriteRawText` and the other methods change how each part is written, and `Depth` is the depth of the current node.
`HtmlFormatter.IsVoidElement`, `HasRawTextContent`, `GetSerializedTagName`, `GetSerializedAttributeName` and
`WriteEscaped` are the building blocks of the serialization.
