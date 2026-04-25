using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Meziantou.AspNetCore.Diagnostics;
using Meziantou.Framework.InlineSnapshotTesting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Meziantou.AspNetCore.Tests;

public sealed class MiddlewarePipelineDebuggingTests
{
    [Fact]
    public async Task MapMiddlewarePipelineDebugEndpoint_Development_ReturnsPipelineAndEndpoints()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddMiddlewarePipelineDebugging();

        await using var app = builder.Build();
        app.UseRouting();
        app.Use(static (context, next) => next(context));
        app.MapGet("/hello", static () => "hello");

        var debugEndpointBuilder = app.MapMiddlewarePipelineDebugEndpoint(developmentOnly: true);
        Assert.NotNull(debugEndpointBuilder);

        await app.StartAsync(XunitCancellationToken);
        using var client = app.GetTestClient();
        var payload = await client.GetFromJsonAsync<MiddlewarePipelineDebugSnapshot>("/_debug/pipeline", cancellationToken: XunitCancellationToken);
        var snapshot = Assert.IsType<MiddlewarePipelineDebugSnapshot>(payload);

        Assert.NotEmpty(snapshot.Pipeline.Middlewares);
        Assert.Contains(snapshot.Endpoints, endpoint => string.Equals(endpoint.RoutePattern, "/hello", StringComparison.Ordinal));
        Assert.Contains(snapshot.Endpoints, endpoint => string.Equals(endpoint.RoutePattern, "/_debug/pipeline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MapMiddlewarePipelineDebugEndpoint_NonDevelopment_DoesNotMapByDefault()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Services.AddMiddlewarePipelineDebugging();

        await using var app = builder.Build();
        app.UseRouting();
        app.Use(static (context, next) => next(context));
        app.MapGet("/hello", static () => "hello");

        var debugEndpointBuilder = app.MapMiddlewarePipelineDebugEndpoint(developmentOnly: true);
        Assert.Null(debugEndpointBuilder);

        await app.StartAsync(XunitCancellationToken);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/_debug/pipeline", XunitCancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMiddlewarePipelineDebugSnapshot_NonDevelopment_WithoutRoute_ReturnsSnapshot()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Services.AddMiddlewarePipelineDebugging();

        await using var app = builder.Build();
        app.UseRouting();
        app.Use(static (context, next) => next(context));
        app.MapGet("/hello", static () => "hello");

        var debugEndpointBuilder = app.MapMiddlewarePipelineDebugEndpoint(developmentOnly: true);
        Assert.Null(debugEndpointBuilder);

        await app.StartAsync(XunitCancellationToken);
        var snapshot = app.GetMiddlewarePipelineDebugSnapshot();
        Assert.NotEmpty(snapshot.Pipeline.Middlewares);
        Assert.Contains(snapshot.Endpoints, endpoint => string.Equals(endpoint.RoutePattern, "/hello", StringComparison.Ordinal));
        Assert.DoesNotContain(snapshot.Endpoints, endpoint => string.Equals(endpoint.RoutePattern, "/_debug/pipeline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetMiddlewarePipelineDebugSnapshot_EndpointEntriesExposeRawEndpoint()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Services.AddMiddlewarePipelineDebugging();

        await using var app = builder.Build();
        app.UseRouting();
        app.Use(static (context, next) => next(context));
        app.MapGet("/hello", static () => "hello");

        await app.StartAsync(XunitCancellationToken);
        var snapshot = app.GetMiddlewarePipelineDebugSnapshot();
        var endpoint = Assert.Single(snapshot.Endpoints, endpoint => string.Equals(endpoint.RoutePattern, "/hello", StringComparison.Ordinal));

        _ = Assert.IsType<RouteEndpoint>(endpoint.Endpoint);
    }

    [Fact]
    public async Task GetMiddlewarePipelineDebugSnapshot_ToString_ContainsPipelineAndEndpoints()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Services.AddMiddlewarePipelineDebugging();

        await using var app = builder.Build();
        app.UseRouting();
        app.Use(static (context, next) => next(context));
        app.MapGet("/hello", static () => "hello");

        await app.StartAsync(XunitCancellationToken);
        var snapshot = app.GetMiddlewarePipelineDebugSnapshot();
        var text = snapshot.ToString();

        Assert.Contains("Pipeline:", text, StringComparison.Ordinal);
        Assert.Contains("Endpoints:", text, StringComparison.Ordinal);
        Assert.Contains("/hello", text, StringComparison.Ordinal);
        Assert.Contains("::", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MiddlewarePipelineDebugSnapshot_ToString_ReturnsExpectedFullOutput()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Services.AddMiddlewarePipelineDebugging();

        await using var app = builder.Build();
        app.UseMiddleware<NoCacheMiddleware>();
        app.MapGet("/hello", static () => "hello");

        await app.StartAsync(XunitCancellationToken);
        var snapshot = app.GetMiddlewarePipelineDebugSnapshot();
        var text = snapshot.ToString();
        InlineSnapshot.WithSettings(static settings =>
        {
            settings.ScrubLinesWithReplace(static line =>
            {
                var scrubbed = Regex.Replace(line, @"Version=\d+\.\d+\.\d+\.\d+", "Version=*");
                scrubbed = Regex.Replace(scrubbed, @"PublicKeyToken=[0-9a-f]{16}", "PublicKeyToken=*");
                return scrubbed;
            });
        }).Validate(text, """
            Pipeline:
              - Microsoft.AspNetCore.Routing.EndpointRoutingMiddleware [System.Func`2[[Microsoft.AspNetCore.Http.RequestDelegate, Microsoft.AspNetCore.Http.Abstractions, Version=*, Culture=neutral, PublicKeyToken=*],[Microsoft.AspNetCore.Http.RequestDelegate, Microsoft.AspNetCore.Http.Abstractions, Version=*, Culture=neutral, PublicKeyToken=*]]::CreateMiddleware]
              - Meziantou.AspNetCore.NoCacheMiddleware [System.Func`2[[Microsoft.AspNetCore.Http.RequestDelegate, Microsoft.AspNetCore.Http.Abstractions, Version=*, Culture=neutral, PublicKeyToken=*],[Microsoft.AspNetCore.Http.RequestDelegate, Microsoft.AspNetCore.Http.Abstractions, Version=*, Culture=neutral, PublicKeyToken=*]]::CreateMiddleware]
              - Microsoft.AspNetCore.Routing.EndpointMiddleware [System.Func`2[[Microsoft.AspNetCore.Http.RequestDelegate, Microsoft.AspNetCore.Http.Abstractions, Version=*, Culture=neutral, PublicKeyToken=*],[Microsoft.AspNetCore.Http.RequestDelegate, Microsoft.AspNetCore.Http.Abstractions, Version=*, Culture=neutral, PublicKeyToken=*]]::CreateMiddleware]
            
            Endpoints:
              - [GET] /hello (Order: 0) HTTP: GET /hello [Microsoft.AspNetCore.Routing.RouteEndpoint]
            
            """);
    }

    [Fact]
    public async Task MapMiddlewarePipelineDebugEndpoint_NonDevelopment_CanBeMappedExplicitly()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Services.AddMiddlewarePipelineDebugging();

        await using var app = builder.Build();
        app.UseRouting();
        app.Use(static (context, next) => next(context));
        app.MapGet("/hello", static () => "hello");

        var debugEndpointBuilder = app.MapMiddlewarePipelineDebugEndpoint(developmentOnly: false);
        Assert.NotNull(debugEndpointBuilder);

        await app.StartAsync(XunitCancellationToken);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/_debug/pipeline", XunitCancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MapMiddlewarePipelineDebugEndpoint_WithoutServiceRegistration_Throws()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();

        await using var app = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(() => app.MapMiddlewarePipelineDebugEndpoint(developmentOnly: false));
        Assert.Contains(nameof(MiddlewarePipelineDebuggingServiceCollectionExtensions.AddMiddlewarePipelineDebugging), exception.Message, StringComparison.Ordinal);
    }
}
