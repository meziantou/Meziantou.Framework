using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Meziantou.AspNetCore.Diagnostics;

internal sealed class MiddlewarePipelineCaptureState
{
    public MiddlewarePipelineDescriptor Root { get; } = new();

    public void Reset()
    {
        Root.Middlewares.Clear();
    }

    public MiddlewarePipelineDebugSnapshot CreateSnapshot(IEnumerable<EndpointDataSource> endpointDataSources)
    {
        ArgumentNullException.ThrowIfNull(endpointDataSources);

        var endpoints = endpointDataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .Select(static endpoint => CreateEndpoint(endpoint))
            .OrderBy(static endpoint => endpoint.RoutePattern, StringComparer.Ordinal)
            .ThenBy(static endpoint => endpoint.DisplayName, StringComparer.Ordinal)
            .ToArray();

        return new MiddlewarePipelineDebugSnapshot
        {
            Pipeline = CreatePipeline(Root),
            Endpoints = endpoints,
        };
    }

    private static MiddlewarePipelineDebugPipeline CreatePipeline(MiddlewarePipelineDescriptor pipeline)
    {
        return new MiddlewarePipelineDebugPipeline
        {
            Middlewares = pipeline.Middlewares.Select(static middleware => CreateMiddleware(middleware)).ToArray(),
        };
    }

    private static MiddlewarePipelineDebugMiddleware CreateMiddleware(MiddlewareDescriptor middleware)
    {
        return new MiddlewarePipelineDebugMiddleware
        {
            Name = middleware.Name,
            DelegateType = middleware.DelegateType,
            DelegateMethod = middleware.DelegateMethod,
            Branches = middleware.Branches.Select(static branch => CreatePipeline(branch)).ToArray(),
        };
    }

    private static MiddlewarePipelineDebugEndpoint CreateEndpoint(Endpoint endpoint)
    {
        var routeEndpoint = endpoint as RouteEndpoint;
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods?.ToArray() ?? [];

        return new MiddlewarePipelineDebugEndpoint
        {
            Endpoint = endpoint,
            DisplayName = endpoint.DisplayName,
            EndpointType = endpoint.GetType().FullName ?? endpoint.GetType().Name,
            HttpMethods = methods,
            Order = routeEndpoint?.Order,
            RoutePattern = routeEndpoint?.RoutePattern.RawText,
        };
    }
}
