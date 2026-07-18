using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.TemporaryContainers;

public partial class TemporaryContainer
{
    private void StartForwardingLogs()
    {
        if (_forwardLogsTask is not null)
            return;

        if (_definition.Logging.Logger is not { } logger)
            return;

        if (_id is null)
            return;

        _forwardLogsCancellationTokenSource = new CancellationTokenSource();
        _forwardLogsTask = ForwardLogsAsync(_id, logger, _forwardLogsCancellationTokenSource.Token);
    }

    private async Task StopForwardingLogsAsync()
    {
        var cts = _forwardLogsCancellationTokenSource;
        var task = _forwardLogsTask;
        _forwardLogsCancellationTokenSource = null;
        _forwardLogsTask = null;

        if (cts is null || task is null)
            return;

        try
        {
            cts.Cancel();
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task ForwardLogsAsync(string id, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var entry in Runtime.GetLogsAsync(id, cancellationToken).ConfigureAwait(false))
            {
                if (entry.Stream is LogStream.Stdout && _definition.Logging.CaptureStandardOutput)
                {
                    logger.LogInformation("{ContainerLog}", entry.Message);
                }
                else if (entry.Stream is LogStream.Stderr && _definition.Logging.CaptureStandardError)
                {
                    logger.LogError("{ContainerLog}", entry.Message);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
