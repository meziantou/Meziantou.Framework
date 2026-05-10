using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector;

internal sealed class OpenTelemetryRequestPipeline(
    IEnumerable<OpenTelemetryHandlerRegistration> receiverRegistrations,
    IOptions<OpenTelemetryReceiverOptions> optionsAccessor,
    OpenTelemetryTraceTailSampler tailSampler)
{
    private readonly OpenTelemetryHandler[] _receivers = GetReceivers(receiverRegistrations);
    private readonly OpenTelemetryReceiverOptions _options = optionsAccessor.Value;
    private readonly OpenTelemetryTraceTailSampler _tailSampler = tailSampler;

    public async ValueTask HandleLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_options.LogsFilter is not null && !await _options.LogsFilter(context, request, cancellationToken))
        {
            return;
        }

        foreach (var receiver in _receivers)
        {
            await receiver.HandleLogsAsync(context, request, cancellationToken);
        }
    }

    public async ValueTask HandleMetricsAsync(OpenTelemetryHandlerContext context, ExportMetricsServiceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_options.MetricsFilter is not null && !await _options.MetricsFilter(context, request, cancellationToken))
        {
            return;
        }

        foreach (var receiver in _receivers)
        {
            await receiver.HandleMetricsAsync(context, request, cancellationToken);
        }
    }

    public async ValueTask HandleTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_options.TracesFilter is not null && !await _options.TracesFilter(context, request, cancellationToken))
        {
            return;
        }

        if (!_options.TailSampling.Enabled)
        {
            await DispatchTracesAsync(context, request, cancellationToken);
            return;
        }

        await _tailSampler.HandleAsync(context, request, DispatchTracesAsync, cancellationToken);
    }

    private async ValueTask DispatchTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken)
    {
        foreach (var receiver in _receivers)
        {
            await receiver.HandleTracesAsync(context, request, cancellationToken);
        }
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
