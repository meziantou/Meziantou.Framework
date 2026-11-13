# Meziantou.Framework.WebPlatform.DevTools

A [Chrome DevTools Protocol](https://chromedevtools.github.io/devtools-protocol/) (CDP) server whose browser is the managed
browser engine of [Meziantou.Framework.WebPlatform](../Meziantou.Framework.WebPlatform/readme.md). The CDP clients see a
headless Chromium browser: Playwright connects to it with `BrowserType.ConnectOverCDPAsync` (`chromium.connectOverCDP()` in
Node.js), the other CDP clients (e.g. Puppeteer) with its WebSocket endpoint.

The pages run in the process of the server, without native browser: the documents are parsed, styled, laid out and painted
by Meziantou.Framework.WebPlatform, their scripts run with
[Meziantou.Framework.WebPlatform.JavaScript](../Meziantou.Framework.WebPlatform.JavaScript/readme.md), the screenshots are
drawn by [Meziantou.Framework.WebPlatform.Imaging](../Meziantou.Framework.WebPlatform.Imaging/readme.md), and the PDF are
printed by [Meziantou.Framework.WebPlatform.PdfRendering](../Meziantou.Framework.WebPlatform.PdfRendering/readme.md).

## Examples

Start a server, and automate a page with the Playwright client for .NET (Microsoft.Playwright):

<!-- ref:../../tests/Meziantou.Framework.WebPlatform.DevTools.Tests/DevToolsServerTests.cs;region=DevToolsServer_ConnectOverCDP;format=md-fenced-code-block;dedent=true -->
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

By default, nothing is fetched from the network: the pages can only use the content they are given (e.g.
`page.SetContentAsync()`) and the responses of the interception of the requests (`page.RouteAsync()`). Set
`DevToolsServerOptions.DocumentLoader` to let the pages load documents and resources (see
[the document loader](../Meziantou.Framework.WebPlatform/docs/browsing-context.md#the-document-loader) to filter the
requests):

<!-- ref:../../samples/Meziantou.Framework.WebPlatform.DevTools.Samples/ReadmeExamples.cs;region=DevToolsServer_AllowNetworkRequests;format=md-fenced-code-block;dedent=true -->
```csharp
await using var server = new DevToolsServer(new DevToolsServerOptions
{
    DocumentLoader = DocumentLoader.Create(), // the pages can load any URL
});
await server.StartAsync();

using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
await using var browser = await playwright.Chromium.ConnectOverCDPAsync(server.HttpEndpoint.ToString());
var context = await browser.NewContextAsync();
var page = await context.NewPageAsync();
await page.GotoAsync("https://example.com/");
await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Example Domain");
await page.ScreenshotAsync(new() { Path = "example.png", FullPage = true });
```
<!-- endref -->

`DevToolsServer` listens on its own TCP port (`DevToolsServerOptions.BindAddress` and `Port`): `HttpEndpoint` serves
`/json/version` (the clients read the WebSocket endpoint from it), and `WebSocketEndpoint` is the endpoint of the browser.
An application can also give it the WebSockets it accepts, e.g. in ASP.NET Core:

<!-- ref:../../samples/Meziantou.Framework.WebPlatform.DevTools.Samples/ReadmeExamples.cs;region=DevToolsServer_AspNetCore;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
await using var server = new DevToolsServer(new DevToolsServerOptions { DocumentLoader = DocumentLoader.Create() });

// The clients connect to the WebSocket endpoint: playwright.Chromium.ConnectOverCDPAsync("ws://host/devtools")
app.UseWebSockets();
app.Map("/devtools", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    await server.HandleConnectionAsync(webSocket, context.RequestAborted);
});

await app.RunAsync();
```
<!-- endref -->

## Versions

The clients drive the server with the commands of the protocol of Chromium: any version of Playwright can connect (the
server reports itself as Chromium `DevToolsServer.ChromiumVersion`). The features that Playwright implements in its own
process work as with Chromium: the auto-waiting, the selectors and the assertions, the tracing, the HAR recording, the clock
(`page.clock`), the locator handlers and the aria snapshots.

## How it works

Each connection gets its own browser: its default browser context and the browser contexts it creates. The browser
contexts are closed when the client disconnects. Each browser context runs its pages on its own thread, with its own script
engine, cookies and local storage; each page is a top-level `BrowsingContext`, and each frame a nested browsing context.

A page created by a client runs once the client lets it run (`Runtime.runIfWaitingForDebugger`, as Chromium does with the
targets that wait for the debugger): the user agent, the locale and the time zone that the client sets before (the
`Emulation` domain) are the ones of the browser context. They apply to its pages from its first page that runs, and they
cannot change afterwards.

## Limitations

- **Engine.** The pages are rendered by Meziantou.Framework.WebPlatform: see its
  [limitations](../Meziantou.Framework.WebPlatform/readme.md#limitations).
- **Isolated worlds.** The isolated worlds (`Page.createIsolatedWorld`, the utility world of Playwright) run in the realm of
  the main world: the scripts of the page can see the changes of their built-ins.
- **Targets.** The pages are the only targets: the frames are not separate targets (no out-of-process iframe), and the
  workers and the service workers are not reported.
- **DOM domain.** The nodes are identified by their `backendNodeId` and by the objects of the `Runtime` domain: the `nodeId`
  of the DOM agent of Chromium is not supported.
- **Not supported:** the screencast (the video recording of Playwright), the coverage (empty), the performance traces of
  Chromium (`Tracing`), the downloads, the interception of the responses (the `Response` stage of the `Fetch` domain), the
  frames of the WebSockets, the WebP screenshots, and the DevTools front end.
- **Ignored:** the geolocation and the permissions, the certificate errors and the cache settings (they are options of the
  `DocumentLoader`), the bypass of the content security policy.
- **Network.** The requests of the pages go through `DevToolsServerOptions.DocumentLoader`: the `data:` and `blob:` URLs are
  not reported to the clients, and the bodies of the responses are buffered. The requests of `APIRequestContext` of
  Playwright are sent by the Playwright driver, not by the server.
- **Security.** The scripts of the pages run in the process (see the
  [threat model of the script engine](../Meziantou.Framework.WebPlatform/docs/scripting.md#threat-model)), and a client can make
  the server request any URL the `DocumentLoader` allows: expose the server to trusted clients only (the default
  `BindAddress` is the loopback address, the path of the WebSocket endpoint is random, and the HTTP requests whose `Host`
  is not an IP address or `localhost` are rejected, as Chromium does).
