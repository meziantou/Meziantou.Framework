using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector;

internal static class OpenTelemetryResponseFactory
{
    public static ExportLogsServiceResponse CreateLogsResponse(OpenTelemetryPartialSuccess partialSuccess)
    {
        var response = new ExportLogsServiceResponse();
        if (partialSuccess.TryGetResult(out var rejectedCount, out var errorMessage))
        {
            response.PartialSuccess = new ExportLogsPartialSuccess
            {
                RejectedLogRecords = rejectedCount,
                ErrorMessage = errorMessage,
            };
        }

        return response;
    }

    public static ExportTraceServiceResponse CreateTracesResponse(OpenTelemetryPartialSuccess partialSuccess)
    {
        var response = new ExportTraceServiceResponse();
        if (partialSuccess.TryGetResult(out var rejectedCount, out var errorMessage))
        {
            response.PartialSuccess = new ExportTracePartialSuccess
            {
                RejectedSpans = rejectedCount,
                ErrorMessage = errorMessage,
            };
        }

        return response;
    }

    public static ExportMetricsServiceResponse CreateMetricsResponse(OpenTelemetryPartialSuccess partialSuccess)
    {
        var response = new ExportMetricsServiceResponse();
        if (partialSuccess.TryGetResult(out var rejectedCount, out var errorMessage))
        {
            response.PartialSuccess = new ExportMetricsPartialSuccess
            {
                RejectedDataPoints = rejectedCount,
                ErrorMessage = errorMessage,
            };
        }

        return response;
    }
}
