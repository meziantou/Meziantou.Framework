using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meziantou.AspNetCore.Diagnostics;

/// <summary>Extension methods to register middleware pipeline debugging services.</summary>
public static class MiddlewarePipelineDebuggingServiceCollectionExtensions
{
    /// <summary>Adds services required to capture and expose the middleware pipeline tree.</summary>
    /// <remarks>
    /// <para>
    /// Capture happens through an <see cref="Microsoft.AspNetCore.Hosting.IStartupFilter"/>, which observes the host
    /// pipeline. Middleware registered directly on a <see cref="Microsoft.AspNetCore.Builder.WebApplication"/> is
    /// <b>not</b> captured individually: <c>WebApplication</c> hands the host a single component standing for its whole
    /// pipeline, which appears as one entry named after that component. Middleware registered from an
    /// <see cref="Microsoft.AspNetCore.Hosting.IStartupFilter"/> or a classic <c>Configure</c> method is captured
    /// individually, including its branches.
    /// </para>
    /// <para>
    /// Middleware names are resolved on a best-effort basis by inspecting the registration delegate. A name that cannot
    /// be resolved degrades to the declaring type and method of the delegate; it is never guessed from unrelated state.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMiddlewarePipelineDebugging(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<MiddlewarePipelineCaptureState>();
        services.TryAddSingleton<MiddlewarePipelineDebugInfoProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, MiddlewarePipelineCaptureStartupFilter>());

        return services;
    }
}
