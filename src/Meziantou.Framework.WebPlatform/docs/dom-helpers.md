# DOM helpers

The DOM follows the specifications, so it is close to the JavaScript API. A few .NET helpers, which are not part of
the specifications, make it easier to use with LINQ.

## Collections

The DOM collections are read-only lists, so the `System.Linq` operators work on them directly:

| Type | Implements |
|---|---|
| `NodeList` (`ChildNodes`, `QuerySelectorAll`…) | `IReadOnlyList<Node>` |
| `HtmlCollection` (`Children`, `GetElementsByTagName`…) | `IReadOnlyList<Element>` |
| `NamedNodeMap` (`Attributes`) | `IReadOnlyList<Attr>` |
| `DomTokenList` (`ClassList`…) | `IReadOnlyList<string>` |

```csharp
var titles = document.QuerySelectorAll<HtmlAnchorElement>("a")
    .Where(a => a.Href.StartsWith("https:", StringComparison.Ordinal))
    .Select(a => a.TextContent);
```

## Tree walks

These methods return lazy sequences, as their LINQ to XML equivalents do. They walk the live tree, so a mutation during
the enumeration is visible to it.

| Method | Nodes |
|---|---|
| `Node.Descendants()`, `Node.Descendants<T>()` | The descendants in tree order |
| `Node.DescendantsAndSelf()`, `Node.DescendantsAndSelf<T>()` | The node, then its descendants in tree order |
| `Node.Ancestors()`, `Node.Ancestors<T>()` | The ancestors, from the parent to the root |
| `Node.AncestorsAndSelf()`, `Node.AncestorsAndSelf<T>()` | The node, then its ancestors |

The generic overloads return the nodes of type `T` (for example `Descendants<Element>()` or
`Ancestors<HtmlFormElement>()`). The ancestors stop at the root of the tree. They do not continue from a shadow root to
its host.

## Typed queries

`Document`, `DocumentFragment` and `Element` have generic overloads of `QuerySelector` and `QuerySelectorAll`. They
take a selector string or a parsed `SelectorList` (see [selectors.md](selectors.md)), and return the matching elements
of type `T`:

```csharp
HtmlAnchorElement? link = document.QuerySelector<HtmlAnchorElement>(".external");
IReadOnlyList<HtmlImageElement> images = document.QuerySelectorAll<HtmlImageElement>("img");
```

`QuerySelector<T>` returns the first element that matches **and** is a `T`. It does not return null when the first
match has another type and a later match is a `T`.

## Queries on a sequence of nodes

`NodeEnumerableExtensions` adds `QuerySelector`, `QuerySelectorAll`, `QuerySelector<T>` and `QuerySelectorAll<T>` to
any `IEnumerable<Node>`, such as a `NodeList`, an `HtmlCollection` or the result of a LINQ query:

```csharp
var articles = document.QuerySelectorAll("article");
var links = articles.QuerySelectorAll<HtmlAnchorElement>(":scope > header a");
```

- They search the descendants of each node of the sequence, as `element.QuerySelectorAll()` does. The nodes of the
  sequence are not part of the result, and `:scope` is the node being searched.
- The nodes that cannot have children elements are skipped (text, comments…).
- The results follow the order of the sequence, then the tree order. An element found from several nodes of the
  sequence (nested nodes) is returned once.

To filter the elements of a sequence, use `Matches`:

| AngleSharp | WebPlatform |
|---|---|
| `elements.Filter(selector)` | `elements.Where(e => e.Matches(selector))` |
| `elements.Not(selector)` | `elements.Where(e => !e.Matches(selector))` |
| `element.Is(selector)` | `element.Matches(selector)` |

Parse the selector once with `SelectorList.Parse` when it is used for many elements.

## Creating typed elements

`Document.CreateElement<T>()` creates the HTML element whose element interface is `T`:

```csharp
var link = document.CreateElement<HtmlAnchorElement>();      // <a>
var picture = document.CreateElement<HtmlPictureElement>();  // <picture>
var heading = document.CreateElement<HtmlHeadingElement>("h2");
```

- The tag name of each interface is generated from the HTML specification, so no reflection is used.
- Some interfaces are the element interface of several elements: `HtmlHeadingElement` (`h1`…`h6`), `HtmlQuoteElement`,
  `HtmlModElement`, `HtmlTableCellElement`, `HtmlTableSectionElement`, `HtmlTableColElement`, `HtmlPreElement`,
  `HtmlElement` and `HtmlUnknownElement`. For these, `CreateElement<T>()` throws `InvalidOperationException`.
- `CreateElement<T>(localName)` creates the element as `CreateElement(localName)` does, and casts it. It throws
  `ArgumentException` when the element is not a `T`. This happens in an XML document, where `CreateElement` creates
  elements in no namespace.
