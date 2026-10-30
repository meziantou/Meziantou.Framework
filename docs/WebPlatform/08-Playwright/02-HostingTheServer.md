---
title: Hosting the server
description: Start the Playwright server in your process or in an ASP.NET Core application, let its pages use the network, and keep it safe.
---

## Start the server

[PlaywrightServer](cref:Meziantou.Framework.WebPlatform.Playwright.PlaywrightServer) listens on a TCP port of its own. [StartAsync](cref:Meziantou.Framework.WebPlatform.Playwright.PlaywrightServer.StartAsync) starts it and [WsEndpoint](cref:Meziantou.Framework.WebPlatform.Playwright.PlaywrightServer.WsEndpoint) is the address the clients
connect to:

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

| [PlaywrightServerOptions](cref:Meziantou.Framework.WebPlatform.Playwright.PlaywrightServerOptions) | Default | Effect |
| --- | --- | --- |
| [BindAddress](cref:Meziantou.Framework.WebPlatform.Playwright.PlaywrightServerOptions.BindAddress), [Port](cref:Meziantou.Framework.WebPlatform.Playwright.PlaywrightServerOptions.Port), [Path](cref:Meziantou.Framework.WebPlatform.Playwright.PlaywrightServerOptions.Path) | loopback, a free port, `/` | Where the server listens |
| [DocumentLoader](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader) | none | The network requests of the pages (see below) |
| [ScriptEngineOptions](cref:Meziantou.Framework.WebPlatform.Playwright.PlaywrightServerOptions.ScriptEngineOptions) | the default limits | The [limits of the scripts](../06-Scripting/04-RunningUntrustedPages.md) |
| [UseSystemFonts](cref:Meziantou.Framework.WebPlatform.Playwright.PlaywrightServerOptions.UseSystemFonts) | false | The pages can use the fonts installed on the machine |
| [BrowserName](cref:Meziantou.Framework.WebPlatform.Playwright.PlaywrightServerOptions.BrowserName) | `chromium` | The name of the browser reported to the clients |
| [AllowAnyClientVersion](cref:Meziantou.Framework.WebPlatform.Playwright.PlaywrightServerOptions.AllowAnyClientVersion) | false | Accepts the clients of another version of Playwright |
| [MaxConnections](cref:Meziantou.Framework.WebPlatform.Playwright.PlaywrightServerOptions.MaxConnections) | none | The number of clients connected at the same time |

## Let the pages use the network

By default, nothing is fetched: the pages only use the content of `SetContentAsync()` and the responses of the routes
(`page.RouteAsync()`). Set a [DocumentLoader](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader) to let them load documents and resources:

<!-- ref:../../../samples/Meziantou.Framework.WebPlatform.Playwright.Samples/ReadmeExamples.cs;region=PlaywrightServer_AllowNetworkRequests;format=md-fenced-code-block;dedent=true -->
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

The requests of the pages go through the loader: filter them as for any page (see
[Network requests](../05-LoadingPages/02-NetworkRequests.md)). The `data:` and `blob:` URLs are not reported to the
clients, and the bodies of the responses are buffered.

## Host the server in ASP.NET Core

An application can give the server the WebSockets it accepts, instead of a port of its own:

<!-- ref:../../../samples/Meziantou.Framework.WebPlatform.Playwright.Samples/ReadmeExamples.cs;region=PlaywrightServer_AspNetCore;format=md-fenced-code-block;dedent=true -->
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

[PlaywrightServer.IsCompatibleClient](cref:Meziantou.Framework.WebPlatform.Playwright.PlaywrightServer.IsCompatibleClient) checks the version of the client from its `User-Agent` header before the connection
is accepted.

## Security

The scripts of the pages run in the process of the server, and a client can make the server request any URL that the
[DocumentLoader](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader) allows. Expose the server to trusted clients only: the default [BindAddress](cref:Meziantou.Framework.WebPlatform.Playwright.PlaywrightServerOptions.BindAddress) is the loopback address.
