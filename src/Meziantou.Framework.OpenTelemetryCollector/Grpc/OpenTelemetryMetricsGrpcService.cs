using Grpc.Core;
using OpenTelemetry.Proto.Collector.Metrics.V1;

namespace Meziantou.Framework.OpenTelemetryCollector.Abstractions.Grpc;

internal sealed class OpenTelemetryMetricsGrpcService(OpenTelemetryRequestPipeline pipeline) : MetricsService.MetricsServiceBase
{
    private readonly OpenTelemetryRequestPipeline _pipeline = pipeline;

    public override async Task<ExportMetricsServiceResponse> Export(ExportMetricsServiceRequest request, ServerCallContext context)
    {
        var partialSuccess = new OpenTelemetryPartialSuccess();
        var receiverContext = OpenTelemetryHandlerContext.CreateGrpc(context.Method, partialSuccess);
        await _pipeline.HandleMetricsAsync(receiverContext, request, context.CancellationToken);

        return OpenTelemetryResponseFactory.CreateMetricsResponse(partialSuccess);
    }
}
