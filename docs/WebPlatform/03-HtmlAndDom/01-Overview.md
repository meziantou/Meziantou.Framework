---
title: HTML and the DOM
description: Parse HTML and XML as a browser does, query and change the tree, write it back, and insert untrusted markup safely.
---

A document is a tree of nodes: the DOM of the browsers, with the same types and the same members, named the .NET way.
Parse markup into a document, find the nodes you need with CSS selectors, XPath or LINQ, change the tree, then write it
back as HTML.

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

## Documents with and without a browsing context

`HtmlDocumentParser` and `XmlDocumentParser` create documents that are only trees: no window, no event loop, no computed
style and no layout. They are the fastest way to read or transform markup. Everything on the pages of this group works on
them, except the features that need a page:

| Feature | Needs |
| --- | --- |
| Computed styles, media queries, layout and geometry | A [browsing context](../05-LoadingPages/01-Overview.md) |
| Custom elements of the global registry | A browsing context (or a registry given to the parser) |
| The callbacks of mutation observers, the `load` events | The event loop of a browsing context |
| Scripts and event handler attributes | A browsing context with a [script engine](../06-Scripting/01-Overview.md) |

To get a document with a browsing context, navigate a `BrowsingContext` to the markup:
`context.Navigate("https://example.com/", _ => html)` returns the document.

## Naming

The members have the names of the web API, adapted to the .NET guidelines: `querySelector` is `QuerySelector`,
`innerHTML` is `InnerHtml`, `classList` is `ClassList`. Each element has its typed interface (`HtmlAnchorElement`,
`HtmlInputElement`, `SvgCircleElement`...), with the reflected attributes as properties (`Href`, `Value`, `Disabled`).

## In this section

- [Parsing documents](02-ParsingDocuments.md): HTML from strings, bytes and streams, encodings, parse errors, fragments
  and XML.
- [Querying documents](03-QueryingDocuments.md): CSS selectors, typed queries, LINQ tree walks and XPath.
- [Modifying documents](04-ModifyingDocuments.md): create, change, move and remove nodes, and listen to events.
- [Serializing and formatting](05-SerializingAndFormatting.md): write HTML back, minified, indented or with your own
  rules.
- [Sanitizing HTML](06-SanitizingHtml.md): insert untrusted markup without scripts.
- [Custom elements](07-CustomElements.md): define elements with .NET classes.
- [Accessibility](08-Accessibility.md): the role and the accessible name of the elements.
