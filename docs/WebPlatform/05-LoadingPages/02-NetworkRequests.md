---
title: Network requests
description: Let pages load documents and resources over HTTP, from memory or from the disk, and filter the requests of untrusted pages.
---

Nothing is fetched unless the browsing context has a [DocumentLoader](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader). With a loader, the browsing context loads the
documents of the navigations and of the `iframe` elements, the style sheets (`<link rel=stylesheet>`, `@import`), the
images, the fonts, and, with a script engine, the scripts and the requests of `fetch()`, `XMLHttpRequest`, `WebSocket` and
`EventSource`.

## Load pages over HTTP

[DocumentLoader.Create()](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader.Create) returns a loader that sends HTTP requests:

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
  itself (`UseCookies = false`) when the browsing context has a [CookieJar](cref:Meziantou.Framework.WebPlatform.Dom.CookieJar).
- The redirects are followed (up to 20), the `Referer` header is sent as a browser sends it, and the cookies of the
  [CookieJar](cref:Meziantou.Framework.WebPlatform.Dom.CookieJar) are sent and stored.
- Only the `http:` and `https:` URLs are requested. The `data:` and `blob:` URLs are handled by the library.

## Protect your network from untrusted pages

[DocumentLoader.Create()](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader.Create) requests any URL that a page navigates to or references. With an untrusted page, the requests
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

A null result is a network error, as is an exception thrown by the loader. [DocumentRequest](cref:Meziantou.Framework.WebPlatform.Dom.DocumentRequest) describes the request: its
[Url](cref:Meziantou.Framework.WebPlatform.Dom.DocumentRequest.Url), [Method](cref:Meziantou.Framework.WebPlatform.Dom.DocumentRequest.Method), [Headers](cref:Meziantou.Framework.WebPlatform.Dom.DocumentRequest.Headers) and [Body](cref:Meziantou.Framework.WebPlatform.Dom.DocumentRequest.Body), its [Destination](cref:Meziantou.Framework.WebPlatform.Dom.DocumentRequest.Destination) (a document, a style sheet, an image, a script...), the [Origin](cref:Meziantou.Framework.WebPlatform.Dom.DocumentRequest.Origin)
of the document that makes it (null when your application navigates), its [Referrer](cref:Meziantou.Framework.WebPlatform.Dom.DocumentRequest.Referrer), and the [BrowsingContext](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContext) it is
for.

The URL keeps the fragment of the reference: `<use href="icons.svg#home">` requests `https://example.com/icons.svg#home`,
and `<iframe src="frame.html#intro">` requests `https://example.com/frame.html#intro`. An HTTP request does not send it, so
remove it when you look up a resource by URL.

## Serve pages from memory or from the disk

A loader can return anything: the files of a folder, the pages of a cache, or test pages. Return a [DocumentResource](cref:Meziantou.Framework.WebPlatform.Dom.DocumentResource)
with the content, the status code and the headers:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/BrowsingContextTests.cs;region=WebPlatform_InMemoryLoader;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>Serves the documents and the resources from memory, by absolute URL without fragment.</summary>
private sealed class InMemoryLoader(IReadOnlyDictionary<string, (string Content, string ContentType)> files) : DocumentLoader
{
    public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
    {
        // The URL keeps its fragment (e.g. "frame.html#intro", "icons.svg#home"), which is not sent over HTTP
        var url = request.Url.Split('#', 2)[0];
        if (!files.TryGetValue(url, out var file))
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
    ["https://example.com/"] = ("<link rel=stylesheet href=site.css><h1>Home</h1><iframe src=/frame.html#intro></iframe>", "text/html"),
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
- As in a browser, the type of a page is sniffed from its bytes when the `Content-Type` is not a valid MIME type, and a
  `text/plain` page whose first bytes are binary data is a binary resource (the default type of Apache is wrong for these
  files), unless the response has `X-Content-Type-Options: nosniff`. A binary resource (`application/octet-stream`) does not
  create a document: it is a download, as a response whose `Content-Disposition` is `attachment`, and the page stays.
- A document is displayed whatever its status (a 404 page is a page). A script or a style sheet whose status is not in the
  200-299 range is a network error.
- The library reads the headers that change the document: `Content-Security-Policy`, `Referrer-Policy`, `Refresh`,
  `Link`, `X-Frame-Options`, `Content-Language`, `Document-Policy`... The other headers are ignored.
- The `Link` header of a page preloads (`preload`, `modulepreload`) and prefetches (`prefetch`) its resources. As in
  Firefox, its `stylesheet` and `icon` links are requested, but the style sheet does not apply and the icon is not
  rendered. As in Chromium, the `preload` links of the `Link` headers of the style sheets, images, scripts and fonts are
  preloaded too: their URLs are relative to the resource, and the requests have no `Referer` header.
- The responses are stored in an HTTP cache shared by the browsing contexts of the loader, as in a browser: a fresh
  response is used without a request, a stale one is revalidated with `If-None-Match` and `If-Modified-Since`, and a stale
  response within its `stale-while-revalidate` window is used while a request revalidates it in the background.
- [Compression dictionaries](https://www.rfc-editor.org/rfc/rfc9842) are supported. A response of a secure URL with a
  `Use-As-Dictionary` header (fetched with CORS or from the same origin) registers its body as a dictionary for the URLs
  of its `match` pattern, while it would be fresh in the HTTP cache. The next requests of these URLs announce the best
  dictionary in their `Available-Dictionary` and `Dictionary-ID` headers (and `Accept-Encoding: dcb, dcz`), and the
  library decodes the responses compressed with it (`Content-Encoding: dcb`, Brotli, or `dcz`, Zstandard); the loader
  decodes the other content codings. A response that cannot be decoded has a body that fails. The dictionaries are
  partitioned like the HTTP cache, and the `cache` and `cookies` types of `Clear-Site-Data` clear them. A
  `<link rel="compression-dictionary">` element and a `Link` header with this relation fetch a dictionary once the page
  is loaded. The Brotli decoder of .NET has no dictionary support: the library decodes most `dcb` streams, but a stream
  whose dictionary and data do not fit in its window, or whose first literals would depend on the bytes before the
  stream, is not supported (its body fails).
- [Document Policy](https://wicg.github.io/document-policy/) is applied as in Chromium: `Document-Policy: sync-xhr=?0`
  makes the synchronous `XMLHttpRequest` requests fail, and `Document-Policy-Report-Only` reports them
  (`document-policy-violation` reports). A page requires a policy from the pages of its frames with the
  `Require-Document-Policy` header and the `policy` attribute of its `iframe` elements: the requests of the frames send it
  in the `Sec-Required-Document-Policy` header, and a frame whose `Document-Policy` header is less strict is blocked. The
  library supports the `sync-xhr` and `force-load-at-top` features. Disable it with
  [DocumentPolicyEnabled](cref:Meziantou.Framework.WebPlatform.Dom.WebPlatformFeatures.DocumentPolicyEnabled).
- The `Cross-Origin-Opener-Policy` and `Cross-Origin-Opener-Policy-Report-Only` headers of a page report to the endpoint
  of their `report-to` parameter (an endpoint of the `Reporting-Endpoints` header, or of the legacy `Report-To` header):
  the navigations that switch, or would switch, the browsing context group of a page that shares it with other windows
  (`navigation-to-response` and `navigation-from-response` reports, also for the redirects), and, as in Chromium, the
  accesses between two windows that a report-only policy would separate (`access-from-coop-page-to-openee` and the other
  `access-*` reports, with the name of the property). The `ReportingObserver`s of the page that performs the access
  receive its report as a `coop-access-violation` report.
- The scripts, the style sheets and the preloads check the hashes of their `integrity` attribute
  ([Subresource Integrity](https://w3c.github.io/webappsec-subresource-integrity/)): a resource whose body does not match
  is a network error (the element fires `error`, the script does not run, the style sheet does not apply), and so is a
  cross-origin resource with hashes fetched without CORS. A response whose `Unencoded-Digest` header (`sha-256`,
  `sha-512`) does not match its body is a network error too, except the responses that `fetch()` streams. The
  `Integrity-Policy` header of a page blocks its scripts and style sheets (`blocked-destinations=(script style)`) that have
  no hash or are not CORS requests; `Integrity-Policy-Report-Only` only reports them (`integrity-violation` reports, sent to
  the `endpoints` of the policy). The documents that inherit the policies of the page (its `srcdoc` and `about:blank`
  frames and popups) inherit both policies too.
- [Signature-based Subresource Integrity](https://wicg.github.io/signature-based-sri/) is supported: an `integrity`
  attribute (or the `integrity` option of `fetch()`) with Ed25519 public keys (`ed25519-<base64 key>`) requires a valid
  signature of one of them, sent in the `Signature` and `Signature-Input` headers (HTTP Message Signatures, with the
  `ed25519-integrity` tag) and covering the `Unencoded-Digest` header. The requests announce the keys in their
  `Accept-Signature` header. A response whose signature of this profile is not valid is a network error, whatever its
  request.
- As in the browsers, a request whose URL has both a newline or a tab and a `<` (e.g. `<img src='https://evil.example/?`
  whose quote is not closed, which captures the markup that follows it) is blocked: the dangling markup mitigation.
- The `Content-DPR` header of an image gives its density: its natural size is divided by it (`Content-DPR: 2` displays a
  100x100 image at 50x50 CSS pixels), in the `img` elements (where it replaces the density of the `srcset` candidate), the
  CSS images, the SVG `image` elements, the video posters and the image buttons.
- [RemainingContentBytes](cref:Meziantou.Framework.WebPlatform.Dom.DocumentResource.RemainingContentBytes) streams a body that arrives over time: the HTML of a navigation is parsed as its chunks arrive,
  the body of `fetch()` is a stream, and an asynchronous `XMLHttpRequest` fires its `readystatechange` and `progress`
  events as the chunks arrive (at most every 50 ms; its `responseText` has every chunk received so far). Return the
  response once its headers are received: the time the loader returns it is the `responseStart` of the resource timing
  entry, and the end of the body its `responseEnd`.
- [ReportInterimResponse](cref:Meziantou.Framework.WebPlatform.Dom.DocumentRequest.ReportInterimResponse) reports an
  interim response (`100 Continue`, `103 Early Hints`...) before the final response: its time is the
  `firstInterimResponseStart` of the resource timing entry, and the `Link` headers of the first `103` response of the
  navigation of a page (not a frame) preload their resources, which the page uses (their timing entries have the
  `early-hints` initiator type), as in Chromium. `HttpClient` does not expose the interim responses: the loader of
  `DocumentLoader.Create()` does not report them. A loader that sends a request to another loader with a new
  `DocumentRequest` forwards them with
  [InterimResponseHandler](cref:Meziantou.Framework.WebPlatform.Dom.DocumentRequest.InterimResponseHandler).

The requests are canceled when the navigation is aborted (another navigation, the removal of the `iframe`) and when the
browsing context is disposed: pass the `CancellationToken` to your I/O.
