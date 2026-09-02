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
        var consumedCount = 0;
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var consumedBeforeAttach = consumedCount;
            try
            {
                var index = 0;
                await foreach (var entry in Runtime.GetLogsAsync(id, cancellationToken).ConfigureAwait(false))
                {
                    // Attaching to the logs replays them from the beginning, so the lines consumed by a previous
                    // attach have to be dropped: the logger must not see the whole log again on every re-attach.
                    if (index++ < consumedCount)
                        continue;

                    consumedCount = index;

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
                return;
            }
            catch
            {
                // The runtime stopped streaming, or the logger itself threw. A logger backed by a test output helper does
                // that as soon as the test that owns it completes, which is exactly when the container is being disposed.
                return;
            }

            // The stream ended on its own. The container may still have a whole life to log, so the only reason to
            // stop pumping is the container itself being done.
            if (!await IsRunningOrPausedAsync(id, cancellationToken).ConfigureAwait(false))
                return;

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

    /// <summary>Determines whether the container may still write logs, without ever throwing: forwarding logs is
    /// best-effort, so a container that cannot be inspected simply ends the pump.</summary>
    private async Task<bool> IsRunningOrPausedAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var info = await Runtime.InspectAsync(id, cts.Token).ConfigureAwait(false);

            // A paused container is not done: it keeps its logs and resumes writing to them once it is unpaused.
            return info.State is ContainerState.Running or ContainerState.Paused;
        }
        catch
        {
            return false;
        }
    }

    private static TimeSpan GetReattachDelay(int attempt)
    {
        var delay = InitialReattachDelayInMilliseconds << Math.Min(attempt, 6);
        return TimeSpan.FromMilliseconds(Math.Min(delay, MaxReattachDelayInMilliseconds));
    }
}
