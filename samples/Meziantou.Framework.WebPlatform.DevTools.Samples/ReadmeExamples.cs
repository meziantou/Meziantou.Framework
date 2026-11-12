using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Playwright;

namespace Meziantou.Framework.WebPlatform.DevTools.Samples;

internal static class ReadmeExamples
{
    public static async Task AllowNetworkRequests()
    {
        #region DevToolsServer_AllowNetworkRequests
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
        #endregion

        Console.WriteLine($"Screenshot saved to {Path.GetFullPath("example.png")}");
    }

    public static async Task HostInAspNetCore(string[] args)
    {
        #region DevToolsServer_AspNetCore
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
        #endregion
    }
}
