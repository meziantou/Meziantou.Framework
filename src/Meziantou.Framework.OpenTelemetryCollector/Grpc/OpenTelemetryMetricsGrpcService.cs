using Grpc.Core;
using OpenTelemetry.Proto.Collector.Metrics.V1;

namespace Meziantou.Framework.OpenTelemetryCollector.Abstractions.Grpc;

internal sealed class OpenTelemetryMetricsGrpcService(IEnumerable<OpenTelemetryHandlerRegistration> receiverRegistrations) : MetricsService.MetricsServiceBase
{
    private readonly OpenTelemetryHandler[] _receivers = GetReceivers(receiverRegistrations);

    public override async Task<ExportMetricsServiceResponse> Export(ExportMetricsServiceRequest request, ServerCallContext context)
    {
        var receiverContext = OpenTelemetryHandlerContext.CreateGrpc(context.Method);
        foreach (var receiver in _receivers)
        {
            await receiver.HandleMetricsAsync(receiverContext, request, context.CancellationToken);
        }

        return new ExportMetricsServiceResponse();
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
