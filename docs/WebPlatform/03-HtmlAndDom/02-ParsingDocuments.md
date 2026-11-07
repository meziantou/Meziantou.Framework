---
title: Parsing documents
description: Parse HTML from strings, bytes and streams with the right encoding, report the parse errors, parse fragments and XML.
---

[HtmlDocumentParser](cref:Meziantou.Framework.WebPlatform.HtmlDocumentParser) implements the parsing algorithm of the HTML Standard: it builds the same tree as a browser for any
input, valid or not. It never throws on invalid markup.

## Parse a string

`HtmlDocumentParser.ParseDocument(html)` parses a document from a string:

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

The missing elements are created as in a browser: the document always has an `html` element with a `head` and a `body`.
A string has no encoding to determine, so the options about encodings do not apply.

## Parse bytes and streams

A file or an HTTP response is a sequence of bytes. Parse the bytes rather than a string decoded beforehand: the parser
finds the encoding as a browser does, from the byte order mark, the `<meta charset>` element, or the content itself.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Html/EncodingDeclarationTests.cs;region=WebPlatform_ParseBytes;format=md-fenced-code-block;dedent=true -->
```csharp
// The bytes of a page in windows-1252, as they come from a file or an HTTP response
var bytes = Encoding.Latin1.GetBytes("<meta charset=windows-1252><p>Café</p>");

// The encoding is found from the bytes: byte order mark, <meta charset>, or detected from the content
var document = new HtmlDocumentParser().Parse(bytes);
Assert.Equal("windows-1252", document.CharacterSet);
Assert.Equal("Café", document.QuerySelector("p")!.TextContent);

// The charset of the Content-Type header wins over the document, and Url resolves the relative URLs
var parser = new HtmlDocumentParser(new HtmlParserOptions
{
    TransportEncoding = "utf-8",
    Url = "https://example.com/blog/",
});
using var stream = new MemoryStream("<a href='first-post'>Café</a>"u8.ToArray());
var post = await parser.ParseAsync(stream);
var link = post.QuerySelector<HtmlAnchorElement>("a")!;
Assert.Equal("https://example.com/blog/first-post", link.Href);
Assert.Equal("Café", link.TextContent);
```
<!-- endref -->

[HtmlDocumentParser](cref:Meziantou.Framework.WebPlatform.HtmlDocumentParser) instances have the options and the overloads `Parse(string)`, `Parse(ReadOnlySpan<byte>)`,
`Parse(Stream)`, `ParseAsync(string)` and `ParseAsync(Stream)`. The [HtmlParserOptions](cref:Meziantou.Framework.WebPlatform.HtmlParserOptions) are:

| Option | Default | Effect |
| --- | --- | --- |
| `Url` | `about:blank` | The URL of the document, which resolves the relative URLs (`Href`, `Src`...) |
| [TransportEncoding](cref:Meziantou.Framework.WebPlatform.HtmlParserOptions.TransportEncoding) | none | The encoding given by the transport, such as the `charset` of the `Content-Type` header: it wins over the document |
| [DefaultEncoding](cref:Meziantou.Framework.WebPlatform.HtmlParserOptions.DefaultEncoding) | `windows-1252` | The encoding of a document whose encoding cannot be determined |
| [EncodingDetector](cref:Meziantou.Framework.WebPlatform.EncodingDetector) | [EncodingDetector.Default](cref:Meziantou.Framework.WebPlatform.EncodingDetector.Default) | Guesses the encoding of a document that declares none; null disables the detection |
| [ParseErrorHandler](cref:Meziantou.Framework.WebPlatform.HtmlParserOptions.ParseErrorHandler) | none | Called for each parse error (see below) |
| [ScriptingEnabled](cref:Meziantou.Framework.WebPlatform.HtmlParserOptions.ScriptingEnabled) | false | Parses `<noscript>` as a browser with scripting enabled does (as text). Scripts never run in a parsed document |
| [AllowDeclarativeShadowRoots](cref:Meziantou.Framework.WebPlatform.HtmlParserOptions.AllowDeclarativeShadowRoots) | true | `<template shadowrootmode>` creates a shadow root |
| [CustomElementRegistry](cref:Meziantou.Framework.WebPlatform.Dom.CustomElementRegistry) | none | The registry that defines the custom elements the parser creates |

## Report the parse errors

The parse errors do not stop the parser: they describe what a browser fixed. Collect them to validate generated or
hand-written HTML:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Html/ParseErrorTests.cs;region=WebPlatform_ParseErrors;format=md-fenced-code-block;dedent=true -->
```csharp
var errors = new List<HtmlParseError>();
var document = HtmlDocumentParser.ParseDocument("<p>Unclosed <b>bold</p>", new HtmlParserOptions
{
    ParseErrorHandler = errors.Add,
});

// The errors are reported, and the document is built as a browser builds it
Assert.Equal("<p>Unclosed <b>bold</b></p>", document.Body!.InnerHtml);
Assert.Equal(["(1,3): expected-doctype-but-got-other", "(1,23): unexpected-end-tag"], errors.Select(error => error.ToString()));
Assert.Equal((1, 3), (errors[0].Line, errors[0].Column));
```
<!-- endref -->

Each [HtmlParseError](cref:Meziantou.Framework.WebPlatform.HtmlParseError) has the [Code](cref:Meziantou.Framework.WebPlatform.HtmlParseError.Code) of the HTML Standard, the [Line](cref:Meziantou.Framework.WebPlatform.HtmlParseError.Line) and the [Column](cref:Meziantou.Framework.WebPlatform.HtmlParseError.Column), and the [Offset](cref:Meziantou.Framework.WebPlatform.HtmlParseError.Offset) of the character
in the input (in UTF-16 code units). Tracking the positions costs nothing when there is no handler.

## Parse a fragment

A fragment is parsed in the context of an element, because the same markup gives different nodes in different places:
`<td>` creates cells inside a table row, and is ignored inside a `div`.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Html/HtmlInsertionTests.cs;region=WebPlatform_ParseFragment;format=md-fenced-code-block;dedent=true -->
```csharp
var document = HtmlDocumentParser.ParseDocument("<table><tr id=row></tr></table>");
var row = document.GetElementById("row")!;

// The markup is parsed as the content of the context element: here, cells of a table row
var fragment = new HtmlDocumentParser().ParseFragment("<td>a<td>b", row);
row.Append(fragment);
Assert.Equal("<td>a</td><td>b</td>", row.InnerHtml);

// The same, in one step
row.InnerHtml = "<td>c<td>d";
row.InsertAdjacentHtml("beforeend", "<td>e");
Assert.Equal("<td>c</td><td>d</td><td>e</td>", row.InnerHtml);
```
<!-- endref -->

[InnerHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.InnerHtml), [OuterHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.OuterHtml) and [InsertAdjacentHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.InsertAdjacentHtml) parse the markup in the context of the element. To insert markup that
comes from users, use [SetHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.SetHtml) instead: see [Sanitizing HTML](06-SanitizingHtml.md).

## Parse XML

[XmlDocumentParser.ParseDocument](cref:Meziantou.Framework.WebPlatform.XmlDocumentParser.ParseDocument) parses an XML document: XHTML, SVG, feeds or any other XML vocabulary. The elements are
in their namespaces, and the XHTML elements get their typed interfaces.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Xml/XmlParserTests.cs;region=WebPlatform_ParseXml;format=md-fenced-code-block;dedent=true -->
```csharp
var feed = XmlDocumentParser.ParseDocument("""
    <?xml version="1.0" encoding="utf-8"?>
    <feed xmlns="http://www.w3.org/2005/Atom">
      <entry><title>First post</title></entry>
      <entry><title>Second post</title></entry>
    </feed>
    """);

var titles = feed.GetElementsByTagNameNS("http://www.w3.org/2005/Atom", "title").Select(title => title.TextContent);
Assert.Equal(["First post", "Second post"], titles);

// A document that is not well-formed throws
Assert.Throws<XmlException>(() => XmlDocumentParser.ParseDocument("<root><item></root>"));

// DOMParser returns a parsererror document instead, as in a browser
var invalid = new DomParser().ParseFromString("<root><item></root>", DomParserSupportedType.ApplicationXml);
Assert.Equal("parsererror", invalid.DocumentElement!.LocalName);
```
<!-- endref -->

- Parse XML bytes with `ParseDocument(ReadOnlySpan<byte>)`, or streams with `ParseDocument(Stream)` and
  [ParseDocumentAsync](cref:Meziantou.Framework.WebPlatform.XmlDocumentParser.ParseDocumentAsync). The byte order mark and
  XML byte signature determine the byte order; the XML declaration selects a supported .NET encoding. Without either,
  the encoding is UTF-8. Malformed byte sequences, unsupported encodings and declarations conflicting with a byte order
  mark or signature throw `XmlException`. Strings are already decoded: their declarations never change their characters.
- Stream overloads read from the current position, buffer the input and leave the caller-owned stream open, including
  on errors or cancellation. The asynchronous overload honors cancellation during reading and while waiting for the parser;
  DOM construction itself is synchronous. These overloads do not promise incremental or constant-memory parsing.
- External entities are never fetched, and the entity expansion limit also applies to byte and stream inputs.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Xml/XmlParserTests.cs;region=WebPlatform_ParseXmlBytes;format=md-fenced-code-block;dedent=true -->
```csharp
var bytes = Encoding.Latin1.GetBytes("<?xml version='1.0' encoding='iso-8859-1'?><root>caf\u00e9</root>");
var document = XmlDocumentParser.ParseDocument(bytes);
Assert.Equal("caf\u00e9", document.DocumentElement!.TextContent);

using var stream = new MemoryStream(bytes);
var streamed = await XmlDocumentParser.ParseDocumentAsync(stream, cancellationToken: CancellationToken.None);
Assert.Equal("caf\u00e9", streamed.DocumentElement!.TextContent);
Assert.True(stream.CanRead);
```
<!-- endref -->

- [XmlParserOptions.ContentType](cref:Meziantou.Framework.WebPlatform.XmlParserOptions.ContentType) is the type of the document: `application/xml` (the default), `text/xml`,
  `application/xhtml+xml` or `image/svg+xml`. `Url` is its URL.
- Use [GetElementsByTagNameNS](cref:Meziantou.Framework.WebPlatform.Dom.Element.GetElementsByTagNameNS) and the namespace-aware members ([NamespaceUri](cref:Meziantou.Framework.WebPlatform.Dom.Element.NamespaceUri), [LocalName](cref:Meziantou.Framework.WebPlatform.Dom.Element.LocalName)) with XML: element names are
  case-sensitive.
- [XmlSerializer](cref:Meziantou.Framework.WebPlatform.Dom.XmlSerializer) writes a node back as XML (`new XmlSerializer().SerializeToString(node)`).

## Documents and browsing contexts

A parsed document has no browsing context: its scripts never run, and there is no window, computed style or layout. To
load a document as a page, navigate a browsing context to it; the browsing context uses the same parser, with the URL and
the encoding of the response. See [Loading pages](../05-LoadingPages/01-Overview.md).
