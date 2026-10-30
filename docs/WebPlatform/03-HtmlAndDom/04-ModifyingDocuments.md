---
title: Modifying documents
description: Create, change, move and remove nodes, edit the attributes and the classes, and listen to events.
---

The DOM methods change the tree as in a browser. A document parsed by [HtmlDocumentParser](cref:Meziantou.Framework.WebPlatform.HtmlDocumentParser) and a page loaded in a
browsing context are changed the same way.

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

## Create elements

[Document.CreateElement\<T\>()](cref:Meziantou.Framework.WebPlatform.Dom.Document.CreateElement) creates the HTML element of an element interface: [CreateElement\<HtmlAnchorElement\>()](cref:Meziantou.Framework.WebPlatform.Dom.Document.CreateElement)
creates an `a` element. Some interfaces belong to several elements ([HtmlHeadingElement](cref:Meziantou.Framework.WebPlatform.Dom.HtmlHeadingElement) is `h1` to `h6`,
[HtmlTableCellElement](cref:Meziantou.Framework.WebPlatform.Dom.HtmlTableCellElement) is `td` and `th`): [CreateElement\<T\>()](cref:Meziantou.Framework.WebPlatform.Dom.Document.CreateElement) throws an `InvalidOperationException` for them, so give the
name with `CreateElement<HtmlHeadingElement>("h2")`. `CreateElement(name)` and `CreateElementNS(namespace, name)` work as
in a browser, and [CreateTextNode](cref:Meziantou.Framework.WebPlatform.Dom.Document.CreateTextNode) and [CreateComment](cref:Meziantou.Framework.WebPlatform.Dom.Document.CreateComment) create the other nodes.

## Insert, move and remove nodes

| Members | Effect |
| --- | --- |
| [Append](cref:Meziantou.Framework.WebPlatform.Dom.Element.Append), [Prepend](cref:Meziantou.Framework.WebPlatform.Dom.Element.Prepend) | Add nodes or strings at the end or the start of the children |
| [Before](cref:Meziantou.Framework.WebPlatform.Dom.Element.Before), [After](cref:Meziantou.Framework.WebPlatform.Dom.Element.After), [ReplaceWith](cref:Meziantou.Framework.WebPlatform.Dom.Element.ReplaceWith) | Insert nodes next to the node, or replace it |
| [AppendChild](cref:Meziantou.Framework.WebPlatform.Dom.Node.AppendChild), [InsertBefore](cref:Meziantou.Framework.WebPlatform.Dom.Node.InsertBefore), [ReplaceChild](cref:Meziantou.Framework.WebPlatform.Dom.Node.ReplaceChild), [RemoveChild](cref:Meziantou.Framework.WebPlatform.Dom.Node.RemoveChild) | The classic methods |
| [Remove()](cref:Meziantou.Framework.WebPlatform.Dom.Element.Remove) | Remove the node from its parent |
| [MoveBefore](cref:Meziantou.Framework.WebPlatform.Dom.Element.MoveBefore) | Move a node without removing it first (it keeps its state) |
| `CloneNode(deep)`, [ImportNode](cref:Meziantou.Framework.WebPlatform.Dom.Document.ImportNode), [AdoptNode](cref:Meziantou.Framework.WebPlatform.Dom.Document.AdoptNode) | Copy a node, or bring a node from another document |
| `TextContent = ...` | Replace the children with a text |
| [InnerHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.InnerHtml), [OuterHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.OuterHtml), [InsertAdjacentHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.InsertAdjacentHtml) | Parse trusted markup into the element |

A node belongs to one place of one tree: appending a node that is already in the tree moves it.

## Attributes, classes and inline styles

- [SetAttribute](cref:Meziantou.Framework.WebPlatform.Dom.Element.SetAttribute), [RemoveAttribute](cref:Meziantou.Framework.WebPlatform.Dom.Element.RemoveAttribute) and [ToggleAttribute](cref:Meziantou.Framework.WebPlatform.Dom.Element.ToggleAttribute) change the attributes; the typed properties ([Href](cref:Meziantou.Framework.WebPlatform.Dom.HtmlAnchorElement.Href), [Id](cref:Meziantou.Framework.WebPlatform.Dom.Element.Id),
  `Disabled`, `Value`...) reflect them.
- [ClassList](cref:Meziantou.Framework.WebPlatform.Dom.Element.ClassList) adds, removes, toggles and tests the classes; [Dataset](cref:Meziantou.Framework.WebPlatform.Dom.HtmlElement.Dataset) reads and writes the `data-*` attributes.
- `Style.SetProperty(name, value)` and `Style.GetPropertyValue(name)` edit the `style` attribute, with the CSS syntax. See
  [Typed values](../04-Css/04-TypedValues.md) to set values as objects.

## Events

[AddEventListener](cref:Meziantou.Framework.WebPlatform.Dom.EventTarget.AddEventListener) takes a .NET callback, and [DispatchEvent](cref:Meziantou.Framework.WebPlatform.Dom.EventTarget.DispatchEvent) dispatches an event, with the capture and bubble phases of
the DOM. [HtmlElement.Click()](cref:Meziantou.Framework.WebPlatform.Dom.HtmlElement.Click) dispatches a click as `element.click()` does in a browser. To simulate a user (a mouse that
moves and clicks at a position, the keys of the keyboard), use the input of the browsing context: see
[User input](../05-LoadingPages/06-UserInput.md).

Without a browsing context, the events are dispatched synchronously, but nothing is queued: the callbacks of the mutation
observers are not invoked ([MutationObserver.TakeRecords()](cref:Meziantou.Framework.WebPlatform.Dom.MutationObserver.TakeRecords) still returns the records) and the `load` events are not fired.

## Insert untrusted markup

[InnerHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.InnerHtml) and [InsertAdjacentHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.InsertAdjacentHtml) keep everything, including the `script` elements and the event handler attributes:
they are for trusted markup. To insert markup that comes from users, use [SetHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.SetHtml), which removes everything that can run
scripts: see [Sanitizing HTML](06-SanitizingHtml.md).
