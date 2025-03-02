using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Meziantou.Framework;

public sealed class RequestCounter(IHttpContextAccessor httpContextAccessor)
{
    private long _totalCount;
    private readonly ConcurrentDictionary<Key, long> _endpointCounter = [];

    public long TotalCount => _totalCount;

    public long Get()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            throw new InvalidOperationException("HttpContext cannot be found");

        return Get(httpContext);
    }

    public long Get(HttpContext httpContext) => Get(httpContext.GetEndpoint());
    public long Get(Endpoint endpoint) => _endpointCounter.GetValueOrDefault(GetKey(endpoint));

    internal void IncrementTotal()
    {
        Interlocked.Increment(ref _totalCount);
    }

    internal void IncrementEndpoint(Endpoint endpoint)
    {
        _endpointCounter.AddOrUpdate(GetKey(endpoint), _ => 1, (_, count) => count + 1);
    }

    internal void IncrementEndpoint(HttpContext httpContext)
    {
        var feature = httpContext.Features.Get<IEndpointFeature>();
        if (feature is not null)
        {
            IncrementEndpoint(feature.Endpoint);
        }
    }

    private static Key GetKey(Endpoint endpoint) => new(endpoint.DisplayName, endpoint.RequestDelegate);

    private readonly record struct Key(string Name, RequestDelegate RequestDelegate);
}
