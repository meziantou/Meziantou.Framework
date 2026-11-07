using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meziantou.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Meziantou.Framework.InlineSnapshotTesting;

namespace Meziantou.AspNetCore.Tests;

public sealed class MiddlewarePipelineDebuggingTests
{
    private static WebApplicationBuilder CreateBuilder(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Services.AddMiddlewarePipelineDebugging();
        return builder;
    }

    private static void AddHostPipeline(WebApplicationBuilder builder, Action<IApplicationBuilder> configure)
        => builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter>(new DelegateStartupFilter(configure, configureAfter: null)));

    private static void AddHostPipelineAfter(WebApplicationBuilder builder, Action<IApplicationBuilder> configure)
        => builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter>(new DelegateStartupFilter(configure: null, configureAfter: configure)));

    [Fact]
    public async Task MapMiddlewarePipelineDebugEndpoint_Development_ReturnsPipelineAndEndpoints()
    {
        var builder = CreateBuilder(Environments.Development);

        await using var app = builder.Build();
        app.UseRouting();
        app.MapGet("/hello", static () => "hello");
        _ = app.MapMiddlewarePipelineDebugEndpoint();

        await app.StartAsync(XunitCancellationToken);
        using var client = app.GetTestClient();
        var snapshot = await client.GetFromJsonAsync<MiddlewarePipelineDebugSnapshot>("/_debug/pipeline", cancellationToken: XunitCancellationToken);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.IsPipelineCaptured);
        Assert.NotEmpty(snapshot.Pipeline.Middlewares);
        Assert.Contains(snapshot.Endpoints, endpoint => string.Equals(endpoint.RoutePattern, "/hello", StringComparison.Ordinal));
        Assert.Contains(snapshot.Endpoints, endpoint => string.Equals(endpoint.RoutePattern, "/_debug/pipeline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MapMiddlewarePipelineDebugEndpoint_NonDevelopment_RespondsNotFoundByDefault()
    {
        var builder = CreateBuilder(Environments.Production);

        await using var app = builder.Build();
        app.UseRouting();
        app.MapGet("/hello", static () => "hello");

        // The route is always registered so the result can be chained; the handler is what refuses outside Development.
        var debugEndpointBuilder = app.MapMiddlewarePipelineDebugEndpoint();
        Assert.NotNull(debugEndpointBuilder);

        await app.StartAsync(XunitCancellationToken);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/_debug/pipeline", XunitCancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MapMiddlewarePipelineDebugEndpoint_NonDevelopment_CanBeMappedExplicitly()
    {
        var builder = CreateBuilder(Environments.Production);

        await using var app = builder.Build();
        app.UseRouting();
        app.MapGet("/hello", static () => "hello");
        _ = app.MapMiddlewarePipelineDebugEndpoint(developmentOnly: false);

        await app.StartAsync(XunitCancellationToken);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/_debug/pipeline", XunitCancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MapMiddlewarePipelineDebugEndpoint_ReturnedBuilderIsChainable()
    {
        var builder = CreateBuilder(Environments.Production);

        await using var app = builder.Build();
        app.MapGet("/hello", static () => "hello");

        // The whole point of the non-nullable return: hardening the endpoint must not depend on the environment.
        _ = app.MapMiddlewarePipelineDebugEndpoint().WithMetadata(new TestMarkerMetadata());

        await app.StartAsync(XunitCancellationToken);
        var snapshot = app.GetMiddlewarePipelineDebugSnapshot();
        var debugEndpoint = Assert.Single(snapshot.Endpoints, endpoint => string.Equals(endpoint.RoutePattern, "/_debug/pipeline", StringComparison.Ordinal));
        Assert.NotNull(debugEndpoint.Endpoint);
        Assert.NotNull(debugEndpoint.Endpoint.Metadata.GetMetadata<TestMarkerMetadata>());
    }

    [Fact]
    public async Task MapMiddlewarePipelineDebugEndpoint_WithoutServiceRegistration_Throws()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();

        await using var app = builder.Build();

        // Not environment-dependent: a missing registration must fail the same way in every environment.
        var exception = Assert.Throws<InvalidOperationException>(() => app.MapMiddlewarePipelineDebugEndpoint());
        Assert.Contains(nameof(MiddlewarePipelineDebuggingServiceCollectionExtensions.AddMiddlewarePipelineDebugging), exception.Message);
    }

    [Fact]
    public async Task GetMiddlewarePipelineDebugSnapshot_HostPipeline_CapturesEachRegisteredMiddleware()
    {
        var builder = CreateBuilder(Environments.Production);
        AddHostPipeline(builder, static app =>
        {
            app.UseMiddleware<AlphaMiddleware>();
            app.UseMiddleware<BravoMiddleware>();
        });

        await using var app = builder.Build();
        app.MapGet("/hello", static () => "hello");

        await app.StartAsync(XunitCancellationToken);
        var snapshot = app.GetMiddlewarePipelineDebugSnapshot();
        var names = snapshot.Pipeline.Middlewares.Select(static middleware => middleware.Name).ToArray();

        Assert.Contains(typeof(AlphaMiddleware).FullName!, names, StringComparer.Ordinal);
        Assert.Contains(typeof(BravoMiddleware).FullName!, names, StringComparer.Ordinal);
        Assert.True(
            Array.IndexOf(names, typeof(AlphaMiddleware).FullName!) < Array.IndexOf(names, typeof(BravoMiddleware).FullName!),
            $"Middleware must be reported in registration order. Actual: {string.Join(", ", names)}");
    }

    [Fact]
    public async Task GetMiddlewarePipelineDebugSnapshot_HostPipeline_ResolvesFrameworkMiddlewareNames()
    {
        var builder = CreateBuilder(Environments.Production);
        builder.Services.AddCors();
        builder.Services.AddResponseCompression();
        builder.Services.AddAuthentication();
        AddHostPipeline(builder, static app =>
        {
            app.UseExceptionHandler("/error");
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseResponseCompression();
            app.UseRouting();
            app.UseCors();
            app.UseAuthentication();
        });

        await using var app = builder.Build();
        app.MapGet("/error", static () => "error");

        await app.StartAsync(XunitCancellationToken);
        var names = app.GetMiddlewarePipelineDebugSnapshot().Pipeline.Middlewares.Select(static middleware => middleware.Name).ToArray();

        // Framework middleware is what name resolution has to get right, and these types are all internal to ASP.NET:
        // they can only be found by inspecting the registration delegate, so this covers the reflective walk against
        // real middleware rather than only the test's own. ExceptionHandlerMiddleware is the one exception -- it is one
        // of only three places in the shared framework that publish "analysis.NextMiddlewareName" -- so it also covers
        // that branch, which nothing else exercises.
        // A name that failed to resolve would show up as a compiler-generated "...Extensions+<>c__DisplayClass..."
        // fallback, so asserting the exact type names is what makes this test meaningful.
        string[] expected =
        [
            "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware",
            "Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware",
            "Microsoft.AspNetCore.StaticFiles.StaticFileMiddleware",
            "Microsoft.AspNetCore.ResponseCompression.ResponseCompressionMiddleware",
            "Microsoft.AspNetCore.Routing.EndpointRoutingMiddleware",
            "Microsoft.AspNetCore.Cors.Infrastructure.CorsMiddleware",
            "Microsoft.AspNetCore.Authentication.AuthenticationMiddleware",
            "Microsoft.AspNetCore.Routing.EndpointMiddleware",
        ];

        var unresolved = expected.Except(names, StringComparer.Ordinal).ToArray();
        Assert.Empty(unresolved, $"Framework middleware whose name did not resolve: {string.Join(", ", unresolved)}.{Environment.NewLine}Captured: {string.Join(", ", names)}");
    }

    [Fact]
    public async Task GetMiddlewarePipelineDebugSnapshot_HostPipeline_CapturesBranches()
    {
        var builder = CreateBuilder(Environments.Production);
        AddHostPipeline(builder, static app =>
        {
            app.Map("/mapped", static branch => branch.UseMiddleware<AlphaMiddleware>());
            app.UseWhen(static context => context.Request.Path == "/when", static branch => branch.UseMiddleware<BravoMiddleware>());
        });

        await using var app = builder.Build();
        app.MapGet("/hello", static () => "hello");

        await app.StartAsync(XunitCancellationToken);
        var snapshot = app.GetMiddlewarePipelineDebugSnapshot();

        var branches = snapshot.Pipeline.Middlewares.Where(static middleware => middleware.Branches.Count > 0).ToArray();
        Assert.HasCount(2, branches);

        // Each branch holds exactly the middleware registered in it, and no build-time rejoin delegate.
        Assert.Collection(
            branches.SelectMany(static middleware => middleware.Branches).Select(static branch => Assert.Single(branch.Middlewares).Name),
            name => Assert.Equal(typeof(AlphaMiddleware).FullName!, name, StringComparer.Ordinal),
            name => Assert.Equal(typeof(BravoMiddleware).FullName!, name, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetMiddlewarePipelineDebugSnapshot_BranchWithoutOwningMiddleware_IsReportedAsUnattached()
    {
        var builder = CreateBuilder(Environments.Production);

        // Registered after the rest of the pipeline: a branch built by hand with no following Use() on the same
        // builder has no middleware to attach to. (A branch followed by an unrelated Use() is attributed to it —
        // positional attribution is all IApplicationBuilder.New() offers.)
        AddHostPipelineAfter(builder, static app =>
        {
            var orphan = app.New();
            orphan.UseMiddleware<BravoMiddleware>();
            _ = orphan.Build();
        });

        await using var app = builder.Build();
        await app.StartAsync(XunitCancellationToken);
        var snapshot = app.GetMiddlewarePipelineDebugSnapshot();

        var unattached = Assert.Single(snapshot.Pipeline.Middlewares, static middleware => middleware.Name == "(unattached branch)");
        Assert.Null(unattached.DelegateType);
        Assert.Null(unattached.DelegateMethod);
        var branch = Assert.Single(unattached.Branches);
        Assert.Equal(typeof(BravoMiddleware).FullName!, Assert.Single(branch.Middlewares).Name, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetMiddlewarePipelineDebugSnapshot_MinimalApiPipeline_IsReportedAsTheBridgeNotAsUserMiddleware()
    {
        // WebApplication hands the host pipeline one component standing for every middleware registered on it, so those
        // middlewares cannot be captured. The entry must say so rather than borrow the name of one of them.
        var builder = CreateBuilder(Environments.Production);

        await using var app = builder.Build();
        app.UseMiddleware<AlphaMiddleware>();
        app.UseMiddleware<BravoMiddleware>();
        app.MapGet("/hello", static () => "hello");

        await app.StartAsync(XunitCancellationToken);
        var snapshot = app.GetMiddlewarePipelineDebugSnapshot();
        var names = snapshot.Pipeline.Middlewares.Select(static middleware => middleware.Name).ToArray();

        Assert.DoesNotContain(typeof(AlphaMiddleware).FullName!, names, StringComparer.Ordinal);
        Assert.DoesNotContain(typeof(BravoMiddleware).FullName!, names, StringComparer.Ordinal);
        Assert.Contains(names, static name => name.Contains("WireSourcePipeline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetMiddlewarePipelineDebugSnapshot_BeforeStart_ReportsNotCaptured()
    {
        var builder = CreateBuilder(Environments.Production);

        await using var app = builder.Build();
        app.MapGet("/hello", static () => "hello");

        var snapshot = app.GetMiddlewarePipelineDebugSnapshot();
        Assert.False(snapshot.IsPipelineCaptured);
        Assert.Empty(snapshot.Pipeline.Middlewares);
        Assert.Contains("not captured yet", snapshot.ToString());
    }

    [Fact]
    public async Task GetMiddlewarePipelineDebugSnapshot_MiddlewareHoldingAThrowingCollection_DoesNotFailStartup()
    {
        var builder = CreateBuilder(Environments.Production);
        AddHostPipeline(builder, static app => app.UseThrowingCollectionMiddleware());

        await using var app = builder.Build();

        // Name resolution is best effort; a hostile object graph must degrade to a fallback name, not abort startup.
        await app.StartAsync(XunitCancellationToken);
        var snapshot = app.GetMiddlewarePipelineDebugSnapshot();

        Assert.True(snapshot.IsPipelineCaptured);
        Assert.Contains(snapshot.Pipeline.Middlewares, static middleware => middleware.Name.Contains("ThrowingCollection", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Snapshot_JsonRoundTrip_LeavesEndpointNull()
    {
        var builder = CreateBuilder(Environments.Production);

        await using var app = builder.Build();
        app.MapGet("/hello", static () => "hello");

        await app.StartAsync(XunitCancellationToken);
        var snapshot = app.GetMiddlewarePipelineDebugSnapshot();
        Assert.NotNull(Assert.Single(snapshot.Endpoints).Endpoint);

        var roundTripped = JsonSerializer.Deserialize<MiddlewarePipelineDebugSnapshot>(JsonSerializer.Serialize(snapshot));

        // Endpoint is [JsonIgnore]d, so it is null after a round trip. The property type says so.
        Assert.NotNull(roundTripped);
        Assert.Null(Assert.Single(roundTripped.Endpoints).Endpoint);
    }

    [Fact]
    public void ToString_RendersPipelineTreeAndEndpoints()
    {
        // Rendered from a hand-built snapshot so the expected text does not depend on ASP.NET internals.
        var snapshot = new MiddlewarePipelineDebugSnapshot
        {
            IsPipelineCaptured = true,
            Pipeline = new MiddlewarePipelineDebugPipeline
            {
                Middlewares =
                [
                    new MiddlewarePipelineDebugMiddleware
                    {
                        Name = "Contoso.FirstMiddleware",
                        DelegateType = "Contoso.Registration",
                        DelegateMethod = "CreateMiddleware",
                        Branches = [],
                    },
                    new MiddlewarePipelineDebugMiddleware
                    {
                        Name = "Contoso.BranchingMiddleware",
                        DelegateType = "Contoso.Registration",
                        DelegateMethod = "CreateMiddleware",
                        Branches =
                        [
                            new MiddlewarePipelineDebugPipeline
                            {
                                Middlewares =
                                [
                                    new MiddlewarePipelineDebugMiddleware
                                    {
                                        Name = "Contoso.InnerMiddleware",
                                        DelegateType = "Contoso.Registration",
                                        DelegateMethod = "CreateMiddleware",
                                        Branches = [],
                                    },
                                ],
                            },
                            new MiddlewarePipelineDebugPipeline { Middlewares = [] },
                        ],
                    },
                    new MiddlewarePipelineDebugMiddleware
                    {
                        Name = "(unattached branch)",
                        Branches = [new MiddlewarePipelineDebugPipeline { Middlewares = [] }],
                    },
                ],
            },
            Endpoints =
            [
                new MiddlewarePipelineDebugEndpoint
                {
                    EndpointType = "Microsoft.AspNetCore.Routing.RouteEndpoint",
                    DisplayName = "HTTP: GET /hello",
                    RoutePattern = "/hello",
                    Order = 0,
                    HttpMethods = ["GET"],
                },
                new MiddlewarePipelineDebugEndpoint
                {
                    EndpointType = "Microsoft.AspNetCore.Routing.RouteEndpoint",
                    HttpMethods = [],
                },
            ],
        };

        InlineSnapshot.Validate(snapshot.ToString(), """
            Pipeline:
              - Contoso.FirstMiddleware [Contoso.Registration::CreateMiddleware]
              - Contoso.BranchingMiddleware [Contoso.Registration::CreateMiddleware]
                Branch 1:
                  - Contoso.InnerMiddleware [Contoso.Registration::CreateMiddleware]
                Branch 2:
                  (none)
              - (unattached branch)
                Branch 1:
                  (none)

            Endpoints:
              - [GET] /hello (Order: 0) HTTP: GET /hello [Microsoft.AspNetCore.Routing.RouteEndpoint]
              - [*] (no route pattern) (Order: -) (no display name) [Microsoft.AspNetCore.Routing.RouteEndpoint]

            """);
    }

    [Fact]
    public void ToString_NotCaptured_SaysSo()
    {
        var snapshot = new MiddlewarePipelineDebugSnapshot
        {
            IsPipelineCaptured = false,
            Pipeline = new MiddlewarePipelineDebugPipeline { Middlewares = [] },
            Endpoints = [],
        };

        InlineSnapshot.Validate(snapshot.ToString(), """
            Pipeline:
              (not captured yet: the pipeline is captured when the host builds it)

            Endpoints:
              (none)

            """);
    }

    private sealed class TestMarkerMetadata;

    private sealed class DelegateStartupFilter(Action<IApplicationBuilder>? configure, Action<IApplicationBuilder>? configureAfter) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            configure?.Invoke(app);
            next(app);
            configureAfter?.Invoke(app);
        };
    }

    internal sealed class AlphaMiddleware(RequestDelegate next)
    {
        public Task InvokeAsync(HttpContext context) => next(context);
    }

    internal sealed class BravoMiddleware(RequestDelegate next)
    {
        public Task InvokeAsync(HttpContext context) => next(context);
    }
}
