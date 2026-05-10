using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector;

public sealed class OpenTelemetryReceiverOptions
{
    public string? HttpLogsEndpoint { get; set; } = "/v1/logs";
    public string? HttpTracesEndpoint { get; set; } = "/v1/traces";
    public string? HttpMetricsEndpoint { get; set; } = "/v1/metrics";
    public bool EnableGrpcEndpoints { get; set; } = true;

    public OpenTelemetryRequestFilter<ExportLogsServiceRequest>? LogsFilter { get; set; }
    public OpenTelemetryRequestFilter<ExportTraceServiceRequest>? TracesFilter { get; set; }
    public OpenTelemetryRequestFilter<ExportMetricsServiceRequest>? MetricsFilter { get; set; }

    public OpenTelemetryTailSamplingOptions TailSampling { get; } = new();
}
