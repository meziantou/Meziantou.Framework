using Grpc.Core;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Meziantou.Framework.OpenTelemetryCollector.Abstractions.Grpc;

internal sealed class OpenTelemetryLogsGrpcService(OpenTelemetryRequestPipeline pipeline) : LogsService.LogsServiceBase
{
    private readonly OpenTelemetryRequestPipeline _pipeline = pipeline;

    public override async Task<ExportLogsServiceResponse> Export(ExportLogsServiceRequest request, ServerCallContext context)
    {
        var receiverContext = OpenTelemetryHandlerContext.CreateGrpc(context.Method);
        await _pipeline.HandleLogsAsync(receiverContext, request, context.CancellationToken);

        return new ExportLogsServiceResponse();
    }
}
