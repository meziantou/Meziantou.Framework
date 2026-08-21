namespace Meziantou.Framework.OpenTelemetryCollector;

public sealed class OpenTelemetryReceiverOptions
{
    public string? HttpLogsEndpoint { get; set; } = "/v1/logs";
    public string? HttpTracesEndpoint { get; set; } = "/v1/traces";
    public string? HttpMetricsEndpoint { get; set; } = "/v1/metrics";
    public bool EnableGrpcEndpoints { get; set; } = true;

    /// <summary>Gets or sets the maximum size, in bytes, of an OTLP/HTTP request payload. Larger payloads are rejected with <c>413 Content Too Large</c>.</summary>
    /// <remarks>
    /// The limit applies to the payload after decompression, so a compressed request cannot expand beyond this size.
    /// Set to <see langword="null"/> to only rely on the limit configured on the web server.
    /// </remarks>
    /// <value>The default value is 20 MiB.</value>
    public long? MaxHttpRequestBodySize { get; set; } = 20 * 1024 * 1024;

    public IList<OpenTelemetrySampler> Samplers { get; } = [];
}
