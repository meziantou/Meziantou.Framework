using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meziantou.AspNetCore.Diagnostics;

/// <summary>Extension methods to map middleware pipeline debugging endpoints.</summary>
public static class MiddlewarePipelineDebuggingWebApplicationExtensions
{
    /// <summary>Gets a middleware pipeline snapshot from code without using the debug route.</summary>
    /// <param name="app">The web application.</param>
    /// <returns>The middleware pipeline snapshot.</returns>
    public static MiddlewarePipelineDebugSnapshot GetMiddlewarePipelineDebugSnapshot(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return GetDebugInfoProvider(app.Services).GetSnapshot();
    }

    /// <summary>
    /// Maps a JSON endpoint that returns the middleware tree and endpoint list.
    /// By default, the endpoint is only mapped in Development.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="pattern">The route pattern used for the debug endpoint.</param>
    /// <param name="developmentOnly">Indicates whether the endpoint should only be mapped in Development.</param>
    /// <returns>The mapped route builder, or <see langword="null"/> if not mapped because of environment filtering.</returns>
    [RequiresUnreferencedCode("This method maps a delegate endpoint, which may use reflection and is not trim-safe.")]
    public static RouteHandlerBuilder? MapMiddlewarePipelineDebugEndpoint(this WebApplication app, string pattern = "/_debug/pipeline", bool developmentOnly = true)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrEmpty(pattern);

        if (developmentOnly && !app.Environment.IsDevelopment())
            return null;

        _ = GetDebugInfoProvider(app.Services);

        return app.MapGet(pattern, static (MiddlewarePipelineDebugInfoProvider debugInfoProvider) =>
        {
            return TypedResults.Ok(debugInfoProvider.GetSnapshot());
        });
    }

    private static MiddlewarePipelineDebugInfoProvider GetDebugInfoProvider(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService<MiddlewarePipelineDebugInfoProvider>() is MiddlewarePipelineDebugInfoProvider debugInfoProvider)
            return debugInfoProvider;

        throw new InvalidOperationException($"Middleware pipeline debugging services are not registered. Call {nameof(MiddlewarePipelineDebuggingServiceCollectionExtensions)}.{nameof(MiddlewarePipelineDebuggingServiceCollectionExtensions.AddMiddlewarePipelineDebugging)}(...) before building the application.");
    }
}
