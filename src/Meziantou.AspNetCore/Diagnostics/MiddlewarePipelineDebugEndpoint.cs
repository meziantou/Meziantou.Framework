using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Meziantou.AspNetCore.Diagnostics;

/// <summary>Represents one endpoint in the endpoint data source collection.</summary>
public sealed record MiddlewarePipelineDebugEndpoint
{
    /// <summary>Gets the underlying endpoint instance.</summary>
    [JsonIgnore]
    public Endpoint Endpoint { get; init; } = default!;

    /// <summary>Gets the endpoint display name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets the endpoint implementation type.</summary>
    public required string EndpointType { get; init; }

    /// <summary>Gets the route pattern if this endpoint is a <see cref="Microsoft.AspNetCore.Routing.RouteEndpoint"/>.</summary>
    public string? RoutePattern { get; init; }

    /// <summary>Gets the route order if this endpoint is a <see cref="Microsoft.AspNetCore.Routing.RouteEndpoint"/>.</summary>
    public int? Order { get; init; }

    /// <summary>Gets the allowed HTTP methods for the endpoint.</summary>
    public required IReadOnlyList<string> HttpMethods { get; init; }
}
