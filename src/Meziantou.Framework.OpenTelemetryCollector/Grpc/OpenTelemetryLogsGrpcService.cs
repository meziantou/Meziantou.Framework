using Grpc.Core;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Meziantou.Framework.OpenTelemetryCollector.Abstractions.Grpc;

internal sealed class OpenTelemetryLogsGrpcService(IEnumerable<OpenTelemetryHandlerRegistration> receiverRegistrations) : LogsService.LogsServiceBase
{
    private readonly OpenTelemetryHandler[] _receivers = GetReceivers(receiverRegistrations);

    public override async Task<ExportLogsServiceResponse> Export(ExportLogsServiceRequest request, ServerCallContext context)
    {
        var receiverContext = OpenTelemetryHandlerContext.CreateGrpc(context.Method);
        foreach (var receiver in _receivers)
        {
            await receiver.HandleLogsAsync(receiverContext, request, context.CancellationToken);
        }

        return new ExportLogsServiceResponse();
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
