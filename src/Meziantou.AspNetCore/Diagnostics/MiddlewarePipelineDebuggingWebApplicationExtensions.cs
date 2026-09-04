using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meziantou.AspNetCore.Diagnostics;

/// <summary>Extension methods to map middleware pipeline debugging endpoints.</summary>
public static class MiddlewarePipelineDebuggingWebApplicationExtensions
{
    /// <summary>Gets a middleware pipeline snapshot from code without using the debug route.</summary>
    /// <remarks>
    /// The pipeline is captured while the host builds it, so call this after the host has started. Before then,
    /// <see cref="MiddlewarePipelineDebugSnapshot.IsPipelineCaptured"/> is <see langword="false"/> and the pipeline is
    /// empty — notably inside an <c>IHostedService</c> registered before the web host.
    /// </remarks>
    /// <param name="app">The web application.</param>
    /// <returns>The middleware pipeline snapshot.</returns>
    public static MiddlewarePipelineDebugSnapshot GetMiddlewarePipelineDebugSnapshot(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return GetDebugInfoProvider(app.Services).GetSnapshot();
    }

    /// <summary>
    /// Maps a JSON endpoint that returns the middleware tree and endpoint list.
    /// By default, the endpoint responds only in Development and returns 404 elsewhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The endpoint is <b>not authenticated</b>. It discloses every registered route, the middleware order, and
    /// implementation type names. When mapping it outside Development, gate it — the returned builder allows
    /// <c>.RequireAuthorization()</c>.
    /// </para>
    /// <para>
    /// The route is always registered so the return value can be chained; when
    /// <paramref name="developmentOnly"/> is <see langword="true"/> and the environment is not Development, the handler
    /// responds 404 instead of returning the snapshot.
    /// </para>
    /// </remarks>
    /// <param name="app">The web application.</param>
    /// <param name="pattern">The route pattern used for the debug endpoint.</param>
    /// <param name="developmentOnly">Indicates whether the endpoint should respond only in Development.</param>
    /// <returns>The mapped route builder.</returns>
    [RequiresUnreferencedCode("This method maps a delegate endpoint, which may use reflection and is not trim-safe.")]
    [RequiresDynamicCode("This method maps a delegate endpoint that serializes the snapshot with reflection-based JSON serialization.")]
    public static RouteHandlerBuilder MapMiddlewarePipelineDebugEndpoint(this WebApplication app, string pattern = "/_debug/pipeline", bool developmentOnly = true)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrEmpty(pattern);

        // Checked regardless of environment, so a missing AddMiddlewarePipelineDebugging() fails the same way everywhere.
        _ = GetDebugInfoProvider(app.Services);

        var blocked = developmentOnly && !app.Environment.IsDevelopment();

        return app.MapGet(pattern, (MiddlewarePipelineDebugInfoProvider debugInfoProvider) =>
        {
            return blocked ? Results.NotFound() : Results.Ok(debugInfoProvider.GetSnapshot());
        });
    }

    private static MiddlewarePipelineDebugInfoProvider GetDebugInfoProvider(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService<MiddlewarePipelineDebugInfoProvider>() is MiddlewarePipelineDebugInfoProvider debugInfoProvider)
            return debugInfoProvider;

        throw new InvalidOperationException($"Middleware pipeline debugging services are not registered. Call {nameof(MiddlewarePipelineDebuggingServiceCollectionExtensions)}.{nameof(MiddlewarePipelineDebuggingServiceCollectionExtensions.AddMiddlewarePipelineDebugging)}(...) before building the application.");
    }
}
