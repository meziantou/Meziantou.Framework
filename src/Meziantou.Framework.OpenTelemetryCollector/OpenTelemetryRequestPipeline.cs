using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector;

internal sealed class OpenTelemetryRequestPipeline
{
    private readonly OpenTelemetryFilter[] _filters;
    private readonly OpenTelemetryHandler[] _receivers;
    private readonly OpenTelemetryTraceTailSampler _tailSampler;
    private readonly OpenTelemetryTailSamplingFilter? _tailSamplingFilter;
    private readonly OpenTelemetryFilter[] _traceFiltersWithoutTailSampling;

    public OpenTelemetryRequestPipeline(
        IEnumerable<OpenTelemetryHandlerRegistration> receiverRegistrations,
        IOptions<OpenTelemetryReceiverOptions> optionsAccessor,
        OpenTelemetryTraceTailSampler tailSampler)
    {
        _receivers = GetReceivers(receiverRegistrations);
        _tailSampler = tailSampler;

        var options = optionsAccessor.Value;
        _filters = [.. options.Filters];
        _tailSamplingFilter = GetTailSamplingFilter(_filters);
        _traceFiltersWithoutTailSampling = [.. _filters.Where(static filter => filter is not OpenTelemetryTailSamplingFilter)];
    }

    public async ValueTask HandleLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var filter in _filters)
        {
            if (!await filter.ShouldProcessLogsAsync(context, request, cancellationToken))
            {
                return;
            }
        }

        foreach (var receiver in _receivers)
        {
            await receiver.HandleLogsAsync(context, request, cancellationToken);
        }
    }

    public async ValueTask HandleMetricsAsync(OpenTelemetryHandlerContext context, ExportMetricsServiceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var filter in _filters)
        {
            if (!await filter.ShouldProcessMetricsAsync(context, request, cancellationToken))
            {
                return;
            }
        }

        foreach (var receiver in _receivers)
        {
            await receiver.HandleMetricsAsync(context, request, cancellationToken);
        }
    }

    public async ValueTask HandleTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var filter in _traceFiltersWithoutTailSampling)
        {
            if (!await filter.ShouldProcessTracesAsync(context, request, cancellationToken))
            {
                return;
            }
        }

        if (_tailSamplingFilter is null)
        {
            await DispatchTracesAsync(context, request, cancellationToken);
            return;
        }

        await _tailSampler.HandleAsync(context, request, _tailSamplingFilter, DispatchTracesAsync, cancellationToken);
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

    private static OpenTelemetryTailSamplingFilter? GetTailSamplingFilter(OpenTelemetryFilter[] filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        OpenTelemetryTailSamplingFilter? result = null;
        foreach (var filter in filters)
        {
            if (filter is not OpenTelemetryTailSamplingFilter tailSamplingFilter)
            {
                continue;
            }

            if (result is not null)
            {
                throw new InvalidOperationException($"Only one {nameof(OpenTelemetryTailSamplingFilter)} can be added to {nameof(OpenTelemetryReceiverOptions)}.{nameof(OpenTelemetryReceiverOptions.Filters)}.");
            }

            result = tailSamplingFilter;
        }

        return result;
    }
}
