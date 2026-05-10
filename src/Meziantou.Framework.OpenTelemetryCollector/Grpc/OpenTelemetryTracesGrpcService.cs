using Grpc.Core;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector.Abstractions.Grpc;

internal sealed class OpenTelemetryTracesGrpcService(OpenTelemetryRequestPipeline pipeline) : TraceService.TraceServiceBase
{
    private readonly OpenTelemetryRequestPipeline _pipeline = pipeline;

    public override async Task<ExportTraceServiceResponse> Export(ExportTraceServiceRequest request, ServerCallContext context)
    {
        var receiverContext = OpenTelemetryHandlerContext.CreateGrpc(context.Method);
        await _pipeline.HandleTracesAsync(receiverContext, request, context.CancellationToken);

        return new ExportTraceServiceResponse();
    }
}
