using Meziantou.AspNetCore.Testing.Components;
using Meziantou.AspNetCore.Testing.Tests.BlazorApp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Meziantou.AspNetCore.Testing.Expectations;

namespace Meziantou.AspNetCore.Testing.Tests;

public sealed class BlazorAppTests
{
    private static async Task<WebApplication> StartApplicationAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(BlazorAppTests).Assembly.GetName().Name,
            EnvironmentName = Environments.Development,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        var app = builder.Build();
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapRazorComponents<AppRoot>().AddInteractiveServerRenderMode();
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task StaticRendering_EnhancedFormPost()
    {
        await using var app = await StartApplicationAsync();
        await using var browser = WebAppBrowser.Create(app.GetTestServer());
        var page = await browser.OpenAsync("/static");
        Assert.Equal("Static", page.Document.Title);

        await page.GetByLabel("Name").FillAsync("Ada");
        await page.GetByRole("button", "Greet").ClickAsync();

        await page.WaitForAssertionAsync(() => Assert.Equal("Hello Ada", page.Find("#message").TextContent));
        Assert.Empty(page.ScriptErrors);
    }

    [Fact]
    public async Task InteractiveServer_Counter()
    {
        await using var app = await StartApplicationAsync();
        await using var browser = WebAppBrowser.Create(app.GetTestServer());
        #region BlazorApp_InteractiveServer
        // blazor.web.js runs in the page: it starts a circuit (SignalR) with the application in memory
        var page = await browser.OpenAsync("/interactive");
        await page.WaitForInteractivityAsync();
        Assert.Equal("interactive", page.Find("#renderer").TextContent);

        // The click goes to the circuit, the render batch comes back: wait for the new content
        await page.GetByRole("button", "Click me").ClickAsync();
        await Expect(page.GetByRole("status")).ToHaveTextAsync("Current count: 1");
        #endregion

        Assert.Empty(page.ScriptErrors);
    }
}
