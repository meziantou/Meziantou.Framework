# Meziantou.Framework.WebPlatform

A spec-compliant, high-performance implementation of the web platform for .NET. It parses HTML, XML, SVG, MathML and CSS
exactly as a browser does, and exposes the browser DOM API (the WebIDL surface of the WHATWG and W3C specifications) with
.NET naming: queries (`QuerySelector`, XPath, live collections), mutations, events, the CSSOM and the computed styles, forms,
custom elements, accessibility, and more. A managed layout engine computes the geometry exposed by the web APIs (CSSOM
View, the observers...), and the documents are painted into a display list that a rendering backend (a managed
rasterizer, in a separate package) rasterizes for the screenshots and the canvases. Documents can be loaded in a
browsing context that fetches their resources over the network and runs their scripts with a pluggable JavaScript
engine ([Meziantou.Framework.JavaScriptEngine](../Meziantou.Framework.JavaScriptEngine/readme.md) is provided).

Every public member mapping a WebIDL member is annotated with `[IdlMember(name, specUrl)]`. The WebIDL names follow the
.NET naming guidelines: acronyms of 3 letters or more are PascalCased and acronyms of 2 letters stay upper case
(`HTMLElement.innerHTML` → `HtmlElement.InnerHtml`, `toJSON` → `ToJson`, `CDATASection` → `CDataSection`, `UIEvent`
and `HTMLBRElement` → `HtmlBRElement`). `MathML` and `XPath` keep their casing.

## Examples

### Parse HTML, query the nodes and the styles

`HtmlDocumentParser` is in the `Meziantou.Framework.WebPlatform` namespace, and the DOM types (`BrowsingContext`,
`Document`, `HtmlAnchorElement`...) are in `Meziantou.Framework.WebPlatform.Dom`.

<!-- ref:../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/BrowsingContextTests.cs;region=WebPlatform_ParseAndQuery;format=md-fenced-code-block;dedent=true -->
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

The computed styles (cascade of the style sheets, inheritance, media queries) need a [browsing
context](docs/browsing-context.md), which provides the window and the viewport:

<!-- ref:../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/BrowsingContextTests.cs;region=WebPlatform_ComputedStyle;format=md-fenced-code-block;dedent=true -->
```csharp
var html = """
    <style>.important { color: red; font-weight: bold }</style>
    <p class="important">Hello</p>
    """;

using var context = new BrowsingContext(new BrowsingContextOptions
{
    Viewport = new ViewportOptions { Width = 1280, Height = 720 }, // optional, 800x600 by default
});

var document = context.Navigate("https://example.com/", _ => html);
var style = context.Window.GetComputedStyle(document.QuerySelector(".important")!);
Assert.Equal("rgb(255, 0, 0)", style.Color);
Assert.Equal("700", style.GetPropertyValue("font-weight"));
```
<!-- endref -->

See [docs/dom-helpers.md](docs/dom-helpers.md), [docs/selectors.md](docs/selectors.md),
[docs/computed-style.md](docs/computed-style.md) and [docs/css-typed-om.md](docs/css-typed-om.md).

### Update the DOM

The formatters are in the `Meziantou.Framework.WebPlatform.Formatting` namespace.

<!-- ref:../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/BrowsingContextTests.cs;region=WebPlatform_UpdateDom;format=md-fenced-code-block;dedent=true -->
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

`Element.SetHtml()` and `Document.ParseHtml()` insert untrusted markup with the HTML Sanitizer API. See
[docs/html-formatting.md](docs/html-formatting.md) and [docs/css-formatting.md](docs/css-formatting.md) for the
serialization options.

### Allow network requests

Nothing is fetched unless a `DocumentLoader` is configured. With a loader, the browsing context loads the documents of
the navigations and the `iframe` elements, the style sheets (`<link rel=stylesheet>`, `@import`), the images and, when a
script engine is configured, the scripts:

<!-- ref:../../samples/Meziantou.Framework.WebPlatform.Samples/ReadmeExamples.cs;region=WebPlatform_AllowNetworkRequests;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    DocumentLoader = DocumentLoader.Create(),  // HTTP GET requests with a shared HttpClient (or pass your own)
    CookieJar = new CookieJar(),               // optional: the cookies (RFC 6265bis, document.cookie, cookieStore)
});

context.Navigate("https://example.com/");
await context.EventLoop.RunUntilIdleAsync(); // runs the tasks until the document and its resources are loaded

var document = context.Document;
```
<!-- endref -->

`DocumentLoader.Create()` requests any URL. A custom loader can filter the requests (to prevent server-side request
forgery with untrusted documents), serve them from a cache or from the disk:

<!-- ref:../../samples/Meziantou.Framework.WebPlatform.Samples/ReadmeExamples.cs;region=WebPlatform_SameHostLoader;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed class SameHostLoader(string host) : DocumentLoader
{
    private static readonly DocumentLoader Http = DocumentLoader.Create();

    public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
    {
        if (new Uri(request.Url).Host != host)
            return ValueTask.FromResult<DocumentResource?>(null); // network error

        return Http.LoadAsync(request, cancellationToken);
    }
}
```
<!-- endref -->

See [docs/browsing-context.md](docs/browsing-context.md#the-document-loader).

### Execute scripts

Scripts only run when a JavaScript engine is plugged into the browsing context. The
`Meziantou.Framework.WebPlatform.JavaScript` package provides one, based on `Meziantou.Framework.JavaScriptEngine`, in the
`Meziantou.Framework.WebPlatform.JavaScript` namespace:

<!-- ref:../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/JavaScriptScriptEngineTests.cs;region=WebPlatformJavaScript_ExecuteScripts;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JavaScriptScriptEngine(new JavaScriptScriptEngineOptions
{
    Timeout = TimeSpan.FromSeconds(5), // per script or callback
});

using var context = new BrowsingContext(new BrowsingContextOptions
{
    ScriptEngine = engine,
    DocumentLoader = DocumentLoader.Create(), // optional: external scripts, modules, fetch, XMLHttpRequest, WebSocket, EventSource...
    UnhandledScriptError = (window, error) => Console.Error.WriteLine(error),
    ConsoleMessage = (window, message) => Console.WriteLine(message.Text), // console.log(), console.count()...
});

var document = await context.NavigateAsync("https://example.com/", _ => """
    <title>Example</title>
    <ul id=list></ul>
    <script>
      setTimeout(() => document.getElementById('list').innerHTML = '<li>a<li>b', 100);
    </script>
    """);

// Run the tasks and the timers until there is nothing left
do { await context.EventLoop.RunUntilIdleAsync(); } while (await context.EventLoop.WaitForNextTimerAsync());

Assert.Equal("<li>a</li><li>b</li>", document.GetElementById("list")!.InnerHtml);

// Evaluate code in the realm of a window
var title = engine.Evaluate(context.Window, "document.title");
Assert.Equal("Example", title.AsString());
```
<!-- endref -->

The scripts run in the process: read the threat model in [docs/scripting.md](docs/scripting.md#threat-model) before
running untrusted documents.

### Take screenshots

The documents are painted by the library into a backend-agnostic display list; the
`Meziantou.Framework.WebPlatform.Imaging` package draws them, and decodes and encodes the images, with
[Meziantou.Framework.Imaging](https://www.nuget.org/packages/Meziantou.Framework.Imaging), a fully managed library (no
native dependency: the same pixels on every platform). `ImagingRenderingBackend` and the screenshot methods are in the
`Meziantou.Framework.WebPlatform.Imaging` namespace:

<!-- ref:../../samples/Meziantou.Framework.WebPlatform.Samples/ReadmeExamples.cs;region=WebPlatform_TakeScreenshots;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    Viewport = new ViewportOptions { Width = 1280, Height = 720, DevicePixelRatio = 2 },
    DocumentLoader = DocumentLoader.Create(),
    RenderingBackend = new ImagingRenderingBackend(),
});

context.Navigate("https://example.com/");
await context.EventLoop.RunUntilIdleAsync();

await File.WriteAllBytesAsync("page.png", context.TakeScreenshotAsPng());            // the viewport: 2560x1440 pixels
using var title = context.Document.QuerySelector("h1")!.TakeScreenshot();           // an element, as an Image<Rgba32>

// Print the document: the print media type, the @page rules, one image per page
var pages = context.RenderPagesAsPng(new PrintOptions { PageWidth = 794, PageHeight = 1123 }); // A4 at 96 dpi
```
<!-- endref -->

See [docs/rendering.md](docs/rendering.md) for what is painted, and [Printing](docs/rendering.md#printing) for the pages.

## Limitations

- **Layout.** The layout engine is internal ([docs/layout.md](docs/layout.md)): it runs on demand, when a web API reads
  the geometry, and it is exposed only through the web APIs: CSSOM View (`getBoundingClientRect()`, `getClientRects()`, the
  `offset*`, `scroll*` and `client*` members, `elementFromPoint()` and the hit testing, `caretPositionFromPoint()`,
  `getBoxQuads()`, the `MouseEvent` coordinates, the scrolling and its events), the used values of `getComputedStyle()`
  (see [docs/computed-style.md](docs/computed-style.md#layout-dependent-values)), the SVG geometry, `ResizeObserver`,
  `IntersectionObserver` and the lazy loading, the layout shifts, the size and scroll-state container queries, the viewport
  units and `window.visualViewport`, `Selection.modify()` and the caret movement keys. It implements the block and inline
  layouts (line breaking, bidirectional text, the CSS Text features), the floats and `shape-outside`, the replaced elements
  and the form controls, the intrinsic sizes, the containment and `content-visibility`, the positioned layout and the anchor
  positioning, the overflow (and `text-overflow`, `line-clamp`), the scrolling, the scroll snapping and the scroll anchoring,
  the scroll markers and the scroll buttons (`::scroll-marker-group`, `::scroll-marker`, `::scroll-button()`, `scroll-target-group`),
  the flex (and `-webkit-box`), grid (with the subgrids), grid lanes (masonry) and table layouts, the ruby layout, the box
  alignment, the gap decorations (the column and row rules of the grid, flex and multi-column containers), the writing modes, the multi-column layout and the block fragmentation in its columns (with the floats pushed to the next column and the repeated table headers and footers), the paged media (the pagination of the printed documents: `@page`, the page breaks, the named pages, the page-margin boxes), the MathML Core layout
  and the SVG layout. The wheel, a panning touch, the keys and the scrollbars scroll (`BrowsingContext.Input`). Not implemented: the selection with the
  pointer, the layout of the pages whose inline size differs from the first page, the repetition of a hyphen at the start of the next line, the spelling changes at a hyphenation break of the languages other than Dutch, Hungarian and Pinyin and the hyphenation
  dictionaries under copyleft licenses (cs, hu, hy, lv), the
  scrolling of a `::scroll-marker-group` that is a scroll container, pinch zoom, and, in the shaping of the complex scripts (the shaper of Meziantou.Framework.Fonts), the Myanmar Zawgyi encoding and
  the Arabic fallback shaping of the Windows-1256 fonts.
- **No rasterizer in the core library.** The documents are painted into a backend-agnostic display list (the painting
  order of CSS, the stacking contexts, the clips, the transforms and effects, the backgrounds, the borders, the outlines, the
  texts with the glyphs of the managed text shaping, the images including the SVG images, the SVG content): the pixels are produced by a rendering
  backend plugged with `BrowsingContextOptions.RenderingBackend`: `ImagingRenderingBackend` of the
  `Meziantou.Framework.WebPlatform.Imaging` package (the managed drawing API of Meziantou.Framework.Imaging), which also takes the screenshots (see
  [docs/rendering.md](docs/rendering.md)) and draws the 2D canvases (`CanvasRenderingContext2D`, `OffscreenCanvas`, also
  in the workers) and the `paint()` images of the paint worklets (`CSS.paintWorklet`, see
  [docs/rendering.md](docs/rendering.md#paint-worklet-images)). Without a backend, nothing is rasterized and `canvas.getContext('2d')` returns null. Not painted yet:
  the color glyphs other than the layers of `COLR` version 0 with the palettes of `font-palette` (the paint graphs of
  `COLR` version 1, the bitmap and SVG glyphs), the wrapping of the SVG texts in the shapes of `shape-inside`. The canvas layers (`beginLayer()`)
  and the `float16` canvases are not implemented.
  As in Blink, the painted boxes and the baselines are snapped to the device pixels and the metrics of the fonts are
  rounded to whole pixels (the layout keeps its fractional geometry).
- **Injected user input.** There is no window: the host injects the pointer and wheel input of the user with
  `BrowsingContext.Input` (mouse, pen and touch pointers, wheel; see [docs/browsing-context.md](docs/browsing-context.md#user-input)),
  which fires the trusted pointer, mouse, touch and click events and runs the default actions (focus, activation, light
  dismiss, drag and drop, scrolling, the picker of the `select` elements: the customizable select, `appearance: base-select`,
  is rendered) and the classic scrollbars (a press on the track, a drag
  of the thumb), and the keys of the keyboard (`BrowsingContext.Input.Keyboard`: the trusted keyboard events, the typing of the
  text, the focus navigation and the shortcuts). There is no pinch zoom. The
  [Playwright server](../Meziantou.Framework.WebPlatform.Playwright/readme.md) automates the pages with the Playwright clients.
- **Opt-in network and scripting.** Without a `DocumentLoader`, nothing is fetched; without a script engine, the scripts
  are never fetched nor run, and the event handler attributes have no effect. A document parsed with
  `HtmlDocumentParser` has no browsing context: no window, no computed style, no event loop (the mutation observer
  callbacks are not invoked).
- **Images are decoded by the rendering backend**, not by the core library: without a backend, only the natural dimensions
  of the images are read from their header (`decode()` resolves once the image is available, `createImageBitmap()` rejects
  the images and the blobs). The Imaging backend decodes PNG (including the animated PNG), JPEG (including CMYK), GIF,
  WebP, AVIF, BMP and ICO images, with their EXIF orientation, their EXIF resolution (the density-corrected size) and their
  color profile.
- **Media (audio, video) are not decoded by the library**: the processing model of the media elements (resource selection,
  events, `play()`, seeking...) runs on top of a media backend provided by the application
  (`BrowsingContextOptions.MediaBackend`, see [docs/browsing-context.md](docs/browsing-context.md#media-elements)); without
  it, the media elements have no media resource. The Media Source Extensions are implemented on top of the segment
  parsers of the backend (the coded frames are not decoded); Encrypted Media and the controls of the media elements are
  not implemented.
- **No downloads**, and the third-party cookies are not blocked.
- **Content Security Policy** covers the fetch directives, the inline checks (scripts, styles, event handlers, `style`
  attributes, `javascript:` URLs), `'unsafe-eval'`/`'wasm-unsafe-eval'` (`eval()` and the `Function` constructors), the
  navigation and document directives (`frame-src`, `form-action`, `frame-ancestors`, `base-uri`, `object-src`,
  `worker-src`), `upgrade-insecure-requests`, the inheritance of the policies and the Trusted Types directives,
  with their violation reports (`securitypolicyviolation`, `report-uri`, and `report-to` with the Reporting API), and CSP
  Embedded Enforcement (the `csp` attribute of the `iframe` element, `Sec-Required-CSP` and `Allow-CSP-From`). The
  Reporting API only generates the CSP, COEP and test reports (no deprecation, intervention, COOP or Document Policy
  reports).
- Streams cannot be transferred, and the tentative `URLPattern` methods `compareComponent()` and `generate()` are not
  implemented.
- **Experimental APIs.** The public APIs of the tentative features (proposals that no specification defines yet, e.g.
  `Element.StreamAppendHtml()`, `Element.MatchContainer()`) have the `[Experimental]` attribute:
  suppress their diagnostic (`WEBPLATEXP0002`...) to use them, see [docs/experimental-apis.md](docs/experimental-apis.md).

## Specifications

| Area | Specification | Status |
|---|---|---|
| Encoding | [WHATWG Encoding](https://encoding.spec.whatwg.org/) | Done |
| URL | [WHATWG URL](https://url.spec.whatwg.org/), [UTS #46](https://www.unicode.org/reports/tr46/) | Done |
| URL Pattern | [URL Pattern](https://urlpattern.spec.whatwg.org/) | Done (`UrlPattern`; the patterns are compiled to .NET regular expressions with the semantics of the ECMAScript `v` flag, the Unicode property escapes of the custom groups are limited to the General_Category values; the tentative `compareComponent()` and `generate()` are not implemented) |
| HTML parsing | [WHATWG HTML](https://html.spec.whatwg.org/multipage/parsing.html) | Done (the encoding declared by a `<meta>` element is found by tokenizing the head, as the browsers do and as proposed by [whatwg/html#6962](https://github.com/whatwg/html/issues/6962), rather than by the prescan of the first 1024 bytes; a `<meta>` element of the head after the first 1024 bytes changes the encoding when the parser inserts it, without the re-parse of the specification; the encoding of a document that does not declare it is detected from its content by a port of chardetng, the detector of Firefox, see [Encoding detection](docs/browsing-context.md#encoding-detection)) |
| HTML serialization | [WHATWG HTML](https://html.spec.whatwg.org/multipage/parsing.html#serialising-html-fragments) | Done (`innerHTML`, `outerHTML`, `getHTML()`; `Document.GetHtml()` and `Document.WriteHtml(TextWriter)` serialize a whole document) |
| HTML formatting | [WHATWG HTML](https://html.spec.whatwg.org/multipage/syntax.html#optional-tags) | Done (`Node.ToHtml(HtmlFormatter)`: minified output, pretty printing and custom formatters): see [docs/html-formatting.md](docs/html-formatting.md) |
| CSS formatting | [CSSOM](https://drafts.csswg.org/cssom/#serialize-a-css-rule) | Done (`CssStyleSheet.ToCss(CssFormatter)`, `CssRule.ToCss`, `CssStyleDeclaration.ToCss`: minified output, pretty printing and custom formatters): see [docs/css-formatting.md](docs/css-formatting.md) |
| DOM | [WHATWG DOM](https://dom.spec.whatwg.org/) | Done (LINQ helpers: tree walks, typed queries, `CreateElement<T>()`): see [docs/dom-helpers.md](docs/dom-helpers.md) |
| Selectors | [Selectors Level 4](https://drafts.csswg.org/selectors-4/), [CSS Syntax](https://drafts.csswg.org/css-syntax-3/) | Done (read-only selector object model: `SelectorList.Parse`, `CssStyleRule.Selectors`, specificity, matching with a parsed selector): see [docs/selectors.md](docs/selectors.md) |
| XML, DOMParser, XMLSerializer | [HTML XML syntax](https://html.spec.whatwg.org/multipage/xhtml.html), [DOM Parsing](https://w3c.github.io/DOM-Parsing/) | Done |
| DOM XPath | [DOM](https://dom.spec.whatwg.org/#xpath) over `System.Xml.XPath` | Done |
| HTML element interfaces, reflection | [WHATWG HTML](https://html.spec.whatwg.org/) | Done |
| SVG and MathML element interfaces | [SVG 2](https://svgwg.org/svg2-draft/), [SVG Paths](https://svgwg.org/specs/paths/), [Filter Effects](https://drafts.fxtf.org/filter-effects/), [Geometry](https://drafts.fxtf.org/geometry/), [MathML Core](https://w3c.github.io/mathml-core/) | Done (path data API: `getPathData`, `setPathData`; the length of the paths is computed from the `d` property, which maps the `d` attribute; `getBBox()`, the text layout, the transforms and the hit testing are computed from the geometry: see [docs/layout.md](docs/layout.md#svg-layout-svg) and [docs/computed-style.md](docs/computed-style.md#geometry)) |
| Forms | [WHATWG HTML](https://html.spec.whatwg.org/multipage/forms.html), [XHR FormData](https://xhr.spec.whatwg.org/#interface-formdata) | Done (the submission navigates the target browsing context, with the `DocumentLoader` for the network requests; no user interaction states) |
| CSS syntax, CSSOM | [CSS Syntax](https://drafts.csswg.org/css-syntax-3/), [CSSOM](https://drafts.csswg.org/cssom/), [CSS Values](https://drafts.csswg.org/css-values-4/), [CSS Color](https://drafts.csswg.org/css-color-4/) | Done (the style sheets of `<link rel=stylesheet>`, of the `@import` rules and of the `xml-stylesheet` processing instructions are fetched by the `DocumentLoader`) |
| CSS Typed OM | [CSS Typed OM](https://drafts.css-houdini.org/css-typed-om-1/) | Done (`attributeStyleMap`, `styleMap`, `computedStyleMap()`, the numeric, keyword, unparsed, transform, image and color values; the colors of the properties are not reified, like in the browsers): see [docs/css-typed-om.md](docs/css-typed-om.md) |
| CSS Parser API | [CSS Parser API](https://wicg.github.io/css-parser-api/) (WICG draft) | Done on the parser of CSS Syntax (`CSS.parseStylesheet()`, `parseRule()`, `parseDeclaration()`, `parseValue()`..., the `CSSParser*` objects; the draft's inconsistent types follow CSS Syntax; can be hidden from the scripts): see [docs/css-parser-api.md](docs/css-parser-api.md) |
| Cascade, computed style | [CSS Cascade](https://drafts.csswg.org/css-cascade-5/), [Media Queries](https://drafts.csswg.org/mediaqueries-5/), [CSSOM](https://drafts.csswg.org/cssom/#dom-window-getcomputedstyle), [CSS Mixins](https://drafts.csswg.org/css-mixins-1/) (custom functions), [CSS Color Adjustment](https://drafts.csswg.org/css-color-adjust-1/) (color schemes, forced colors mode) | Done (`getComputedStyle` with the layout-dependent resolved values from the layout, `innerText`; the `:visited` links with a history provided by the application, painted only): see [docs/computed-style.md](docs/computed-style.md) |
| Layout, CSSOM View | [CSS 2](https://drafts.csswg.org/css2/), [CSS Display](https://drafts.csswg.org/css-display-4/), [CSS Flexbox](https://drafts.csswg.org/css-flexbox-1/), [CSS Grid](https://drafts.csswg.org/css-grid-2/), [CSS Grid 3](https://drafts.csswg.org/css-grid-3/) (grid lanes), [CSS Gap Decorations](https://drafts.csswg.org/css-gaps-1/), [CSS Tables](https://drafts.csswg.org/css-tables-3/), [CSS Ruby](https://drafts.csswg.org/css-ruby-1/), [CSS Text](https://drafts.csswg.org/css-text-4/), [CSS Writing Modes](https://drafts.csswg.org/css-writing-modes-4/), [CSS Paged Media](https://drafts.csswg.org/css-page-3/), [CSS Anchor Positioning](https://drafts.csswg.org/css-anchor-position-1/), [CSSOM View](https://drafts.csswg.org/cssom-view/), [Resize Observer](https://drafts.csswg.org/resize-observer/), [Intersection Observer](https://w3c.github.io/IntersectionObserver/) | Done (internal layout engine exposed through the web APIs, the user input and the printing): see [docs/layout.md](docs/layout.md) |
| Rendering | [CSS Backgrounds](https://drafts.csswg.org/css-backgrounds-3/), [Filter Effects](https://drafts.fxtf.org/filter-effects/), [CSS Masking](https://drafts.fxtf.org/css-masking/), [SVG 2](https://svgwg.org/svg2-draft/painting.html), [HTML canvas](https://html.spec.whatwg.org/multipage/canvas.html), [Paint Timing](https://w3c.github.io/paint-timing/) | Opt-in (a backend-agnostic display list rasterized by the `Meziantou.Framework.WebPlatform.Imaging` package: screenshots, 2D canvases, image decoding): see [docs/rendering.md](docs/rendering.md) |
| Animations | [Web Animations](https://drafts.csswg.org/web-animations-1/), [CSS Animations](https://drafts.csswg.org/css-animations-1/), [CSS Transitions](https://drafts.csswg.org/css-transitions-1/) | Done (`Element.animate()`, `getAnimations()`, the CSS animations and transitions in the computed styles, their events; the animations follow the clock of the event loop; the scroll and view timelines of [Scroll-driven Animations](https://drafts.csswg.org/scroll-animations-1/) follow the scroll positions; no animation triggers), and the [SVG animations](https://svgwg.org/specs/animations/) (SMIL: `animate`, `set`, `animateMotion`, `animateTransform`, the timing model, `animVal`, the timing events): see [docs/animations.md](docs/animations.md) |
| View transitions | [CSS View Transitions 1](https://drafts.csswg.org/css-view-transitions-1/), [2](https://drafts.csswg.org/css-view-transitions-2/) | Done for the transitions of a document (`document.startViewTransition()`, `ViewTransition`, the `::view-transition-*()` pseudo-element tree with its user agent style sheet and animations, the snapshots rasterized by the rendering backend, the transition types, `view-transition-class`, the nested groups), for the scoped transitions (`element.startViewTransition()`, `element.activeViewTransition`, `view-transition-scope`, the pseudo-element tree painted above the contents of its root element, the rendering paused within it) and for the cross-document transitions (`@view-transition`, `pageswap`, `pagereveal`, revealed once the document is not render-blocked): see [docs/view-transitions.md](docs/view-transitions.md) |
| Compatibility | [WHATWG Compatibility](https://compat.spec.whatwg.org/) | Done (the `-webkit-` prefixed properties, keywords, gradients, at-rules and media features; `-webkit-gradient()`; the legacy `-webkit-box` is laid out as a flex container; `window.orientation` is not exposed, as in the desktop browsers) |
| Accessibility | [WAI-ARIA](https://w3c.github.io/aria/), [HTML-AAM](https://w3c.github.io/html-aam/), [Accessible Name](https://w3c.github.io/accname/) | Done (`Element.ComputedRole`, `Element.ComputedName`): see [docs/accessibility.md](docs/accessibility.md) |
| Media elements | [WHATWG HTML](https://html.spec.whatwg.org/multipage/media.html) | Done on top of a media backend provided by the application (`audio`, `video`, `source`, resource selection, events, playback state, `TimeRanges`, audio and video tracks, media documents, and the [Media Source Extensions](https://w3c.github.io/media-source/): `MediaSource`, `SourceBuffer`, `ManagedMediaSource`, in the windows and the dedicated workers): see [docs/browsing-context.md](docs/browsing-context.md#media-elements). Not done: the controls |
| Text tracks and WebVTT | [WHATWG HTML](https://html.spec.whatwg.org/multipage/media.html#timed-text-tracks), [WebVTT](https://w3c.github.io/webvtt/) | Done (`track`, `TextTrack`, `VTTCue`, `VTTRegion`, the WebVTT parsers, the cue events, the rendering of the cues over the videos, `::cue`, `::cue()`, `::cue-region`, `:past`, `:future`): see [docs/browsing-context.md](docs/browsing-context.md#text-tracks-and-webvtt). Not done: the user preferences for the track selection, the animation of the scrolling regions |
| HTML Sanitizer API | [WHATWG HTML](https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#html-sanitization) | Done (`setHTML`, `parseHTML`, `Sanitizer`) |
| HTML insertion methods, partial updates | [WHATWG HTML](https://html.spec.whatwg.org/multipage/scripting.html#the-template-element) (`<template for>`, [HTML insertion methods](https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#html-insertion-methods)), proposal [whatwg/html#12758](https://github.com/whatwg/html/pull/12758) | Done (`appendHTML()`, `beforeHTML()`... and the streaming methods `streamAppendHTMLUnsafe()`..., the tentative `buffer`, `sanitize` and `src` attributes of `<template for>`; no in-place `<template for="">`): see [docs/partial-updates.md](docs/partial-updates.md) |
| Trusted Types | [Trusted Types](https://w3c.github.io/trusted-types/dist/spec/) | Done (enforcement is opt-in): see [docs/trusted-types.md](docs/trusted-types.md) |
| Selection API | [Selection API](https://w3c.github.io/selection-api/) | Done (`modify()` moves the selection over the layout; the selection also changes with the caret movement keys of the editable content, not with the pointer; `toString()` returns the rendered text of the range) |
| Custom elements | [WHATWG HTML](https://html.spec.whatwg.org/multipage/custom-elements.html) (`CustomElementRegistry`, `ElementInternals`, form-associated custom elements, `:state()`) | Done (the elements are defined with .NET constructors): see [docs/custom-elements.md](docs/custom-elements.md) |
| Popover API, invoker commands | [WHATWG HTML](https://html.spec.whatwg.org/multipage/popover.html) | Done (focus, light dismiss and close requests are processed for the events dispatched by the library or the scripts: there is no user input) |
| Text directives | [URL Fragment Text Directives](https://wicg.github.io/scroll-to-text-fragment/) | Done (`#:~:text=`, `::target-text`, `document.fragmentDirective`, the user activation and cross-origin restrictions, `force-load-at-top`; the collation of the locale is not used): see [docs/browsing-context.md](docs/browsing-context.md#text-directives) |
| Fullscreen API | [Fullscreen](https://fullscreen.spec.whatwg.org/) | Done (`requestFullscreen()`, `exitFullscreen()`, `fullscreenElement`, the events of the rendering update, `:fullscreen`, the top layer and `::backdrop`, the `fullscreen` permissions policy feature, the keyboard lock of the `Escape` key; nothing is displayed: the host resizes the viewport with `BrowsingContextOptions.EnterFullscreen`): see [docs/browsing-context.md](docs/browsing-context.md#fullscreen) |
| Pointer Lock API | [Pointer Lock](https://w3c.github.io/pointerlock/) | Done (`requestPointerLock()`, `exitPointerLock()`, `pointerLockElement`, `pointerlockchange` and `pointerlockerror`, `movementX` and `movementY`, the events of the locked mouse; the host locks its pointer with `BrowsingContextOptions.RequestPointerLock`): see [docs/browsing-context.md](docs/browsing-context.md#pointer-lock) |
| Web Locks | [Web Locks API](https://w3c.github.io/web-locks/) | Done (`navigator.locks` in the windows and the workers, `request()` with the modes, `ifAvailable`, `steal` and `signal`, `query()`, the lock managers per storage key of a browsing context group, partitioned for the third-party contexts, the release when a document or a worker is discarded): see [docs/browsing-context.md](docs/browsing-context.md#web-locks) |
| Deferred fetching | [Fetch](https://fetch.spec.whatwg.org/#deferred-fetch) | Done (`fetchLater()` and `FetchLaterResult`: the requests are sent when the document is destroyed or after `activateAfter`, the quota shared by the documents of an origin and delegated to the cross-origin frames by the `deferred-fetch` and `deferred-fetch-minimal` policy-controlled features; no back/forward cache): see [docs/browsing-context.md](docs/browsing-context.md#the-fetch-algorithm) |
| Storage manager | [Storage](https://storage.spec.whatwg.org/) | Done (`navigator.storage` in the windows and the workers: `estimate()` from the usage that the storage backends report, `persisted()`, `persist()` answered by the host; no storage buckets): see [docs/browsing-context.md](docs/browsing-context.md#storage-manager) |
| Origin private file system | [File System](https://fs.spec.whatwg.org/) | Done (`getDirectory()`, the file and directory handles, the async iteration, `FileSystemWritableFileStream`, `FileSystemSyncAccessHandle` in the dedicated workers, the locks, the serialization of the handles, `move()` and `remove()`, on a delegated `FileSystemBackend`; no `FileSystemObserver`): see [docs/browsing-context.md](docs/browsing-context.md#origin-private-file-system) |
| Prioritized Task Scheduling | [Prioritized Task Scheduling](https://wicg.github.io/scheduling-apis/) | Done (`scheduler.postTask()`, `scheduler.yield()`, `TaskController`, `TaskSignal`, `TaskSignal.any()`, the priorities in the event loop, in the windows and the workers; the scheduling state is not propagated by the `await` of a promise that another task settles: it is not attached to the promise jobs yet): see [docs/browsing-context.md](docs/browsing-context.md#prioritized-task-scheduling) |
| Close watchers | [WHATWG HTML](https://html.spec.whatwg.org/multipage/interaction.html#close-requests-and-close-watchers) | Done (`CloseWatcher`, the close watcher manager shared with the dialogs, the popovers and the pickers of the `select` elements; the close request is the `Escape` key): see [docs/browsing-context.md](docs/browsing-context.md#close-watchers) |
| Editing, drag and drop | [WHATWG HTML](https://html.spec.whatwg.org/multipage/interaction.html#editing), [execCommand](https://w3c.github.io/editing/docs/execCommand/), [Drag and drop](https://html.spec.whatwg.org/multipage/dnd.html) | Partial (editing hosts, `designMode`, the `delete`, `forwardDelete`, `insertText` and `selectAll` commands, `hidden=until-found`; `DataTransfer` and the drag-and-drop processing model, driven by the host with `DragAndDropOperation`): see [docs/browsing-context.md](docs/browsing-context.md#editing) |
| Browsing contexts | [WHATWG HTML](https://html.spec.whatwg.org/multipage/document-sequences.html) (`Window`, `iframe`, `Location`, `History`) | Opt-in (nested documents, navigations, `window.open()`, focus; the resources are fetched only with a `DocumentLoader`): see [docs/browsing-context.md](docs/browsing-context.md) |
| Client Hints | [Client Hints Infrastructure](https://wicg.github.io/client-hints-infrastructure/), [User-Agent Client Hints](https://wicg.github.io/ua-client-hints/) | Done (the `Accept-CH` cache, the `Sec-CH-*` request headers with the values of `BrowsingContextOptions.ClientHints` and of the viewport, their delegation with the permissions policy and `<meta http-equiv="delegate-ch">`, the `Critical-CH` restarts and `navigator.userAgentData`; the requests of the workers have no hint, and of `Clear-Site-Data` only the clearing of the hints is implemented): see [docs/browsing-context.md](docs/browsing-context.md#client-hints) |
| Scripting | [WHATWG HTML](https://html.spec.whatwg.org/multipage/webappapis.html) (script elements, event handlers, `javascript:` URLs, timers, error reporting) | Opt-in: a JavaScript engine is plugged into the browsing contexts (`Meziantou.Framework.WebPlatform.JavaScript` for [Meziantou.Framework.JavaScriptEngine](../Meziantou.Framework.JavaScriptEngine/readme.md)), with module scripts and import maps: see [docs/scripting.md](docs/scripting.md) |
| Speculation rules | [WHATWG HTML](https://html.spec.whatwg.org/multipage/speculative-loading.html), [Prefetch](https://wicg.github.io/nav-speculation/prefetch.html), [Prerendering](https://wicg.github.io/nav-speculation/prerendering.html) | Done (`<script type="speculationrules">` and the `Speculation-Rules` header, list and document rules, eagerness with the pointer of the user, navigational prefetches used by the navigations, prerendering traversables activated by the navigations, `document.prerendering`, `prerenderingchange`, `activationStart`: see [docs/browsing-context.md](docs/browsing-context.md#speculative-loading)); the workers and the media elements of a prerendering document are not suspended |
| Worklets, CSS Painting API, CSS Layout API, Animation Worklet | [WHATWG HTML](https://html.spec.whatwg.org/multipage/worklets.html), [CSS Painting API](https://drafts.css-houdini.org/css-paint-api-1/), [CSS Layout API](https://drafts.css-houdini.org/css-layout-api-1/), [CSS Animation Worklet API](https://drafts.css-houdini.org/css-animation-worklet-1/) | Done with a script engine that supports the module scripts (`Worklet.addModule()`; `CSS.paintWorklet`, `registerPaint()`, `PaintRenderingContext2D` and the `paint()` images, which need a rendering backend; `CSS.layoutWorklet`, `registerLayout()` and the `display: layout()` boxes, without fragmentation by the layout; `CSS.animationWorklet`, `registerAnimator()` and `WorkletAnimation`: see [docs/scripting.md](docs/scripting.md#worklets), [docs/rendering.md](docs/rendering.md#paint-worklet-images), [docs/layout.md](docs/layout.md#layout-api-containers-csslayoutlayoutapi-csslayoutapi) and [docs/animations.md](docs/animations.md#animation-worklet)); the global scopes of a worklet are realms of the script engine of the document. The audio worklets are not implemented |
| Web Cryptography | [Web Cryptography API](https://w3c.github.io/webcrypto/), [Secure Curves](https://wicg.github.io/webcrypto-secure-curves/), [Modern Algorithms](https://wicg.github.io/webcrypto-modern-algos/) | Done (implemented in JavaScript in the WebIDL bindings, with managed primitives; no SLH-DSA): see [docs/scripting.md](docs/scripting.md#web-cryptography) |
| WebAssembly | [WebAssembly JavaScript Interface](https://webassembly.github.io/spec/js-api/), [Web API](https://webassembly.github.io/spec/web-api/) | Opt-in: a WebAssembly runtime is plugged into the browsing contexts (`Meziantou.Framework.WebPlatform.Wasmtime` for [Wasmtime](https://wasmtime.dev/), WebAssembly 3.0 without the threads): see [docs/scripting.md](docs/scripting.md#webassembly) |
| Streams | [WHATWG Streams](https://streams.spec.whatwg.org/), `TextEncoderStream`, `TextDecoderStream`, `Blob.stream()`, [WHATWG Compression](https://compression.spec.whatwg.org/) (`CompressionStream`, `DecompressionStream`) | Done (implemented in JavaScript in the WebIDL bindings; streams cannot be transferred): see [docs/scripting.md](docs/scripting.md#streams) |

## Building

```bash
./eng/webplatform/fetch-tests.sh
dotnet test --project tests/Meziantou.Framework.WebPlatform.Tests
dotnet test --project tests/Meziantou.Framework.WebPlatform.Imaging.Tests
dotnet test --project tests/Meziantou.Framework.WebPlatform.Wpt
```

The web-platform-tests (testharness.js tests and reftests, the reftests being rendered by the Imaging backend) run with the script engine of `Meziantou.Framework.WebPlatform.JavaScript`: see [docs/web-platform-tests.md](docs/web-platform-tests.md).

See [docs/updating-specs.md](docs/updating-specs.md) to update the specification data, [docs/fuzzing.md](docs/fuzzing.md)
to fuzz the parsers, and [docs/performance.md](docs/performance.md) for the benchmarks.
