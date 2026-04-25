using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Meziantou.AspNetCore.Tests;

public sealed class NoCacheMiddlewareTests
{
    [Fact]
    public async Task ResponseWithoutCacheControl_AddsNoCacheHeader()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        await using var app = builder.Build();
        app.UseMiddleware<NoCacheMiddleware>();
        app.MapGet("/no-cache", static () => "ok");

        await app.StartAsync(XunitCancellationToken);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/no-cache", XunitCancellationToken);

        var cacheControl = Assert.Single(response.Headers.GetValues("Cache-Control"));
        var directives = cacheControl.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Contains("no-cache", directives, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("no-store", directives, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("must-revalidate", directives, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResponseWithCacheControl_DoesNotOverrideHeader()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        await using var app = builder.Build();
        app.UseMiddleware<NoCacheMiddleware>();
        app.MapGet("/cache-control", static (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "public,max-age=60";
            return "ok";
        });

        await app.StartAsync(XunitCancellationToken);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/cache-control", XunitCancellationToken);

        var cacheControl = Assert.Single(response.Headers.GetValues("Cache-Control"));
        Assert.Equal("public,max-age=60", cacheControl.Replace(" ", string.Empty, StringComparison.Ordinal));
    }
}
