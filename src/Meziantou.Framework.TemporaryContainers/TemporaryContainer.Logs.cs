using System.Runtime.CompilerServices;

namespace Meziantou.Framework.TemporaryContainers;

public partial class TemporaryContainer
{
    /// <summary>Streams the container logs, from the creation of the container, following new lines until the container stops or the enumeration is cancelled.</summary>
    /// <param name="cancellationToken">A cancellation token that stops following the logs.</param>
    /// <returns>An asynchronous sequence of log entries.</returns>
    public async IAsyncEnumerable<LogEntry> GetLogsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var id = RequireId();
        await foreach (var entry in Runtime.GetLogsAsync(id, follow: true, cancellationToken).ConfigureAwait(false))
            yield return entry;
    }

    /// <summary>Streams the logs of the current run of the container: a container that was stopped and started again, or restarted, keeps the logs of its earlier runs, and a wait strategy must not find its ready message there.</summary>
    internal async IAsyncEnumerable<LogEntry> GetCurrentRunLogsAsync(bool follow, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = RequireId();
        var startedAt = _startedAt;
        var entriesToSkip = _logEntriesToSkip;
        var index = 0;
        await foreach (var entry in Runtime.GetLogsAsync(id, follow, cancellationToken).ConfigureAwait(false))
        {
            if (index++ < entriesToSkip)
                continue;

            if (startedAt is { } start && entry.Timestamp is { } timestamp && timestamp < start)
                continue;

            yield return entry;
        }
    }

    /// <summary>Prepares <see cref="GetCurrentRunLogsAsync"/> for a start. A runtime that time-stamps its log entries needs nothing: the start time reported by the runtime separates the runs. For the others, the entries of the earlier runs are counted, so they can be skipped.</summary>
    private async Task PrepareLogsForStartAsync(bool restarting, CancellationToken cancellationToken)
    {
        _logEntriesToSkip = 0;
        if (Runtime.LogsIncludeTimestamps)
            return;

        if (!restarting)
        {
            // A container that never ran has no earlier run, and one that is already running (adopted through a reuse
            // identifier) is not started again, so its logs are all current.
            var info = await InspectAsync(cancellationToken).ConfigureAwait(false);
            if (info.State is ContainerState.Created or ContainerState.Running)
                return;
        }

        var count = 0;
        await foreach (var _ in Runtime.GetLogsAsync(Id, follow: false, cancellationToken).ConfigureAwait(false))
            count++;

        _logEntriesToSkip = count;
    }
}
