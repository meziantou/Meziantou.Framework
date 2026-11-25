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
- There is no prompt for the HTTP authentication (`401` with `WWW-Authenticate`): when the URL of the request has
  credentials (`https://user:password@example.com/`), they answer the challenge, as in the browsers, also for the
  subresources (images, scripts...). Once accepted, they are used for the other requests to the same origin.

## Upgrade the navigations to HTTPS

A browser that implements HTTPS upgrades (a proposal for the Fetch standard, https://github.com/whatwg/fetch/pull/1655)
requests the `https:` URL of an `http:` link, and falls back to the `http:` URL when the `https:` request fails. Set
[HttpsUpgrades](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.HttpsUpgrades) to do the same:

<!-- ref:../../../samples/Meziantou.Framework.WebPlatform.Samples/ReadmeExamples.cs;region=WebPlatform_HttpsUpgrades;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    DocumentLoader = DocumentLoader.Create(),
    HttpsUpgrades = new HttpsUpgradeOptions
    {
        FallbackTimeout = TimeSpan.FromSeconds(3), // optional: do not wait for the timeout of the network
    },
});

context.Navigate("http://example.com/"); // requests https://example.com/, then http://example.com/ if it fails
await context.EventLoop.RunUntilIdleAsync();
```
<!-- endref -->

- Only the `GET` navigations of the top-level browsing contexts are upgraded, including the `http:` URLs of their
  redirects. The navigations of the `iframe` elements, the form submissions that post data and the subresources keep their
  URL (the `upgrade-insecure-requests` directive of a page and the mixed content checks upgrade or block the subresources).
- The navigation falls back to the `http:` URL when the loader returns no response or throws (the connection or the
  certificate is refused), when [FallbackTimeout](cref:Meziantou.Framework.WebPlatform.Dom.HttpsUpgradeOptions.FallbackTimeout)
  expires before the response, or when the `https:` URL redirects back to the `http:` URL. An HTTP error (`404`, `500`)
  is a response: it does not fall back. The host of a failed upgrade is not upgraded again for 7 days by the browsing
  contexts that share the options.
- The referrer is computed for the `https:` URL, so a `no-referrer-when-downgrade` policy sends it, and the document has
  the `https:` URL.
- The IP addresses, `localhost`, the hosts of a private network (`intranet`, `printer.local`, `*.test`, the top-level
  domains that are not in the public suffix list) and the ports other than 80 are not upgraded.
  [UpgradeNonUniqueHosts](cref:Meziantou.Framework.WebPlatform.Dom.HttpsUpgradeOptions.UpgradeNonUniqueHosts) and
  [UpgradeNonDefaultPorts](cref:Meziantou.Framework.WebPlatform.Dom.HttpsUpgradeOptions.UpgradeNonDefaultPorts) upgrade
  them too, keeping the port: use them for a test server that serves HTTPS on the port of the `http:` URL.

The option is off by default. The library loads the URLs your application gives it, often from local or test servers, and
an upgrade sends a request you did not ask for and changes the URL of the document. The feature is not part of the Fetch
standard yet, and the browsers that implement it do not all enable it in every mode.

## Upgrade the requests of the HSTS hosts

As in a browser, HTTP Strict Transport Security (HSTS, RFC 6797) is on by default. The `http:` requests to an HSTS
host are sent to the `https:` URL, and unlike the HTTPS upgrades, they do not fall back to `http:` when the `https:`
request fails. The HSTS hosts are:

- The hosts of the HSTS preload list of Chromium (<https://hstspreload.org>), such as `github.com` and the `.dev`
  domains. It is the same list as the one of the [Meziantou.Framework.Http.Hsts](https://www.nuget.org/packages/Meziantou.Framework.Http.Hsts)
  package, built into the library.
- The hosts learned from the `Strict-Transport-Security` header of the `https:` responses.
- The hosts of [IsKnownHost](cref:Meziantou.Framework.WebPlatform.Dom.HstsOptions.IsKnownHost), when you set it.

The rules:

- The navigations, the `iframe` elements, the subresources, the redirects and the `ws:` WebSockets are upgraded. The
  `http:` port 80 becomes the `https:` port 443; the other ports are kept.
- A learned host does not upgrade the requests of a third party, so that a third party cannot use HSTS to track the
  users across sites. A request that is not a top-level navigation is not upgraded when its URL is not on the same
  site as the top-level page, such as the image or the `iframe` of another site. This is the rule of Safari. Firefox
  also ignores the third-party headers, and Chrome is experimenting with upgrading the top-level navigations only. The
  preloaded hosts and the hosts of `IsKnownHost` upgrade every request.
- The header of an `http:` response is ignored, and so is a header that is not valid. When a response has several
  headers, only the first one counts. `includeSubDomains` upgrades the subdomains too, `max-age=0` removes the host, and
  a `max-age` longer than one year is reduced to one year, as in Chromium.
- The referrer is computed for the `http:` URL, so a `no-referrer-when-downgrade` policy does not send it.
- The IP addresses, `localhost` and the `*.localhost` hosts are not upgraded.
- The browsing contexts that share the options share the hosts they learn. The hosts are kept in memory only, up to
  10,000 of them.

[IsKnownHost](cref:Meziantou.Framework.WebPlatform.Dom.HstsOptions.IsKnownHost) adds hosts, such as the ones of an
intranet:

<!-- ref:../../../samples/Meziantou.Framework.WebPlatform.Samples/ReadmeExamples.cs;region=WebPlatform_HstsKnownHosts;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    DocumentLoader = DocumentLoader.Create(),
    Hsts = new HstsOptions
    {
        // The hosts of the preload list are upgraded too
        IsKnownHost = host => host is "intranet.example" || host.EndsWith(".intranet.example", StringComparison.Ordinal),
    },
});

context.Navigate("http://wiki.intranet.example/"); // requests https://wiki.intranet.example/
await context.EventLoop.RunUntilIdleAsync();
```
<!-- endref -->

The function gets the host in lowercase ASCII (an internationalized domain is in its Punycode form), without a
trailing dot. It must match the parent domains itself, and be thread-safe. To share the policies that an `HttpClient`
learns with the Meziantou.Framework.Http.Hsts package, set it to
[HstsDomainPolicyCollection.MustUpgradeRequest](cref:Meziantou.Framework.Http.HstsDomainPolicyCollection.MustUpgradeRequest).

The preload list is loaded the first time an `http:` request needs it, which costs about 120 KB of memory and a few
milliseconds. Set [UsePreloadList](cref:Meziantou.Framework.WebPlatform.Dom.HstsOptions.UsePreloadList) to `false` to
skip it, [LearnFromResponses](cref:Meziantou.Framework.WebPlatform.Dom.HstsOptions.LearnFromResponses) to `false` to
use only the lists, or [Hsts](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.Hsts) to `null` to turn
HSTS off. An application that never wants the preload list can turn it off everywhere, in the library and in the
Meziantou.Framework.Http.Hsts package, with a feature switch. The trimmer then removes the code that reads it, but the
list still ships inside the assembly (about 520 KB):

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="Meziantou.Framework.Http.Hsts.IncludePreloadList" Value="false" Trim="true" />
</ItemGroup>
```

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
- The `upgrade-insecure-requests` directive of a `Content-Security-Policy` upgrades the `http:` requests of the page to
  `https:`. As in Chromium, the requests to `localhost` and to the loopback addresses (`127.0.0.1`, `[::1]`) are not
  upgraded: a site served over `http://localhost` by a test server loads its same-origin resources (e.g. the SVG sprites
  of its `<use>` elements) from the same origin.
- The `Link` header of a page preloads (`preload`, `modulepreload`) and prefetches (`prefetch`) its resources. As in
  Firefox, its `stylesheet` and `icon` links are requested, but the style sheet does not apply and the icon is not
  rendered. The `Link` headers of the other responses (style sheets, images, scripts, fonts...) are ignored.
- The responses are stored in an HTTP cache shared by the browsing contexts of the loader, as in a browser: a fresh
  response is used without a request, a stale one is revalidated with `If-None-Match` and `If-Modified-Since`, and a stale
  response within its `stale-while-revalidate` window is used while a request revalidates it in the background. The
  partial responses (`206`) of the range requests are stored too: a fresh stored response (complete or partial) answers
  the range requests whose range it contains, and a request without `Range` header completes a stored partial response
  that starts at the first byte with a request of the rest of the representation (`Range: bytes=<next byte>-`, with an
  `If-Range` header when the partial response has a validator).
- A `421 Misdirected Request` response is retried once, as the browsers retry it on a new connection: the loader gets
  the request again. A request whose body is a `ReadableStream` is not retried (its body cannot be sent again).
- The opaque range responses (`206` or `416`) of the range requests (e.g. of a media element) are network errors for the
  no-cors requests without `Range` header: a service worker cannot give the partial response of an audio file, stored or
  put in a cache, to a script element.
- A `fetch()` request whose body is a `ReadableStream` (`duplex: "half"`) reads the stream before the request is sent to
  the loader: the response arrives once the body is sent, as with the half duplex of the browsers. The body of a request
  of another scheme than `http:` and `https:` (e.g. a `data:` URL) is not read.
- The icon of a top-level document (`<link rel=icon>`) is requested (destination `image`, a CORS request with a
  `crossorigin` attribute), as the browsers request the icon of a tab, but it is not rendered. The element fires no
  `load` or `error` event, and the request has no resource timing entry.
- [Compression dictionaries](https://www.rfc-editor.org/rfc/rfc9842) are supported. A response of a secure URL with a
  `Use-As-Dictionary` header (fetched with CORS or from the same origin) registers its body as a dictionary for the URLs
  of its `match` pattern, while it would be fresh in the HTTP cache. A header whose pattern is not valid, has regular
  expression groups, or is not same-origin with the dictionary is ignored. The next requests of these URLs announce the best
  dictionary in their `Available-Dictionary` and `Dictionary-ID` headers (and `Accept-Encoding: dcb, dcz`), and the
  library decodes the responses compressed with it (`Content-Encoding: dcb`, Brotli, or `dcz`, Zstandard); the loader
  decodes the other content codings. A response that cannot be decoded has a body that fails. The dictionaries are
  partitioned like the HTTP cache, and the `cache` and `cookies` types of `Clear-Site-Data` clear them. A dictionary is
  at most 16 MiB (as a body of the HTTP cache), and the dictionaries of a document loader take at most 64 MiB: the ones
  registered the longest ago are removed first. A
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
- As in Chromium, the page reports the use of deprecated features (`deprecation` reports, once per page and feature: a
  synchronous `XMLHttpRequest` in a window) and the requests that the library does not honor (`intervention` reports: a
  `preventDefault()` call in a `touchstart`, `touchmove`, `wheel` or `mousewheel` listener of the window, the document,
  the root element or the body that is passive because it is registered without the `passive` option). The
  `ReportingObserver`s of the page receive them, and they are sent to the `default` endpoint of the `Reporting-Endpoints`
  header. The body gives the identifier of the feature (as in Chromium, e.g.
  `XMLHttpRequestSynchronousInNonWorkerOutsideBeforeUnload`, `PreventDefaultPassive`), a message and the location of the
  script. The non-standard members that the browsers deprecate (e.g. `webkitRequestAnimationFrame()`,
  `window.fullScreen`) are not exposed, so they report nothing.
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
- The resource timing entries (`performance.getEntriesByType("resource")`) have the experimental `initiatorUrl` of
  Chromium ([PerformanceResourceTiming.InitiatorUrl](cref:Meziantou.Framework.WebPlatform.Dom.PerformanceResourceTiming.InitiatorUrl), whose .NET
  property reports the experimental diagnostic `WEBPLATEXP0026`): the style
  sheet that fetched a resource (its `@import` rules, fonts and images), the module that imports a module, else the script
  that started the fetch, followed through the timers, the tasks, the callbacks and the asynchronous operations it
  scheduled (an inline script is the page), else the page or the worker.
- The navigation timing entry of a page served by the HTTP cache has the `cache` delivery type, and its `transferSize`
  is 0 (300 when the stored response was validated). The navigation preload request of a service worker is reported to
  the performance timeline of the service worker, with the `navigation` initiator type.
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

### Network Error Logging

[Network Error Logging](https://w3c.github.io/network-error-logging/) is disabled by default: the reports describe the
requests of the user, and are sent to collectors that the sites choose. Enable it with
[BrowsingContextOptions.NetworkErrorLogging](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.NetworkErrorLogging):

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/ReportingTests.cs;region=WebPlatform_NetworkErrorLogging;format=md-fenced-code-block;dedent=true -->
```csharp
// Network Error Logging is disabled by default: give the browsing contexts a cache of the NEL policies
var networkErrorLogging = new NetworkErrorLoggingCache();
var options = new BrowsingContextOptions { NetworkErrorLogging = networkErrorLogging };
```
<!-- endref -->

- The `NEL` header of a secure response (`{"report_to": "group", "max_age": 86400}`) sets the policy of its origin, and
  `max_age: 0` removes it. A `max_age` above 10 years is clamped to 10 years. `report_to` names an endpoint of the `Reporting-Endpoints` header or a group of the legacy
  `Report-To` header of the same response.
- Each request sent to the network (the redirects and the `304` revalidations included, not the responses of the HTTP
  cache) to an origin with a policy generates a `network-error` report, sent to the endpoint of the policy: `ok`,
  `http.error` (`4xx` and `5xx`), or the type of the failure. The loader reports the failure with the
  `HttpRequestError` of the `HttpRequestException` it throws (`dns.name_not_resolved`, `tcp.failed`, `tls.failed`...).
  The `success_fraction` (0 by default) and `failure_fraction` (1 by default) members sample the reports.
- A policy with `include_subdomains` also applies to the subdomains of its origin, for their DNS failures only (the URL of
  the report is then the origin of the request).
- The reports are not visible to the `ReportingObserver`s. The cache can be shared by several browsing contexts.

## Downloads

A hyperlink with a `download` attribute, and a navigation to a response that is not displayed (its `Content-Disposition`
is `attachment`, or its MIME type is `application/octet-stream`), start a download: the page stays, and
[DownloadStarted](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.DownloadStarted) receives the response ([Download](cref:Meziantou.Framework.WebPlatform.Dom.Download)) to save it:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/BrowsingContextTests.cs;region=WebPlatform_Downloads;format=md-fenced-code-block;dedent=true -->
```csharp
var loader = new InMemoryLoader(new Dictionary<string, (string Content, string ContentType)>(StringComparer.Ordinal)
{
    ["https://example.com/"] = ("<a href=/export.csv download=report.csv>Export</a>", "text/html"),
    ["https://example.com/export.csv"] = ("id,name\n1,Ada\n", "text/csv"),
    ["https://example.com/archive.bin"] = ("binary", "application/octet-stream"),
});

var downloads = new List<string>();
using var context = new BrowsingContext(new BrowsingContextOptions
{
    DocumentLoader = loader,

    // The callback saves the response: Filename is the download attribute of the hyperlink, or null for a navigation
    DownloadStarted = (_, download) => downloads.Add($"{download.Url} {download.Filename ?? "(navigation)"}: {download.Resource.Content}"),
});
context.Navigate("https://example.com/");
await context.EventLoop.RunUntilIdleAsync();
var page = context.Document;

((HtmlElement)page.QuerySelector("a")!).Click(); // a hyperlink with a download attribute
context.Window.Location.Assign("/archive.bin");   // a response that is not displayed (application/octet-stream)
await context.EventLoop.RunUntilIdleAsync();

Assert.Same(page, context.Document); // the page stays
Assert.Equal(["https://example.com/export.csv report.csv: id,name\n1,Ada\n", "https://example.com/archive.bin (navigation): binary"], downloads);
```
<!-- endref -->

- [Filename](cref:Meziantou.Framework.WebPlatform.Dom.Download.Filename) is the value of the `download` attribute (possibly empty), or null for a navigation; the
  `Content-Disposition` header of [Resource](cref:Meziantou.Framework.WebPlatform.Dom.Download.Resource) may suggest another name.
- A body that arrives over time ([RemainingContentBytes](cref:Meziantou.Framework.WebPlatform.Dom.DocumentResource.RemainingContentBytes)) is not read by the library: enumerate it to save the
  rest of the file, or dispose its enumerator to cancel the download.
- Without a callback, the downloads are ignored: the hyperlinks with a `download` attribute fetch nothing. The documents
  sandboxed without `allow-downloads` do not download, and the listeners of the `navigate` event can cancel a download.

## WebSockets

The `WebSocket` connections of the scripts are opened by [DocumentLoader.ConnectWebSocketAsync](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader.ConnectWebSocketAsync): the loader of
[DocumentLoader.Create()](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader.Create) connects to the server, and a custom loader can filter the connections or return
an in-memory connection. The scripts can also use `WebSocketStream`, the
[proposal](https://github.com/whatwg/websockets/pull/48) that Chromium ships, whose connection is opened the same way:

- The `opened` promise gives a `ReadableStream` of the received messages (a string for a text message, a `Uint8Array` for
  a binary message) and a `WritableStream` that sends a string as a text message and a `BufferSource` as a binary message.
  A write is fulfilled once the message is sent.
- The readable stream applies backpressure: the next message is read from the network only when the page has read the
  previous one, so a server that sends faster than the page reads is slowed down.
- `close()`, `cancel()` of the readable stream and `abort()` of the writable stream close the connection; the `closed`
  promise gives the status code and the reason of the closing handshake. A connection that cannot be established or is not
  closed cleanly rejects the promises with a `WebSocketError`, and the `signal` option aborts the opening handshake.
- As a `WebSocket`, the connection is closed when its document is unloaded or its worker ends.

The .NET types ([WebSocketStream](cref:Meziantou.Framework.WebPlatform.Dom.WebSocketStream), [WebSocketError](cref:Meziantou.Framework.WebPlatform.Dom.WebSocketError)) are
[experimental](../09-Limitations.md#experimental-apis) (`WEBPLATEXP0027`). A [WebSocketError](cref:Meziantou.Framework.WebPlatform.Dom.WebSocketError) gives the status code and the reason of
the Close frame:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/WebSocketTests.cs;region=WebPlatform_WebSocketError;format=md-fenced-code-block;dedent=true -->
```csharp
// A reason without status code gives the 1000 (normal closure) status code
var error = new WebSocketError("The chat is closed", new WebSocketCloseInfo { Reason = "maintenance" });
Assert.Equal("WebSocketError", error.Name);
Assert.Equal((ushort)1000, error.CloseCode);
Assert.Equal("maintenance", error.Reason);

// The status code must be 1000 or between 3000 and 4999, and the reason at most 123 bytes in UTF-8
var invalid = Assert.Throws<DomException>(() => new WebSocketError("", new WebSocketCloseInfo { CloseCode = 1001 }));
Assert.Equal("InvalidAccessError", invalid.Name);
```
<!-- endref -->

## WebTransport

The `WebTransport` sessions of the scripts of the secure contexts are established by
[DocumentLoader.ConnectWebTransportAsync](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader.ConnectWebTransportAsync), after the `connect-src` directive and the mixed content checks:

- The loader of [DocumentLoader.Create()](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader.Create) opens the session over HTTP/3 with `Meziantou.Framework.WebTransport`, which needs
  MsQuic (see [QUIC and WebTransport](../../Quic/01-Introduction.md)). Each session has its own connection.
- A custom loader gets a [WebTransportConnectionRequest](cref:Meziantou.Framework.WebPlatform.Dom.WebTransportConnectionRequest): the URL, the headers (the `origin` header
  first), the protocols, the SHA-256 hashes of the `serverCertificateHashes` option and the congestion control. It returns
  a [WebTransportConnection](cref:Meziantou.Framework.WebPlatform.Dom.WebTransportConnection) (an in-memory session, for instance), or null to reject the session.
- The sessions of a document are closed when the document is unloaded, and the sessions of a worker when the worker ends.
- `exportKeyingMaterial()` is not supported.
