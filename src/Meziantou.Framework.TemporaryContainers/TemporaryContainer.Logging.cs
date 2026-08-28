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
        catch
        {
            // Forwarding logs is best-effort. A pump that faulted must not break the lifecycle operation that stops it,
            // and it must never keep the container from being removed on dispose.
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
            // Expected: the container is stopping.
        }
        catch
        {
            // The runtime stopped streaming, or the logger itself threw. A logger backed by a test output helper does
            // that as soon as the test that owns it completes, which is exactly when the container is being disposed.
        }
    }
}
