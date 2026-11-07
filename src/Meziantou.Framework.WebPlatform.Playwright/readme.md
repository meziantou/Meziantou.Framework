# Meziantou.Framework.WebPlatform.Playwright

A [Playwright](https://playwright.dev) server whose browser is the managed browser engine of
[Meziantou.Framework.WebPlatform](../Meziantou.Framework.WebPlatform/readme.md). The Playwright clients (.NET, Node.js,
Python, Java) connect to it with `BrowserType.ConnectAsync` (`browserType.connect()` in Node.js), as to a server started with
`npx playwright run-server`, and automate its pages with the API of Playwright: navigation, locators, actions, assertions,
evaluation, screenshots, PDF, network interception, cookies and storage state, emulation, dialogs, popups and frames.

The pages run in the process of the server, without native browser: the documents are parsed, styled, laid out and painted
by Meziantou.Framework.WebPlatform, their scripts run with
[Meziantou.Framework.WebPlatform.JavaScript](../Meziantou.Framework.WebPlatform.JavaScript/readme.md), the screenshots are
drawn by [Meziantou.Framework.WebPlatform.Imaging](../Meziantou.Framework.WebPlatform.Imaging/readme.md), and the PDF are
printed by [Meziantou.Framework.WebPlatform.PdfRendering](../Meziantou.Framework.WebPlatform.PdfRendering/readme.md).

## Examples

Start a server, and automate a page with the Playwright client for .NET (Microsoft.Playwright):

<!-- ref:../../tests/Meziantou.Framework.WebPlatform.Playwright.Tests/PlaywrightServerTests.cs;region=PlaywrightServer_Connect;format=md-fenced-code-block;dedent=true -->
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

By default, nothing is fetched from the network: the pages can only use the content of `page.SetContentAsync()` and the
responses of the routes (`page.RouteAsync()`). Set `PlaywrightServerOptions.DocumentLoader` to let the pages load documents
and resources (see [the document loader](../Meziantou.Framework.WebPlatform/docs/browsing-context.md#the-document-loader) to
filter the requests):

<!-- ref:../../samples/Meziantou.Framework.WebPlatform.Playwright.Samples/ReadmeExamples.cs;region=PlaywrightServer_AllowNetworkRequests;format=md-fenced-code-block;dedent=true -->
```csharp
await using var server = new PlaywrightServer(new PlaywrightServerOptions
{
    DocumentLoader = DocumentLoader.Create(), // the pages can load any URL
});
await server.StartAsync();

using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
await using var browser = await playwright.Chromium.ConnectAsync(server.WsEndpoint.ToString());
var page = await browser.NewPageAsync();
await page.GotoAsync("https://example.com/");
await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Example Domain");
await page.ScreenshotAsync(new() { Path = "example.png", FullPage = true });
```
<!-- endref -->

`PlaywrightServer` listens on its own TCP port (`PlaywrightServerOptions.BindAddress`, `Port` and `Path`). An application
can also give it the WebSockets it accepts, e.g. in ASP.NET Core:

<!-- ref:../../samples/Meziantou.Framework.WebPlatform.Playwright.Samples/ReadmeExamples.cs;region=PlaywrightServer_AspNetCore;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
await using var server = new PlaywrightServer(new PlaywrightServerOptions { DocumentLoader = DocumentLoader.Create() });

app.UseWebSockets();
app.Map("/playwright", async context =>
{
    // The protocol changes with each minor version of Playwright: the clients must use the version of the server
    if (!context.WebSockets.IsWebSocketRequest || !PlaywrightServer.IsCompatibleClient(context.Request.Headers.UserAgent))
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

The [Meziantou.Framework.WebPlatform.Playwright.Tool](../Meziantou.Framework.WebPlatform.Playwright.Tool/readme.md) .NET tool
runs a server from the command line.

## Versions

The protocol of Playwright changes with each minor version: the clients must have the major and minor versions of
`PlaywrightServer.PlaywrightVersion` (1.64). As the Playwright server, the server rejects the other clients with an HTTP 428
response (see `PlaywrightServerOptions.AllowAnyClientVersion`).

## How it works

Each connection gets a browser (the `preLaunchedBrowser` of the protocol), reported as `chromium` by default
(`PlaywrightServerOptions.BrowserName`). Each browser context runs its pages on its own thread, with its own script engine,
cookies and local storage; each page is a top-level `BrowsingContext`, and each frame a nested browsing context.

The selectors (`getByRole()`, `getByText()`, `frameLocator()`...), the actionability checks, the assertions (`expect()`) and
the aria snapshots are evaluated by the scripts of Playwright, injected in the pages as Playwright does (see
[Scripts/README.md](Scripts/README.md)), so they behave as in the browsers. The rest of the logic of the Playwright server
(the auto-waiting and the retries, the clickable points and the hit target checks, the keyboard, the navigation lifecycle,
the network events and the routes) is ported to C#.

## Limitations

- **Engine.** The pages are rendered by Meziantou.Framework.WebPlatform: see its
  [limitations](../Meziantou.Framework.WebPlatform/readme.md#limitations) (no media decoding, no downloads...).
- **Not supported:** video recording, tracing (the traces are empty), HAR recording and replay, downloads, the workers and the
  service workers of the protocol, the clock (`page.clock`), the locator handlers (`page.addLocatorHandler()`),
  `toMatchAriaSnapshot()`, the WebSocket routes, CDP sessions, Android and Electron, the WebP screenshots, the `any-frame`
  selectors, and the files given by path to `setInputFiles()` (give their content instead, as `FilePayload`).
- **Ignored:** geolocation and permissions (there is no Geolocation API), `ignoreHTTPSErrors` and `proxy` (they are options of
  the `DocumentLoader`), `bypassCSP`, the `animations: "disabled"` option of the screenshots.
- **Network.** The requests of the pages go through `PlaywrightServerOptions.DocumentLoader`: the `data:` and `blob:` URLs are
  not reported to the clients, and the bodies of the responses are buffered.
- **Security.** The scripts of the pages run in the process (see the
  [threat model of the script engine](../Meziantou.Framework.WebPlatform/docs/scripting.md#threat-model)), and a client can make
  the server request any URL the `DocumentLoader` allows: expose the server to trusted clients only (the default
  `BindAddress` is the loopback address).
