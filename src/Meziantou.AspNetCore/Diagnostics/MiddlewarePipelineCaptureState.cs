using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Meziantou.AspNetCore.Diagnostics;

internal sealed class MiddlewarePipelineCaptureState
{
    private static readonly MiddlewarePipelineDebugPipeline EmptyPipeline = new() { Middlewares = [] };

    // Written once by the startup filter when configuration completes, read by any thread afterwards. Readers never
    // touch Root, so appends during configuration cannot tear a read or throw "Collection was modified".
    private volatile MiddlewarePipelineDebugPipeline? _publishedPipeline;

    public MiddlewarePipelineDescriptor Root { get; } = new();

    public void Reset()
    {
        _publishedPipeline = null;
        Root.Middlewares.Clear();
    }

    /// <summary>Projects the recorded pipeline into an immutable tree and publishes it to readers.</summary>
    public void Publish() => _publishedPipeline = CreatePipeline(Root);

    public MiddlewarePipelineDebugSnapshot CreateSnapshot(IEnumerable<EndpointDataSource> endpointDataSources)
    {
        ArgumentNullException.ThrowIfNull(endpointDataSources);

        var pipeline = _publishedPipeline;

        var endpoints = endpointDataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .Select(static endpoint => CreateEndpoint(endpoint))
            .OrderBy(static endpoint => endpoint.RoutePattern, StringComparer.Ordinal)
            .ThenBy(static endpoint => endpoint.DisplayName, StringComparer.Ordinal)
            .ToArray();

        return new MiddlewarePipelineDebugSnapshot
        {
            Pipeline = pipeline ?? EmptyPipeline,
            IsPipelineCaptured = pipeline is not null,
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
