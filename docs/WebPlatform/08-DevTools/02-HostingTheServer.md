---
title: Hosting the server
description: Start the DevTools protocol server in your process or in an ASP.NET Core application, let its pages use the network, and keep it safe.
---

## Start the server

[DevToolsServer](cref:Meziantou.Framework.WebPlatform.DevTools.DevToolsServer) listens on a TCP port of its own. [StartAsync](cref:Meziantou.Framework.WebPlatform.DevTools.DevToolsServer.StartAsync) starts it. The clients connect to:

- [HttpEndpoint](cref:Meziantou.Framework.WebPlatform.DevTools.DevToolsServer.HttpEndpoint) (e.g. `http://127.0.0.1:9222/`): the clients read the WebSocket endpoint from `/json/version`, as
  with Chromium. This is the address `ConnectOverCDPAsync` takes.
- [WebSocketEndpoint](cref:Meziantou.Framework.WebPlatform.DevTools.DevToolsServer.WebSocketEndpoint) (e.g. `ws://127.0.0.1:9222/devtools/browser/<id>`): the endpoint of the browser, for the
  clients that take a WebSocket address (`puppeteer.connect({ browserWSEndpoint })`, or `ConnectOverCDPAsync` too).

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

| [DevToolsServerOptions](cref:Meziantou.Framework.WebPlatform.DevTools.DevToolsServerOptions) | Default | Effect |
| --- | --- | --- |
| [BindAddress](cref:Meziantou.Framework.WebPlatform.DevTools.DevToolsServerOptions.BindAddress), [Port](cref:Meziantou.Framework.WebPlatform.DevTools.DevToolsServerOptions.Port) | loopback, a free port | Where the server listens |
| [DocumentLoader](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader) | none | The network requests of the pages (see below) |
| [ScriptEngineOptions](cref:Meziantou.Framework.WebPlatform.DevTools.DevToolsServerOptions.ScriptEngineOptions) | the default limits | The [limits of the scripts](../06-Scripting/04-RunningUntrustedPages.md) |
| [UseSystemFonts](cref:Meziantou.Framework.WebPlatform.DevTools.DevToolsServerOptions.UseSystemFonts) | false | The pages can use the fonts installed on the machine |
| [MaxConnections](cref:Meziantou.Framework.WebPlatform.DevTools.DevToolsServerOptions.MaxConnections) | none | The number of clients connected at the same time |

## Let the pages use the network

By default, nothing is fetched: the pages only use the content they are given (`SetContentAsync()`) and the responses of
the interception of the requests (`page.RouteAsync()`). Set a [DocumentLoader](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader) to let them load documents and
resources:

<!-- ref:../../../samples/Meziantou.Framework.WebPlatform.DevTools.Samples/ReadmeExamples.cs;region=DevToolsServer_AllowNetworkRequests;format=md-fenced-code-block;dedent=true -->
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

The requests of the pages go through the loader: filter them as for any page (see
[Network requests](../05-LoadingPages/02-NetworkRequests.md)). The `data:` and `blob:` URLs are not reported to the
clients, and the bodies of the responses are buffered.

## Host the server in ASP.NET Core

An application can give the server the WebSockets it accepts, instead of a port of its own. The clients then connect to
the WebSocket address of the application:

<!-- ref:../../../samples/Meziantou.Framework.WebPlatform.DevTools.Samples/ReadmeExamples.cs;region=DevToolsServer_AspNetCore;format=md-fenced-code-block;dedent=true -->
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

## Security

The scripts of the pages run in the process of the server, and a client can make the server request any URL that the
[DocumentLoader](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader) allows. Expose the server to trusted clients only:

- The default [BindAddress](cref:Meziantou.Framework.WebPlatform.DevTools.DevToolsServerOptions.BindAddress) is the loopback address.
- The path of [WebSocketEndpoint](cref:Meziantou.Framework.WebPlatform.DevTools.DevToolsServer.WebSocketEndpoint) is random: a client that does not know it cannot connect.
- As Chromium, the server rejects the HTTP requests whose `Host` header is not an IP address or `localhost`, so that a
  web page cannot reach it with a DNS rebinding attack. An application that hosts the server checks its own requests.
