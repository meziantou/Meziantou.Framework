using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meziantou.DnsProxy.Filtering;

internal sealed class FilterEngineRefreshService(FilterEngineProvider filterEngineProvider, IOptions<DnsProxyOptions> options, ILogger<FilterEngineRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await filterEngineProvider.RefreshAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh filter rules during startup");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var refreshInterval = options.Value.FilterRefreshInterval;
            if (refreshInterval <= TimeSpan.Zero)
            {
                refreshInterval = TimeSpan.FromMinutes(30);
            }

            try
            {
                await Task.Delay(refreshInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await filterEngineProvider.RefreshAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to refresh filter rules");
            }
        }
    }
}
