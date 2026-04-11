using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.Http.Recording;

/// <summary>Represents a recorded HTTP request/response pair.</summary>
public sealed class HttpRecordingEntry
{
    /// <summary>Gets or sets the HTTP method (e.g., GET, POST).</summary>
    [JsonPropertyName("method")]
    public required string Method { get; set; }

    /// <summary>Gets or sets the absolute request URI.</summary>
    [JsonPropertyName("requestUri")]
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings")]
    public required string RequestUri { get; set; }

    /// <summary>Gets or sets the request headers.</summary>
    [JsonPropertyName("requestHeaders")]
    public Dictionary<string, string[]>? RequestHeaders { get; set; }

    /// <summary>Gets or sets the request body.</summary>
    [JsonPropertyName("requestBody")]
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[]? RequestBody { get; set; }

    /// <summary>Gets or sets the HTTP status code.</summary>
    [JsonPropertyName("statusCode")]
    public required int StatusCode { get; set; }

    /// <summary>Gets or sets the response headers.</summary>
    [JsonPropertyName("responseHeaders")]
    public Dictionary<string, string[]>? ResponseHeaders { get; set; }

    /// <summary>Gets or sets the response body.</summary>
    [JsonPropertyName("responseBody")]
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[]? ResponseBody { get; set; }

    /// <summary>Gets or sets the timestamp when this entry was recorded.</summary>
    [JsonPropertyName("recordedAt")]
    public DateTimeOffset RecordedAt { get; set; }
}
