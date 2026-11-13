---
title: DevTools protocol
description: Automate the pages of the engine with Playwright (connectOverCDP), Puppeteer and the other Chrome DevTools Protocol clients, as a headless Chromium.
---

The `Meziantou.Framework.WebPlatform.DevTools` package is a
[Chrome DevTools Protocol](https://chromedevtools.github.io/devtools-protocol/) (CDP) server whose browser is this engine.
The CDP clients see a headless Chromium: Playwright connects to it with `ConnectOverCDPAsync`, Puppeteer with its
WebSocket endpoint. No browser is installed or started: the pages run in the process of the server.

<InstallNuGetPackage Id="Meziantou.Framework.WebPlatform.DevTools" />

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.DevTools.Tests/DevToolsServerTests.cs;region=DevToolsServer_ConnectOverCDP;format=md-fenced-code-block;dedent=true -->
```csharp
await using var server = new DevToolsServer(); // no DocumentLoader: the pages use page.SetContentAsync() and the routes
await server.StartAsync();

using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
await using var browser = await playwright.Chromium.ConnectOverCDPAsync(server.HttpEndpoint.ToString());
var context = await browser.NewContextAsync();
var page = await context.NewPageAsync();
await page.SetContentAsync("""
    <label for=name>Name</label><input id=name>
    <button onclick="document.querySelector('output').textContent = 'Hello ' + document.querySelector('input').value">Greet</button>
    <output></output>
    """);

await page.GetByLabel("Name").FillAsync("World");
await page.GetByRole(AriaRole.Button, new() { Name = "Greet" }).ClickAsync();
await Assertions.Expect(page.Locator("output")).ToHaveTextAsync("Hello World");
var screenshot = await page.ScreenshotAsync();
```
<!-- endref -->

## Versions

The clients drive the server with the commands of the protocol of Chromium, so any version of Playwright can connect: the
server reports itself as Chromium [DevToolsServer.ChromiumVersion](cref:Meziantou.Framework.WebPlatform.DevTools.DevToolsServer.ChromiumVersion). The features that Playwright implements
in its own process work as with Chromium: the auto-waiting, the selectors and the assertions, the tracing, the HAR
recording, the clock (`page.Clock`), the locator handlers and the aria snapshots.

## How it works

Each connection gets its own browser: its default browser context and the browser contexts it creates. They are closed
when the client disconnects. Each browser context runs its pages on its own thread, with its own script engine, cookies
and storage; each page is a [BrowsingContext](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContext), and each frame a nested browsing context.

A page created by a client runs once the client lets it run (`Runtime.runIfWaitingForDebugger`, as Chromium does with the
targets that wait for the debugger). The user agent, the locale and the time zone that the client sets before (the
`Emulation` domain, the `UserAgent`, `Locale` and `TimezoneId` options of `NewContextAsync`) are the ones of the browser
context: they apply to its pages from its first page that runs, and they cannot change afterwards.

The pages are rendered by this engine: the scripts run with the [JavaScript engine](../06-Scripting/01-Overview.md), the
screenshots with the [Imaging backend](../07-Rendering/02-Screenshots.md) and the PDF with the
[PDF printing](../07-Rendering/04-PdfOutput.md).

## Not supported

- The isolated worlds (`Page.createIsolatedWorld`, the utility world of Playwright) run in the realm of the main world: the
  scripts of the page can see the changes of their built-ins.
- The pages are the only targets: the frames are not separate targets, and the workers and the service workers are not
  reported.
- The nodes of the `DOM` domain are identified by their `backendNodeId` and by the objects of the `Runtime` domain: the
  `nodeId` of the DOM agent of Chromium is not supported.
- The screencast (the video recording of Playwright), the coverage (empty), the performance traces of Chromium, the
  downloads, the interception of the responses (the `Response` stage of the `Fetch` domain), the frames of the WebSockets,
  the WebP screenshots, and the DevTools front end.
- The geolocation, the permissions, the certificate errors, the cache settings and the bypass of the content security
  policy are ignored: the network options belong to the [DocumentLoader](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader).
- The requests of `APIRequestContext` (`context.APIRequest`) are sent by the Playwright driver, not by the server.
- Playwright enumerates `localStorage` with `Object.keys()` to save the storage state: as in Chromium, a key named as a
  method of `Storage` (e.g. `key`) is not saved.

See also the [limitations of the engine](../09-Limitations.md).

## In this section

- [Hosting the server](02-HostingTheServer.md): start the server in your process or in ASP.NET Core, and let the pages
  use the network.
- [Command-line tool](03-CommandLineTool.md): run the server from the command line, for the clients of any language.
