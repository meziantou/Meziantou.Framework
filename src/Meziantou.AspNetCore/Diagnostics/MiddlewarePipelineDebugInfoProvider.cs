using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.AspNetCore.Diagnostics;

/// <summary>Provides a snapshot of the middleware pipeline and endpoint list.</summary>
public sealed class MiddlewarePipelineDebugInfoProvider(IServiceProvider serviceProvider)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    /// <summary>Creates a middleware pipeline and endpoint snapshot.</summary>
    public MiddlewarePipelineDebugSnapshot GetSnapshot()
    {
        var captureState = _serviceProvider.GetRequiredService<MiddlewarePipelineCaptureState>();
        var endpointDataSources = _serviceProvider.GetServices<EndpointDataSource>();

        return captureState.CreateSnapshot(endpointDataSources);
    }
}
