using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector;

internal sealed class OpenTelemetryRequestPipeline
{
    private readonly OpenTelemetrySampler[] _samplers;
    private readonly OpenTelemetryHandler[] _receivers;
    private readonly OpenTelemetryTraceTailSamplerHandler _tailSamplerHandler;
    private readonly OpenTelemetryTailSampler? _tailSampler;
    private readonly OpenTelemetrySampler[] _traceSamplersWithoutTailSampler;

    public OpenTelemetryRequestPipeline(
        IEnumerable<OpenTelemetryHandlerRegistration> receiverRegistrations,
        IOptions<OpenTelemetryReceiverOptions> optionsAccessor,
        OpenTelemetryTraceTailSamplerHandler tailSamplerHandler)
    {
        _receivers = GetReceivers(receiverRegistrations);
        _tailSamplerHandler = tailSamplerHandler;

        var options = optionsAccessor.Value;
        _samplers = [.. options.Samplers];
        _tailSampler = GetTailSampler(_samplers);
        _traceSamplersWithoutTailSampler = [.. _samplers.Where(static sampler => sampler is not OpenTelemetryTailSampler)];
    }

    public async ValueTask HandleLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var sampler in _samplers)
        {
            if (!await sampler.ShouldSampleLogsAsync(context, request, cancellationToken))
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

        foreach (var sampler in _samplers)
        {
            if (!await sampler.ShouldSampleMetricsAsync(context, request, cancellationToken))
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

        foreach (var sampler in _traceSamplersWithoutTailSampler)
        {
            if (!await sampler.ShouldSampleTracesAsync(context, request, cancellationToken))
            {
                return;
            }
        }

        if (_tailSampler is null)
        {
            await DispatchTracesAsync(context, request, cancellationToken);
            return;
        }

        await _tailSamplerHandler.HandleAsync(context, request, _tailSampler, DispatchTracesAsync, cancellationToken);
    }

    /// <summary>Gets the interval at which buffered traces must be swept, or <see langword="null"/> when no tail sampler is configured.</summary>
    public TimeSpan? TailSamplerSweepInterval => _tailSampler?.GetSweepInterval();

    public ValueTask FlushTimedOutTracesAsync(CancellationToken cancellationToken)
    {
        if (_tailSampler is null)
        {
            return ValueTask.CompletedTask;
        }

        return _tailSamplerHandler.FlushTimedOutTracesAsync(_tailSampler, DispatchTracesAsync, cancellationToken);
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

        // The same handler instance can be registered more than once, typically when AddOpenTelemetryReceiver is called
        // from several places for the same receiver. Dispatching to it twice would duplicate every record.
        var receivers = new List<OpenTelemetryHandler>();
        var seenHandlers = new HashSet<OpenTelemetryHandler>(ReferenceEqualityComparer.Instance);
        foreach (var registration in receiverRegistrations)
        {
            if (seenHandlers.Add(registration.Handler))
            {
                receivers.Add(registration.Handler);
            }
        }

        if (receivers.Count is 0)
        {
            throw new InvalidOperationException($"No OpenTelemetry receivers are registered. Use {nameof(OpenTelemetryServiceCollectionExtensions)}.{nameof(OpenTelemetryServiceCollectionExtensions.AddOpenTelemetryReceiver)}(...).");
        }

        return [.. receivers];
    }

    private static OpenTelemetryTailSampler? GetTailSampler(OpenTelemetrySampler[] samplers)
    {
        ArgumentNullException.ThrowIfNull(samplers);

        OpenTelemetryTailSampler? result = null;
        foreach (var sampler in samplers)
        {
            if (sampler is not OpenTelemetryTailSampler tailSampling)
            {
                continue;
            }

            if (result is not null)
            {
                throw new InvalidOperationException($"Only one {nameof(OpenTelemetryTailSampler)} can be added to {nameof(OpenTelemetryReceiverOptions)}.{nameof(OpenTelemetryReceiverOptions.Samplers)}.");
            }

            result = tailSampling;
        }

        return result;
    }
}
