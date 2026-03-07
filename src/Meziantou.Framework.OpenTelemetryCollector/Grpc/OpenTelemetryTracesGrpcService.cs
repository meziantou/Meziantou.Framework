using Grpc.Core;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector.Abstractions.Grpc;

internal sealed class OpenTelemetryTracesGrpcService(IEnumerable<OpenTelemetryHandlerRegistration> receiverRegistrations) : TraceService.TraceServiceBase
{
    private readonly OpenTelemetryHandler[] _receivers = GetReceivers(receiverRegistrations);

    public override async Task<ExportTraceServiceResponse> Export(ExportTraceServiceRequest request, ServerCallContext context)
    {
        var receiverContext = OpenTelemetryHandlerContext.CreateGrpc(context.Method);
        foreach (var receiver in _receivers)
        {
            await receiver.HandleTracesAsync(receiverContext, request, context.CancellationToken);
        }

        return new ExportTraceServiceResponse();
    }

    private static OpenTelemetryHandler[] GetReceivers(IEnumerable<OpenTelemetryHandlerRegistration> receiverRegistrations)
    {
        ArgumentNullException.ThrowIfNull(receiverRegistrations);

        var receivers = receiverRegistrations.Select(static item => item.Handler).ToArray();
        if (receivers.Length is 0)
        {
            throw new InvalidOperationException($"No OpenTelemetry receivers are registered. Use {nameof(OpenTelemetryServiceCollectionExtensions)}.{nameof(OpenTelemetryServiceCollectionExtensions.AddOpenTelemetryReceiver)}(...).");
        }

        return receivers;
    }
}
