using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.AspNetCore.ServiceDefaults.Tests;

public sealed class ServiceDefaultTests
{
    [Fact]
    public async Task HasDefaultHealthChecks()
    {
        var builder = WebApplication.CreateBuilder();
        builder.UseMeziantouConventions();
        builder.WebHost.UseKestrel(conf => conf.Listen(IPAddress.Loopback, port: 0));

        await using var app = builder.Build();
        app.MapMeziantouDefaultEndpoints();
        app.MapGet("/", () => TypedResults.Ok(new { Sample = Sample.Value1 }));
        var t = app.RunAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
        using var httpClient = new HttpClient() { BaseAddress = new Uri(address) };
        Assert.Equal("Healthy", await httpClient.GetStringAsync("health", XunitCancellationToken));
        Assert.Equal("Healthy", await httpClient.GetStringAsync("alive", XunitCancellationToken));

        Assert.Equal("""{"sample":"value1"}""", await httpClient.GetStringAsync("/"));
    }

    [Fact]
    public async Task CanCallTryUseMeziantouConventionsMultipleTimes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.UseMeziantouConventions();
        builder.TryUseMeziantouConventions();
        builder.WebHost.UseKestrel(conf => conf.Listen(IPAddress.Loopback, port: 0));

        await using var app = builder.Build();
        app.MapMeziantouDefaultEndpoints();
        app.MapGet("/", () => TypedResults.Ok(new { Sample = Sample.Value1 }));
        var t = app.RunAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
        using var httpClient = new HttpClient() { BaseAddress = new Uri(address) };
        Assert.Equal("Healthy", await httpClient.GetStringAsync("health", XunitCancellationToken));
        Assert.Equal("Healthy", await httpClient.GetStringAsync("alive", XunitCancellationToken));

        Assert.Equal("""{"sample":"value1"}""", await httpClient.GetStringAsync("/"));
    }

    [Fact]
    public async Task ValidateContainerOnStartup_MissingServices()
    {
        var builder = WebApplication.CreateBuilder();
        builder.UseMeziantouConventions();
        builder.Services.AddTransient<FooService>();

        Assert.Throws<AggregateException>(() => builder.Build());
    }

    [Fact]
    public async Task ValidateContainerOnStartup_InvalidLifeCycle()
    {
        var builder = WebApplication.CreateBuilder();
        builder.UseMeziantouConventions();
        builder.Services.AddSingleton<FooService>();
        builder.Services.AddScoped<BarService>();

        Assert.Throws<AggregateException>(() => builder.Build());
    }

    [Fact]
    public async Task CachingMiddleware_AddsNoCacheHeadersWhenNotSet()
    {
        var builder = WebApplication.CreateBuilder();
        builder.UseMeziantouConventions();
        builder.WebHost.UseKestrel(conf => conf.Listen(IPAddress.Loopback, port: 0));

        await using var app = builder.Build();
        app.MapMeziantouDefaultEndpoints();
        app.MapGet("/test", () => TypedResults.Ok(new { Value = "test" }));
        var t = app.RunAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
        using var httpClient = new HttpClient() { BaseAddress = new Uri(address) };
        using var response = await httpClient.GetAsync("/test", XunitCancellationToken);

        Assert.True(response.Headers.CacheControl is not null);
        Assert.True(response.Headers.CacheControl.NoCache);
        Assert.True(response.Headers.CacheControl.NoStore);
        Assert.True(response.Headers.CacheControl.MustRevalidate);
    }

    [Fact]
    public async Task CachingMiddleware_DoesNotOverrideExplicitCacheHeaders()
    {
        var builder = WebApplication.CreateBuilder();
        builder.UseMeziantouConventions();
        builder.WebHost.UseKestrel(conf => conf.Listen(IPAddress.Loopback, port: 0));

        await using var app = builder.Build();
        app.MapMeziantouDefaultEndpoints();
        app.MapGet("/test", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "public, max-age=3600";
            return TypedResults.Ok(new { Value = "test" });
        });
        var t = app.RunAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
        using var httpClient = new HttpClient() { BaseAddress = new Uri(address) };
        using var response = await httpClient.GetAsync("/test", XunitCancellationToken);

        Assert.True(response.Headers.CacheControl is not null);
        Assert.True(response.Headers.CacheControl.Public);
        Assert.Equal(3600, response.Headers.CacheControl.MaxAge?.TotalSeconds);
        Assert.False(response.Headers.CacheControl.NoCache);
    }

    [Fact]
    public async Task CachingMiddleware_CanBeDisabled()
    {
        var builder = WebApplication.CreateBuilder();
        builder.UseMeziantouConventions(options =>
        {
            options.Caching.SetNoCacheWhenMissingCacheHeaders = false;
        });
        builder.WebHost.UseKestrel(conf => conf.Listen(IPAddress.Loopback, port: 0));

        await using var app = builder.Build();
        app.MapMeziantouDefaultEndpoints();
        app.MapGet("/test", () => TypedResults.Ok(new { Value = "test" }));
        var t = app.RunAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
        using var httpClient = new HttpClient() { BaseAddress = new Uri(address) };
        using var response = await httpClient.GetAsync("/test", XunitCancellationToken);

        Assert.True(response.Headers.CacheControl is null || response.Headers.CacheControl.NoCache == false);
    }

    private enum Sample
    {
        Value1,
        Value2,
    }

    private sealed class FooService(BarService bar) { public BarService Bar { get; } = bar; }
    private sealed class BarService;
}