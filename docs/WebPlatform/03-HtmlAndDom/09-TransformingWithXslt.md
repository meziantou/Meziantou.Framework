---
title: Transforming XML with XSLT
description: Transform XML documents with XSLT 1.0 stylesheets, as XSLTProcessor and the xml-stylesheet processing instruction do in a browser.
---

[XsltProcessor](cref:Meziantou.Framework.WebPlatform.Dom.XsltProcessor) runs XSLT 1.0 stylesheets, as `XSLTProcessor` does in a browser. Import the stylesheet,
then transform a node into a fragment of a document, or into a new document:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/XPath/XPathTests.cs;region=WebPlatform_XsltTransformToFragment;format=md-fenced-code-block;dedent=true -->
```csharp
var stylesheet = XmlDocumentParser.ParseDocument("""
    <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
      <xsl:template match="/">
        <ul>
          <xsl:for-each select="catalog/book">
            <xsl:sort select="@year" data-type="number"/>
            <li><xsl:value-of select="title"/></li>
          </xsl:for-each>
        </ul>
      </xsl:template>
    </xsl:stylesheet>
    """);
var source = XmlDocumentParser.ParseDocument("""
    <catalog>
      <book year="2004"><title>Second</title></book>
      <book year="1999"><title>First</title></book>
    </catalog>
    """);
var page = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><body></body>");

var processor = new XsltProcessor();
processor.ImportStylesheet(stylesheet);

// The elements in no namespace are HTML elements of the output document
var fragment = processor.TransformToFragment(source, page);
page.Body!.AppendChild(fragment);
Assert.Equal("<ul><li>First</li><li>Second</li></ul>", page.Body.InnerHtml);
```
<!-- endref -->

The stylesheet is a document whose document element is `xsl:stylesheet` or `xsl:transform`, or a document whose root is a
literal result element with an `xsl:version` attribute. It is compiled by the first transformation, then reused until
[ImportStylesheet](cref:Meziantou.Framework.WebPlatform.Dom.XsltProcessor.ImportStylesheet) or [Reset](cref:Meziantou.Framework.WebPlatform.Dom.XsltProcessor.Reset) is called.

## Output methods

The output method of `xsl:output` determines the nodes of the result:

| Method | [TransformToFragment](cref:Meziantou.Framework.WebPlatform.Dom.XsltProcessor.TransformToFragment) | [TransformToDocument](cref:Meziantou.Framework.WebPlatform.Dom.XsltProcessor.TransformToDocument) |
| --- | --- | --- |
| `html` | The elements in no namespace are HTML elements | An HTML document, parsed from the serialization of the result (in quirks mode without `doctype-system`) |
| `xml` | The elements keep their namespace | An XML document; a result that has several elements, or text, is wrapped in a `transformiix:result` element |
| `text` | A text node | An HTML document whose text is in a `pre` element |
| None | `html` when the output document is an HTML document, else `xml` | `html` when the first element of the result is an `html` element in no namespace, else `xml` |

The scripts of a fragment run when it is inserted in a document of a browsing context. The scripts of a document created by
[TransformToDocument](cref:Meziantou.Framework.WebPlatform.Dom.XsltProcessor.TransformToDocument) never run, even when they are moved to another document.

## Parameters

[SetParameter](cref:Meziantou.Framework.WebPlatform.Dom.XsltProcessor.SetParameter) gives a value to a global `xsl:param` of the stylesheet. A string, a number and a boolean are the
XPath values; a node, or a list of nodes, is a node-set; another value is converted to a string.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/XPath/XPathTests.cs;region=WebPlatform_XsltParameters;format=md-fenced-code-block;dedent=true -->
```csharp
var processor = new XsltProcessor();
processor.ImportStylesheet(XmlDocumentParser.ParseDocument("""
    <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
      <xsl:output method="xml"/>
      <xsl:param name="greeting" select="'Hello'"/>
      <xsl:template match="/">
        <message><xsl:value-of select="concat($greeting, ', ', name)"/></message>
      </xsl:template>
    </xsl:stylesheet>
    """));
processor.SetParameter("", "greeting", "Bonjour");

// The result is a new XML document: its scripts never run
var result = processor.TransformToDocument(XmlDocumentParser.ParseDocument("<name>Alice</name>"));
Assert.Equal("application/xml", result.ContentType);
Assert.Equal("Bonjour, Alice", result.DocumentElement!.TextContent);
```
<!-- endref -->

## XML documents with a stylesheet

A navigation to an XML document whose prolog has an `xml-stylesheet` processing instruction of an XSLT stylesheet
(`type="text/xsl"`) shows the result of the transformation, as the browsers do: the stylesheet is fetched, the document is
transformed, and the result replaces the document in the same window.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/BrowsingContextTests.cs;region=WebPlatform_XsltStylesheetProcessingInstruction;format=md-fenced-code-block;dedent=true -->
```csharp
var loader = new InMemoryLoader(new Dictionary<string, (string Content, string ContentType)>(StringComparer.Ordinal)
{
    ["https://example.com/feed.xml"] = ("""
        <?xml-stylesheet type="text/xsl" href="feed.xsl"?>
        <feed><title>News</title><entry>First</entry><entry>Second</entry></feed>
        """, "application/xml"),
    ["https://example.com/feed.xsl"] = ("""
        <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
          <xsl:template match="/feed">
            <html><body><h1><xsl:value-of select="title"/></h1><xsl:apply-templates select="entry"/></body></html>
          </xsl:template>
          <xsl:template match="entry"><p><xsl:value-of select="."/></p></xsl:template>
        </xsl:stylesheet>
        """, "text/xsl"),
});

using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = loader });
context.Navigate("https://example.com/feed.xml");
await context.EventLoop.RunUntilIdleAsync(); // the XML document, its stylesheet, then the result of the transformation

// The result of the transformation replaces the XML document: an HTML document, in quirks mode without doctype
var document = context.Document;
Assert.Equal("text/html", document.ContentType);
Assert.Equal("BackCompat", document.CompatMode);
Assert.Equal("News", document.QuerySelector("h1")!.TextContent);
Assert.Equal(2u, document.QuerySelectorAll("p").Length);
```
<!-- endref -->

- The `href` of the processing instruction is a URL, or `#id` for a stylesheet embedded in the document (the element whose
  `xml:id` or `id` attribute is the id).
- The scripts of the XML document do not run. The scripts of the result run, as in any loaded document.
- When the stylesheet cannot be loaded, or the transformation fails, the XML document is shown.

## Loading other documents

`xsl:include` and `xsl:import` load their stylesheets synchronously, with the fetch of the page (of the document of the
global object that created the [XsltProcessor](cref:Meziantou.Framework.WebPlatform.Dom.XsltProcessor), or of the XML document of a navigation):

- The requests are same-origin requests: a cross-origin stylesheet is not loaded, and the stylesheet is an error.
- Their destination is `xslt`: the `script-src-elem` (or `script-src`, `default-src`) directive of the content security
  policy of the page applies, as for a script.
- The `document()` function loads documents in the transformations of the XML documents of a navigation, checked by the
  `connect-src` directive, as in Chromium. As in Chromium, it returns an empty node-set in the transformations of
  [XsltProcessor](cref:Meziantou.Framework.WebPlatform.Dom.XsltProcessor).

## Limits

A small stylesheet can run for hours (two recursive calls with a counter of 40 make about 10^12 calls) or build a result
that does not fit in memory. The transformations stop at the limits of [BrowsingContextOptions.Xslt](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.Xslt):

| Option | Default | What it counts |
| --- | --- | --- |
| [MaxSteps](cref:Meziantou.Framework.WebPlatform.Dom.XsltOptions.MaxSteps) | 100,000,000 | The instructions instantiated, the expressions evaluated, the patterns matched and the nodes copied |
| [MaxOutputNodes](cref:Meziantou.Framework.WebPlatform.Dom.XsltOptions.MaxOutputNodes) | 2,000,000 | The nodes created, in the result and in the result tree fragments of the variables |
| [Timeout](cref:Meziantou.Framework.WebPlatform.Dom.XsltOptions.Timeout) | 10 seconds | The duration of the transformation; null for no limit |

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/BrowsingContextTests.cs;region=WebPlatform_XsltLimits;format=md-fenced-code-block;dedent=true -->
```csharp
var loader = new InMemoryLoader(new Dictionary<string, (string Content, string ContentType)>(StringComparer.Ordinal)
{
    ["https://example.com/doc.xml"] = ("""<?xml-stylesheet type="text/xsl" href="slow.xsl"?><root>a</root>""", "application/xml"),

    // Two recursive calls with a counter of 40: about 10^12 calls
    ["https://example.com/slow.xsl"] = ("""
        <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
          <xsl:template match="/"><xsl:call-template name="t"><xsl:with-param name="n" select="40"/></xsl:call-template></xsl:template>
          <xsl:template name="t">
            <xsl:param name="n"/>
            <xsl:if test="$n &gt; 0">
              <xsl:call-template name="t"><xsl:with-param name="n" select="$n - 1"/></xsl:call-template>
              <xsl:call-template name="t"><xsl:with-param name="n" select="$n - 1"/></xsl:call-template>
            </xsl:if>
          </xsl:template>
        </xsl:stylesheet>
        """, "text/xsl"),
});

var messages = new List<string>();
using var context = new BrowsingContext(new BrowsingContextOptions
{
    DocumentLoader = loader,
    Xslt = new XsltOptions
    {
        MaxSteps = 1_000_000,         // instructions, expressions, patterns and copied nodes
        MaxOutputNodes = 100_000,     // nodes of the result and of the variables
        Timeout = TimeSpan.FromSeconds(2),
    },
    ConsoleMessage = (_, message) => messages.Add(message.Text),
});
context.Navigate("https://example.com/doc.xml");
await context.EventLoop.RunUntilIdleAsync();

// The transformation fails: the XML document is shown, and the error is reported to the console
Assert.Equal("application/xml", context.Document.ContentType);
Assert.Equal("root", context.Document.DocumentElement!.LocalName);
Assert.Contains(messages, message => message.StartsWith("XSLT transformation failed", StringComparison.Ordinal));
```
<!-- endref -->

- A transformation that exceeds a limit fails like any other transformation: [XsltProcessor](cref:Meziantou.Framework.WebPlatform.Dom.XsltProcessor) throws a
  [DomException](cref:Meziantou.Framework.WebPlatform.Dom.DomException) whose name is `OperationError`, and the navigation to an XML document shows the XML document.
- [XsltProcessor](cref:Meziantou.Framework.WebPlatform.Dom.XsltProcessor) uses the options of the browsing context of the global object that created it, and
  [XsltOptions.Default](cref:Meziantou.Framework.WebPlatform.Dom.XsltOptions.Default) without browsing context.
- A transformation run by a script also stops when the script engine interrupts the script, as a loop of the script would:
  with `Meziantou.Framework.WebPlatform.JavaScript`, the time limit and the memory limit of the engine (the memory the
  transformation allocates counts), or the cancellation of the engine, terminate the script, which cannot catch it.
- The duration is checked between the steps: a single XPath expression over a large document (such as nested `//` steps
  in predicates) runs to its end before the transformation stops.

Lower the limits when the stylesheets or the documents come from an untrusted source.

## Supported features

The stylesheets support XSLT 1.0: template rules, modes, priorities and import precedence, named templates and
parameters, variables and result tree fragments, keys, `xsl:number`, `xsl:sort`, `format-number()` and
`xsl:decimal-format`, attribute sets, `xsl:namespace-alias`, `xsl:strip-space`, and the forwards-compatible processing
with `xsl:fallback`. The `node-set()` and `object-type()` functions of EXSLT (`http://exslt.org/common`) are supported, as
in the browsers. A transformation that fails (a static error of the stylesheet, `xsl:message terminate="yes"`, an unknown
function that is called) throws a [DomException](cref:Meziantou.Framework.WebPlatform.Dom.DomException) whose name is `OperationError`.

The differences with the browsers:

- `disable-output-escaping` is ignored, as in Firefox: the result is a tree, not text.
- The `lang` attributes of `xsl:sort` and `xsl:number` are ignored: the text sort keys are compared by code point, the
  case is only used to order the strings that differ by case.
- `unparsed-entity-uri()` returns an empty string.
- A recursion of more than 3000 nested templates is an error, as in libxslt.

The stylesheets are interpreted: the expressions and the patterns are evaluated by System.Xml.XPath over the DOM, as
[Document.Evaluate](cref:Meziantou.Framework.WebPlatform.Dom.Document.Evaluate) does. The transformations do not generate code, so they work in the applications compiled with native
AOT, and a recursion that does not end is an error, not a crash of the process (`XslCompiledTransform` compiles the
stylesheets to IL, which requires dynamic code, and overflows the stack in such a recursion).
