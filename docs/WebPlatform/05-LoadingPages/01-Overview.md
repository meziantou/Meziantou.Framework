---
title: Loading pages
description: "Turn a document into a page with a browsing context: a window, a viewport, an event loop, and the resources the page needs."
---

A parsed document is only a tree. A `BrowsingContext` turns it into a page, as a tab of a browser: it has a `Window`, a
viewport, an event loop, a session history, and, when you allow it, the network, the cookies, the scripts and the
rendering. Navigate it to a URL to load a document:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/BrowsingContextTests.cs;region=WebPlatform_NavigateToResource;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext();

// The callback returns the response, as a DocumentLoader would: here, an XML document
var document = context.Navigate("https://example.com/feed.xml", url => new DocumentResource("<feed><title>News</title></feed>")
{
    Headers = [new("Content-Type", "application/xml")],
});

Assert.Equal("application/xml", document.ContentType);
Assert.Equal("https://example.com/feed.xml", document.Url);
Assert.Equal("News", document.QuerySelector("title")!.TextContent);
```
<!-- endref -->

## Navigate

| Method | Loads |
| --- | --- |
| `Navigate(url, url => html)` | An HTML document from its text, synchronously |
| `Navigate(url, url => new DocumentResource(...))` | A document from a response: HTML, XML or text, from its text or its bytes, with its status and headers. A null resource is a network error |
| `NavigateAsync(url, ...)` | The same, and waits until the parser has finished (when a script engine makes it wait for a script) |
| `Navigate(url)` | The document returned by the [document loader](02-NetworkRequests.md), from a task of the event loop |

The URL of the document resolves its relative URLs and gives its origin, which the cookies, the storage and the security
checks use. The navigation adds an entry to the session history, and the loaded document becomes `BrowsingContext.Document`.
Before the first navigation, a browsing context has an `about:blank` document.

## Run the event loop

The `DOMContentLoaded` and `load` events, the documents of the `iframe` elements, the style sheets and the images are
loaded by the tasks of the event loop of the page. Run them:

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

- `EventLoop.RunUntilIdleAsync()` runs the tasks until nothing is left, including the resources that are still loading.
- `EventLoop.RunUntilIdle()` runs the tasks that are already queued, without waiting.
- These methods do not wait for the timers that are not due yet (`setTimeout`, `setInterval`) nor for the animation
  frames: see [Time and the event loop](07-TimeAndTheEventLoop.md).

Use a browsing context from one thread at a time, the thread that runs its event loop.

## Options

`BrowsingContextOptions` decides what the pages can do. Everything is off by default:

| Option | Allows |
| --- | --- |
| `DocumentLoader` | Network requests: documents, style sheets, images, frames, scripts, `fetch()` (see [Network requests](02-NetworkRequests.md)) |
| `CookieJar`, `LocalStorage` | Cookies and storage (see [Cookies and storage](03-CookiesAndStorage.md)) |
| `Viewport`, `UserAgent` | The size of the viewport and the emulated device (see [Viewport and devices](04-ViewportAndDevices.md)) |
| `ScriptEngine`, `WebAssemblyRuntime` | Scripts and WebAssembly (see [Scripting](../06-Scripting/01-Overview.md)) |
| `RenderingBackend` | Screenshots, printing, canvases and image decoding (see [Rendering](../07-Rendering/01-Overview.md)) |
| `TimeProvider` | The clock of the event loop (see [Time and the event loop](07-TimeAndTheEventLoop.md)) |
| `UseSystemFonts` | The fonts installed on the machine (see [Fonts](../07-Rendering/06-Fonts.md)) |

The callbacks of the options report what happens in the pages: `ConsoleMessage` and `UnhandledScriptError` for the
scripts, `BrowsingContextCreated` and `BrowsingContextDestroyed` for the frames and the popups, `DocumentActivated` for
the navigations, and `UserPromptHandler` answers `alert()`, `confirm()` and `prompt()`.

## Frames and popups

The `iframe` elements get nested browsing contexts (`BrowsingContext.Children`, `HtmlIFrameElement.ContentDocument`),
which share the event loop and the options of their page. `window.open()` creates a popup: another top-level browsing
context of the same group.

## Dispose the page

`BrowsingContext.Dispose()` discards the page: its documents and the documents of its frames are destroyed, the pending
requests are canceled, and the pending tasks and timers are dropped. No event is fired: to run the `pagehide` and
`unload` handlers first, call `TryClose()`, which closes the page as a user closes a tab.

## In this section

- [Network requests](02-NetworkRequests.md): load pages and resources from the network, from memory or from the disk,
  and filter the requests.
- [Cookies and storage](03-CookiesAndStorage.md): share and persist the cookies, the local storage and the other stores.
- [Viewport and devices](04-ViewportAndDevices.md): the size of the viewport, the device pixel ratio, mobile devices, the
  color scheme.
- [Layout and geometry](05-LayoutAndGeometry.md): the positions and sizes of the elements, hit testing and scrolling.
- [User input](06-UserInput.md): click, tap, scroll and type as a user.
- [Time and the event loop](07-TimeAndTheEventLoop.md): timers, animations and fake clocks.
