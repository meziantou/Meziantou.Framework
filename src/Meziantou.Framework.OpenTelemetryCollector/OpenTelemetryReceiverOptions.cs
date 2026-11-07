namespace Meziantou.Framework.OpenTelemetryCollector;

public sealed class OpenTelemetryReceiverOptions
{
    public string? HttpLogsEndpoint { get; set; } = "/v1/logs";
    public string? HttpTracesEndpoint { get; set; } = "/v1/traces";
    public string? HttpMetricsEndpoint { get; set; } = "/v1/metrics";
    public bool EnableGrpcEndpoints { get; set; } = true;

    /// <summary>Gets the samplers evaluated before the handlers are called.</summary>
    /// <remarks>The list is read once, when the receiver pipeline is first resolved. Samplers added afterwards are ignored.</remarks>
    public IList<OpenTelemetrySampler> Samplers { get; } = [];
}
