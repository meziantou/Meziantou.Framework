using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meziantou.AspNetCore.Diagnostics;

/// <summary>Extension methods to register middleware pipeline debugging services.</summary>
public static class MiddlewarePipelineDebuggingServiceCollectionExtensions
{
    /// <summary>Adds services required to capture and expose the middleware pipeline tree.</summary>
    public static IServiceCollection AddMiddlewarePipelineDebugging(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<MiddlewarePipelineCaptureState>();
        services.TryAddSingleton<MiddlewarePipelineDebugInfoProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, MiddlewarePipelineCaptureStartupFilter>());

        return services;
    }
}
