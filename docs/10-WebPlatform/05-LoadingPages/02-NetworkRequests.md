---
title: Network requests
description: Let pages load documents and resources over HTTP, from memory or from the disk, and filter the requests of untrusted pages.
---

Nothing is fetched unless the browsing context has a `DocumentLoader`. With a loader, the browsing context loads the
documents of the navigations and of the `iframe` elements, the style sheets (`<link rel=stylesheet>`, `@import`), the
images, the fonts, and, with a script engine, the scripts and the requests of `fetch()`, `XMLHttpRequest`, `WebSocket` and
`EventSource`.

## Load pages over HTTP

`DocumentLoader.Create()` returns a loader that sends HTTP requests:

<!-- ref:../../../samples/Meziantou.Framework.WebPlatform.Samples/ReadmeExamples.cs;region=WebPlatform_AllowNetworkRequests;format=md-fenced-code-block;dedent=true -->
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

- Without an argument, the loader uses a shared `HttpClient` that decompresses the responses. Pass your own
  `HttpClient` to `DocumentLoader.Create(httpClient)` to set a proxy, a timeout or a handler; it should not store cookies
  itself (`UseCookies = false`) when the browsing context has a `CookieJar`.
- The redirects are followed (up to 20), the `Referer` header is sent as a browser sends it, and the cookies of the
  `CookieJar` are sent and stored.
- Only the `http:` and `https:` URLs are requested. The `data:` and `blob:` URLs are handled by the library.

## Protect your network from untrusted pages

`DocumentLoader.Create()` requests any URL that a page navigates to or references. With an untrusted page, the requests
can reach the services of your private network (server-side request forgery). Use a loader that filters the requests:

<!-- ref:../../../samples/Meziantou.Framework.WebPlatform.Samples/ReadmeExamples.cs;region=WebPlatform_SameHostLoader;format=md-fenced-code-block;dedent=true -->
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

A null result is a network error, as is an exception thrown by the loader. `DocumentRequest` describes the request: its
`Url`, `Method`, `Headers` and `Body`, its `Destination` (a document, a style sheet, an image, a script...), the `Origin`
of the document that makes it (null when your application navigates), its `Referrer`, and the `BrowsingContext` it is
for.

## Serve pages from memory or from the disk

A loader can return anything: the files of a folder, the pages of a cache, or test pages. Return a `DocumentResource`
with the content, the status code and the headers:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/BrowsingContextTests.cs;region=WebPlatform_InMemoryLoader;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>Serves the documents and the resources from memory, by absolute URL.</summary>
private sealed class InMemoryLoader(IReadOnlyDictionary<string, (string Content, string ContentType)> files) : DocumentLoader
{
    public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
    {
        if (!files.TryGetValue(request.Url, out var file))
            return ValueTask.FromResult<DocumentResource?>(new DocumentResource("Not found") { StatusCode = 404 });

        return ValueTask.FromResult<DocumentResource?>(new DocumentResource(file.Content)
        {
            Headers = [new("Content-Type", file.ContentType)],
        });
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/BrowsingContextTests.cs;region=WebPlatform_UseInMemoryLoader;format=md-fenced-code-block;dedent=true -->
```csharp
var loader = new InMemoryLoader(new Dictionary<string, (string Content, string ContentType)>(StringComparer.Ordinal)
{
    ["https://example.com/"] = ("<link rel=stylesheet href=site.css><h1>Home</h1><iframe src=/frame.html></iframe>", "text/html"),
    ["https://example.com/site.css"] = ("h1 { color: green }", "text/css"),
    ["https://example.com/frame.html"] = ("<p>Inside the frame</p>", "text/html"),
});

using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = loader });
context.Navigate("https://example.com/");
await context.EventLoop.RunUntilIdleAsync(); // the document, its style sheet, the iframe and its document

var document = context.Document;
Assert.Equal("rgb(0, 128, 0)", context.Window.GetComputedStyle(document.QuerySelector("h1")!).Color);
var frame = (HtmlIFrameElement)document.QuerySelector("iframe")!;
Assert.Equal("Inside the frame", frame.ContentDocument!.Body!.TextContent);
```
<!-- endref -->

## Responses

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/ResponseHeadersTests.cs;region=DocumentResource_Example;format=md-fenced-code-block;dedent=true -->
```csharp
// A resource created from its decoded content
var page = new DocumentResource("<p>Hello</p>");
Assert.Equal("<p>Hello</p>", page.Content);

// A resource created from its bytes, decoded using the charset of the Content-Type header
var bytes = "console.log('Hello');"u8.ToArray();
var script = new DocumentResource(bytes) { Headers = [new("Content-Type", "text/javascript; charset=utf-8")] };
Assert.Null(script.Content);
Assert.Equal(bytes, script.ContentBytes?.ToArray());
```
<!-- endref -->

- The `Content-Type` header gives the type of the resource (`text/html` when there is none) and its encoding. The bytes
  are decoded as a browser decodes them: byte order mark, `charset`, then, for HTML, `<meta charset>` and the detection of
  the encoding from the content.
- `text/html` is parsed as HTML, the XML types (`application/xhtml+xml`, `image/svg+xml`, `application/xml`) as XML, and
  `text/plain` and the other text types as a text document.
- A document is displayed whatever its status (a 404 page is a page). A script or a style sheet whose status is not in the
  200-299 range is a network error.
- The library reads the headers that change the document: `Content-Security-Policy`, `Referrer-Policy`, `Refresh`,
  `Link` (preloads), `X-Frame-Options`, `Content-Language`... The other headers are ignored.
- `RemainingContentBytes` streams a body that arrives over time: the HTML of a navigation is parsed as its chunks arrive.

The requests are canceled when the navigation is aborted (another navigation, the removal of the `iframe`) and when the
browsing context is disposed: pass the `CancellationToken` to your I/O.
