using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.OpenTelemetryCollector;

/// <summary>Periodically releases the buffered traces that reached <see cref="OpenTelemetryTailSampler.MaxTraceDuration"/>.</summary>
internal sealed class OpenTelemetryTailSamplerBackgroundService(
    OpenTelemetryRequestPipeline pipeline,
    TimeProvider timeProvider,
    ILogger<OpenTelemetryTailSamplerBackgroundService> logger) : BackgroundService
{
    private readonly OpenTelemetryRequestPipeline _pipeline = pipeline;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<OpenTelemetryTailSamplerBackgroundService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_pipeline.TailSamplerSweepInterval is not { } interval)
        {
            return;
        }

        using var timer = new PeriodicTimer(interval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await _pipeline.FlushTimedOutTracesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Cannot dispatch the buffered traces that reached the maximum trace duration");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
