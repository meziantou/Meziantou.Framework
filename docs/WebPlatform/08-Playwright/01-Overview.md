---
title: Playwright
description: Automate the pages of the engine with the Playwright clients for .NET, Node.js, Python and Java, without a native browser.
---

The `Meziantou.Framework.WebPlatform.Playwright` package is a [Playwright](https://playwright.dev) server whose browser is
this engine. The Playwright clients connect to it as to a server started with `npx playwright run-server`, and automate
its pages with the API of Playwright: navigation, locators, actions, assertions, evaluation, screenshots, PDF, network
interception, cookies, emulation, dialogs, popups and frames. No browser is installed or started: the pages run in the
process of the server.

<InstallNuGetPackage Id="Meziantou.Framework.WebPlatform.Playwright" />

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Playwright.Tests/PlaywrightServerTests.cs;region=PlaywrightServer_Connect;format=md-fenced-code-block;dedent=true -->
```csharp
await using var server = new PlaywrightServer(); // no DocumentLoader: the pages use page.SetContentAsync() and the routes
await server.StartAsync();

using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
await using var browser = await playwright.Chromium.ConnectAsync(server.WsEndpoint.ToString());
var page = await browser.NewPageAsync();
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

## How it works

Each connection gets a browser, reported as `chromium` by default. Each browser context of Playwright runs its pages on
its own thread, with its own script engine, cookies and storage; each page is a [BrowsingContext](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContext), and each frame a nested
browsing context. The selectors (`GetByRole`, `GetByText`...), the actionability checks and the assertions are evaluated
by the scripts of Playwright, injected in the pages as Playwright does, so they behave as in the browsers.

The pages are rendered by this engine: the scripts run with the
[JavaScript engine](../06-Scripting/01-Overview.md), the screenshots with the [Imaging backend](../07-Rendering/02-Screenshots.md)
and the PDF with the [PDF printing](../07-Rendering/04-PdfOutput.md).

## Versions

The protocol of Playwright changes with each minor version: the clients must have the major and minor versions of
[PlaywrightServer.PlaywrightVersion](cref:Meziantou.Framework.WebPlatform.Playwright.PlaywrightServer.PlaywrightVersion). The server rejects the other clients with an HTTP 428 response, as the Playwright
server does; [PlaywrightServerOptions.AllowAnyClientVersion](cref:Meziantou.Framework.WebPlatform.Playwright.PlaywrightServerOptions.AllowAnyClientVersion) accepts them anyway.

## Not supported

- Video recording, tracing (the traces are empty), HAR recording and replay, and downloads.
- The workers and the service workers of the protocol, the clock (`page.clock`), the locator handlers,
  `toMatchAriaSnapshot()`, the WebSocket routes and the CDP sessions.
- The WebP screenshots, and the files given by path to `SetInputFiles()`: give their content instead (`FilePayload`).
- Geolocation, the permissions, `ignoreHTTPSErrors`, `proxy` and `bypassCSP` are ignored: the network options belong to the
  [DocumentLoader](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader).

See also the [limitations of the engine](../09-Limitations.md).

## In this section

- [Hosting the server](02-HostingTheServer.md): start the server in your process or in ASP.NET Core, and let the pages
  use the network.
- [Command-line tool](03-CommandLineTool.md): run the server from the command line, for the clients of any language.
