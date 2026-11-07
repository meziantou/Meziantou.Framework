using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Meziantou.AspNetCore.Tests;

public sealed class NoCacheMiddlewareTests
{
    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        return builder;
    }

    private static string[] Directives(HttpResponseMessage response)
    {
        var value = Assert.Single(response.Headers.GetValues("Cache-Control"));
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    [Fact]
    public async Task ResponseWithoutCacheControl_AddsNoCacheHeader()
    {
        var builder = CreateBuilder();

        await using var app = builder.Build();
        app.UseMiddleware<NoCacheMiddleware>();
        app.MapGet("/no-cache", static () => "ok");

        await app.StartAsync(XunitCancellationToken);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/no-cache", XunitCancellationToken);

        var directives = Directives(response);
        Assert.Contains("no-cache", directives, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("no-store", directives, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("must-revalidate", directives, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UseNoCache_AddsNoCacheHeader()
    {
        var builder = CreateBuilder();

        await using var app = builder.Build();
        app.UseNoCache();
        app.MapGet("/no-cache", static () => "ok");

        await app.StartAsync(XunitCancellationToken);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/no-cache", XunitCancellationToken);

        Assert.Contains("no-store", Directives(response), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResponseWithCacheControl_DoesNotOverrideHeader()
    {
        var builder = CreateBuilder();

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

    [Fact]
    public async Task CacheControlSetFromDownstreamOnStarting_IsPreserved()
    {
        var builder = CreateBuilder();

        await using var app = builder.Build();
        app.UseMiddleware<NoCacheMiddleware>();

        // OnStarting callbacks run in reverse registration order, so this inner one runs BEFORE the middleware's.
        // The middleware must observe the header this sets and leave it alone. Response caching and static files
        // set headers this way, so the ordering assumption is worth pinning.
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "public,max-age=99";
                return Task.CompletedTask;
            });

            await next(context);
        });

        app.MapGet("/late", static () => "ok");

        await app.StartAsync(XunitCancellationToken);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/late", XunitCancellationToken);

        var cacheControl = Assert.Single(response.Headers.GetValues("Cache-Control"));
        Assert.Equal("public,max-age=99", cacheControl.Replace(" ", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmptyCacheControl_IsTreatedAsUnset()
    {
        var builder = CreateBuilder();

        await using var app = builder.Build();
        app.UseMiddleware<NoCacheMiddleware>();

        // Assigning "" leaves a StringValues with Count 1, so a Count-based emptiness test would ship an empty header.
        app.MapGet("/empty", static (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "";
            return "ok";
        });

        await app.StartAsync(XunitCancellationToken);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/empty", XunitCancellationToken);

        Assert.Contains("no-store", Directives(response), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResponseAlreadyStarted_DoesNotFailTheRequest()
    {
        var builder = CreateBuilder();

        await using var app = builder.Build();

        // Flush upstream so the response has started before the middleware runs. Registering an OnStarting callback
        // then throws InvalidOperationException, which must not be allowed to abort the response.
        app.Use(async (context, next) =>
        {
            await context.Response.WriteAsync("partial-", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await next(context);
        });

        app.UseMiddleware<NoCacheMiddleware>();
        app.Run(context => context.Response.WriteAsync("tail", context.RequestAborted));

        await app.StartAsync(XunitCancellationToken);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/anything", XunitCancellationToken);

        Assert.Equal("partial-tail", await response.Content.ReadAsStringAsync(XunitCancellationToken));
        Assert.False(response.Headers.Contains("Cache-Control"));
    }
}
