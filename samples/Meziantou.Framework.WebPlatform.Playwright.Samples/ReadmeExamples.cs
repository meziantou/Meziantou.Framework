using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Playwright;

namespace Meziantou.Framework.WebPlatform.Playwright.Samples;

internal static class ReadmeExamples
{
    public static async Task AllowNetworkRequests()
    {
        #region PlaywrightServer_AllowNetworkRequests
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
        #endregion

        Console.WriteLine($"Screenshot saved to {Path.GetFullPath("example.png")}");
    }

    public static async Task HostInAspNetCore(string[] args)
    {
        #region PlaywrightServer_AspNetCore
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
        #endregion
    }
}
