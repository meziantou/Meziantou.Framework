---
title: Modifying documents
description: Create, change, move and remove nodes, edit the attributes and the classes, and listen to events.
---

The DOM methods change the tree as in a browser. A document parsed by `HtmlDocumentParser` and a page loaded in a
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

`Document.CreateElement<T>()` creates the HTML element of an element interface: `CreateElement<HtmlAnchorElement>()`
creates an `a` element. Some interfaces belong to several elements (`HtmlHeadingElement` is `h1` to `h6`,
`HtmlTableCellElement` is `td` and `th`): `CreateElement<T>()` throws an `InvalidOperationException` for them, so give the
name with `CreateElement<HtmlHeadingElement>("h2")`. `CreateElement(name)` and `CreateElementNS(namespace, name)` work as
in a browser, and `CreateTextNode` and `CreateComment` create the other nodes.

## Insert, move and remove nodes

| Members | Effect |
| --- | --- |
| `Append`, `Prepend` | Add nodes or strings at the end or the start of the children |
| `Before`, `After`, `ReplaceWith` | Insert nodes next to the node, or replace it |
| `AppendChild`, `InsertBefore`, `ReplaceChild`, `RemoveChild` | The classic methods |
| `Remove()` | Remove the node from its parent |
| `MoveBefore` | Move a node without removing it first (it keeps its state) |
| `CloneNode(deep)`, `ImportNode`, `AdoptNode` | Copy a node, or bring a node from another document |
| `TextContent = ...` | Replace the children with a text |
| `InnerHtml`, `OuterHtml`, `InsertAdjacentHtml` | Parse trusted markup into the element |

A node belongs to one place of one tree: appending a node that is already in the tree moves it.

## Attributes, classes and inline styles

- `SetAttribute`, `RemoveAttribute` and `ToggleAttribute` change the attributes; the typed properties (`Href`, `Id`,
  `Disabled`, `Value`...) reflect them.
- `ClassList` adds, removes, toggles and tests the classes; `Dataset` reads and writes the `data-*` attributes.
- `Style.SetProperty(name, value)` and `Style.GetPropertyValue(name)` edit the `style` attribute, with the CSS syntax. See
  [Typed values](../04-Css/04-TypedValues.md) to set values as objects.

## Events

`AddEventListener` takes a .NET callback, and `DispatchEvent` dispatches an event, with the capture and bubble phases of
the DOM. `HtmlElement.Click()` dispatches a click as `element.click()` does in a browser. To simulate a user (a mouse that
moves and clicks at a position, the keys of the keyboard), use the input of the browsing context: see
[User input](../05-LoadingPages/06-UserInput.md).

Without a browsing context, the events are dispatched synchronously, but nothing is queued: the callbacks of the mutation
observers are not invoked (`MutationObserver.TakeRecords()` still returns the records) and the `load` events are not fired.

## Insert untrusted markup

`InnerHtml` and `InsertAdjacentHtml` keep everything, including the `script` elements and the event handler attributes:
they are for trusted markup. To insert markup that comes from users, use `SetHtml`, which removes everything that can run
scripts: see [Sanitizing HTML](06-SanitizingHtml.md).
