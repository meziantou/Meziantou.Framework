using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Meziantou.AspNetCore.Diagnostics;

/// <summary>Represents one endpoint in the endpoint data source collection.</summary>
public sealed class MiddlewarePipelineDebugEndpoint
{
    /// <summary>
    /// Gets the underlying endpoint instance, or <see langword="null"/> when the snapshot was deserialized.
    /// </summary>
    /// <remarks>
    /// This property is excluded from serialization, so a snapshot obtained from the debug endpoint's JSON always has
    /// it set to <see langword="null"/>. It is only populated on a snapshot created in the same process.
    /// </remarks>
    [JsonIgnore]
    public Endpoint? Endpoint { get; init; }

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
