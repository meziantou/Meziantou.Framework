namespace Meziantou.Framework.OpenTelemetryCollector;

public sealed class OpenTelemetryReceiverOptions
{
    public string? HttpLogsEndpoint { get; set; } = "/v1/logs";
    public string? HttpTracesEndpoint { get; set; } = "/v1/traces";
    public string? HttpMetricsEndpoint { get; set; } = "/v1/metrics";
    public bool EnableGrpcEndpoints { get; set; } = true;

    public IList<OpenTelemetrySampler> Samplers { get; } = [];
}
