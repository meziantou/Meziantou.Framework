---
title: Querying documents
description: Find nodes with CSS selectors, typed queries, LINQ tree walks and XPath.
---

## CSS selectors

`QuerySelector` and `QuerySelectorAll` take any selector of [Selectors Level 4](https://drafts.csswg.org/selectors-4/),
as in a browser: combinators, attribute selectors, `:is()`, `:where()`, `:not()`, `:has()`, `:nth-child(An+B of S)`...
The generic overloads return the elements of a type:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/BrowsingContextTests.cs;region=WebPlatform_ParseAndQuery;format=md-fenced-code-block;dedent=true -->
```csharp
var document = HtmlDocumentParser.ParseDocument("""
    <style>.important { color: red; font-weight: bold }</style>
    <ul>
      <li><a href="https://example.com/a" class="important">A</a>
      <li><a href="/b" style="color: blue">B</a>
    </ul>
    """);

// CSS selectors, with typed overloads and LINQ
var links = document.QuerySelectorAll<HtmlAnchorElement>("li > a");
var external = links.Where(a => a.Href.StartsWith("https:", StringComparison.Ordinal)).Select(a => a.TextContent);
var first = document.GetElementsByTagName("li")[0];
Assert.Equal(["A"], external);
Assert.Same(first, links[0].ParentElement);

// XPath
var result = document.Evaluate("//a[@class='important']", document, type: XPathResult.FirstOrderedNodeType);
var important = (Element?)result.SingleNodeValue;
Assert.Equal("A", important?.TextContent);

// Inline style (style attribute)
var color = links[1].Style.GetPropertyValue("color");
Assert.Equal("blue", color);
```
<!-- endref -->

- `QuerySelector<T>` returns the first element that matches **and** is a `T`: it skips the matching elements of another
  type.
- `Matches(selector)` tests an element, and `Closest(selector)` returns the nearest ancestor (or the element) that
  matches.
- An invalid selector throws a `DomException` whose `Name` is `SyntaxError`.
- `GetElementById`, `GetElementsByTagName`, `GetElementsByClassName` and `GetElementsByName` are available too. The
  collections they return are live: they follow the changes of the document.

## LINQ and tree walks

The DOM collections (`NodeList`, `HtmlCollection`, `NamedNodeMap`, `DomTokenList`) are read-only lists, so LINQ works on
them directly. The tree walks return lazy sequences, as in LINQ to XML:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/LinqHelperTests.cs;region=WebPlatform_TreeWalks;format=md-fenced-code-block;dedent=true -->
```csharp
var document = HtmlDocumentParser.ParseDocument("""
    <article><header><a href="/a">A</a></header><p>Read <a href="/b">B</a></p></article>
    <article><header><a href="/c">C</a></header></article>
    """);

// Lazy walks of the tree, as in LINQ to XML
var texts = document.Body!.Descendants<HtmlAnchorElement>().Select(link => link.TextContent);
Assert.Equal(["A", "B", "C"], texts);

var link = document.QuerySelector("a[href='/b']")!;
Assert.Equal(["p", "article", "body", "html"], link.Ancestors<Element>().Select(element => element.LocalName));

// Search the descendants of every node of a sequence
var articles = document.QuerySelectorAll("article");
var headerLinks = articles.QuerySelectorAll<HtmlAnchorElement>(":scope > header a");
Assert.Equal(["A", "C"], headerLinks.Select(link => link.TextContent));

// Filter elements with a selector parsed once
var inParagraph = SelectorList.Parse("p a");
Assert.Equal(["B"], document.Body.Descendants<Element>().Where(element => element.Matches(inParagraph)).Select(element => element.TextContent));
```
<!-- endref -->

| Method | Nodes |
| --- | --- |
| `Descendants()`, `Descendants<T>()` | The descendants, in tree order |
| `DescendantsAndSelf()`, `DescendantsAndSelf<T>()` | The node, then its descendants |
| `Ancestors()`, `Ancestors<T>()` | The ancestors, from the parent to the root |
| `AncestorsAndSelf()`, `AncestorsAndSelf<T>()` | The node, then its ancestors |

- The walks follow the live tree: a change made during the enumeration is visible to it. The ancestors stop at the root
  of the tree, and do not go from a shadow root to its host.
- `QuerySelector` and `QuerySelectorAll` also work on any `IEnumerable<Node>`: they search the descendants of each node,
  return the results in the order of the sequence, and return an element found from several nodes once.
- Parse a selector once with `SelectorList.Parse` when you match it against many elements: see
  [Style sheets](../04-Css/03-StyleSheets.md#inspect-the-selectors).

## XPath

`Document.Evaluate` evaluates an XPath 1.0 expression, as `document.evaluate()` does in a browser:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/XPath/XPathTests.cs;region=WebPlatform_XPath;format=md-fenced-code-block;dedent=true -->
```csharp
var document = HtmlDocumentParser.ParseDocument("""
    <ul>
      <li data-price="12">Pen</li>
      <li data-price="45">Bag</li>
      <li data-price="30">Book</li>
    </ul>
    """);

// A snapshot of the matching nodes, in document order
var result = document.Evaluate("//li[@data-price > 20]", document, type: XPathResult.OrderedNodeSnapshotType);
var names = new List<string>();
for (var i = 0u; i < result.SnapshotLength; i++)
{
    names.Add(result.SnapshotItem(i)!.TextContent!);
}

Assert.Equal(["Bag", "Book"], names);

// The other result types: numbers, strings and booleans
var total = document.Evaluate("sum(//li/@data-price)", document, type: XPathResult.NumberType).NumberValue;
Assert.Equal(87, total);
```
<!-- endref -->

The `type` argument is one of the constants of `XPathResult`: `FirstOrderedNodeType` (read `SingleNodeValue`),
`OrderedNodeSnapshotType` (read `SnapshotLength` and `SnapshotItem`), `NumberType`, `StringType`, `BooleanType`, or the
iterator types. In an XML document, pass an `IXPathNSResolver` to resolve the namespace prefixes of the expression.

## Text and attributes

- `TextContent` is the text of the node and its descendants, as written in the markup. `InnerText` is the text as
  rendered, with the line breaks of the layout; it needs a browsing context to apply the styles.
- `GetAttribute`, `HasAttribute` and `Attributes` read the attributes. The typed properties (`HtmlAnchorElement.Href`,
  `HtmlImageElement.Src`) return the URL resolved against the URL of the document.
