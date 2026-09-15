using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.TemporaryContainers;

public partial class TemporaryContainer
{
    // A log stream that ends is not proof that the container is done: '<runtime> logs -f' exits with code 0 and the
    // 'follow' response body ends when the runtime drops the attachment, both of which happen while the container is
    // perfectly healthy. The pump re-attaches so a container does not stop logging for the rest of its life, backing
    // off so a runtime that keeps ending the stream at once is not re-attached to in a tight loop.
    private const int InitialReattachDelayInMilliseconds = 100;
    private const int MaxReattachDelayInMilliseconds = 5000;

    // A runtime that cannot say whether the container still runs is asked again, but not forever.
    private const int MaxFailedInspections = 5;

    private void StartForwardingLogs()
    {
        // A pump that ended on its own (the container exited) is replaced, so the next run of the container is logged.
        if (_forwardLogsTask is { IsCompleted: false })
            return;

        if (_definition.Logging.Logger is not { } logger)
            return;

        if (_id is null)
            return;

        _forwardLogsCancellationTokenSource?.Dispose();
        _forwardLogsCancellationTokenSource = new CancellationTokenSource();
        _forwardLogsTask = ForwardLogsAsync(logger, _forwardLogsCancellationTokenSource.Token);
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
            await cts.CancelAsync().ConfigureAwait(false);
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

    private async Task ForwardLogsAsync(ILogger logger, CancellationToken cancellationToken)
    {
        var consumedCount = 0;
        var attempt = 0;
        var failedInspections = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var consumedBeforeAttach = consumedCount;
            try
            {
                var index = 0;
                await foreach (var entry in GetCurrentRunLogsAsync(follow: true, cancellationToken).ConfigureAwait(false))
                {
                    // Attaching to the logs replays them from the beginning, so the lines consumed by a previous
                    // attach have to be dropped: the logger must not see the whole log again on every re-attach.
                    if (index++ < consumedCount)
                        continue;

                    consumedCount = index;
                    if (!TryLog(logger, entry))
                        return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Expected: the container is stopping.
                return;
            }
            catch
            {
                // The runtime dropped the stream in the middle of an entry. That says no more about the container than
                // a stream that ends, so it is handled the same way.
            }

            // The stream ended on its own. The container may still have a whole life to log, so the only reason to
            // stop pumping is the container itself being done.
            switch (await IsRunningOrPausedAsync(cancellationToken).ConfigureAwait(false))
            {
                case false:
                    return;

                case null when ++failedInspections >= MaxFailedInspections:
                    return;

                case true:
                    failedInspections = 0;
                    break;
            }

            // A stream that carried something was a working one, so the next attach is worth making right away.
            attempt = consumedCount > consumedBeforeAttach ? 0 : attempt + 1;

            try
            {
                await Task.Delay(GetReattachDelay(attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Logs an entry. A logger backed by a test output helper throws as soon as the test that owns it completes, which is exactly when the container is being disposed, so a logger that throws ends the pump.</summary>
    private bool TryLog(ILogger logger, LogEntry entry)
    {
        try
        {
            if (entry.Stream is LogStream.Stdout && _definition.Logging.CaptureStandardOutput)
            {
                logger.LogInformation("{ContainerLog}", entry.Message);
            }
            else if (entry.Stream is LogStream.Stderr && _definition.Logging.CaptureStandardError)
            {
                logger.LogError("{ContainerLog}", entry.Message);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Determines whether the container may still write logs, without ever throwing. Returns <see langword="null"/> when the runtime could not tell.</summary>
    private async Task<bool?> IsRunningOrPausedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var info = await InspectAsync(cancellationToken).ConfigureAwait(false);

            // A paused container is not done: it keeps its logs and resumes writing to them once it is unpaused.
            return info.State is ContainerState.Running or ContainerState.Paused;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            return null;
        }
    }

    private static TimeSpan GetReattachDelay(int attempt)
    {
        var delay = InitialReattachDelayInMilliseconds << Math.Min(attempt, 6);
        return TimeSpan.FromMilliseconds(Math.Min(delay, MaxReattachDelayInMilliseconds));
    }
}
