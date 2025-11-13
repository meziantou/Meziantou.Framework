using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Meziantou.Framework;

/// <summary>Tracks the number of requests made to the mock server and individual endpoints.</summary>
public sealed class RequestCounter(IHttpContextAccessor httpContextAccessor)
{
    private long _totalCount;
    private readonly ConcurrentDictionary<Key, long> _endpointCounter = [];

    /// <summary>Gets the total number of requests made to the mock server.</summary>
    public long TotalCount => _totalCount;

    /// <summary>Gets the number of requests made to the current endpoint.</summary>
    /// <returns>The number of requests made to the current endpoint.</returns>
    public long Get()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            throw new InvalidOperationException("HttpContext cannot be found");

        return Get(httpContext);
    }

    /// <summary>Gets the number of requests made to the endpoint associated with the specified <see cref="HttpContext"/>.</summary>
    /// <param name="httpContext">The HTTP context containing the endpoint.</param>
    /// <returns>The number of requests made to the endpoint.</returns>
    public long Get(HttpContext httpContext) => Get(httpContext.GetEndpoint());

    /// <summary>Gets the number of requests made to the specified endpoint.</summary>
    /// <param name="endpoint">The endpoint to get the request count for.</param>
    /// <returns>The number of requests made to the endpoint.</returns>
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
