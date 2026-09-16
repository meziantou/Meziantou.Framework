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
        var entriesToSkip = _logEntriesToSkip;
        var index = 0;
        await foreach (var entry in Runtime.GetLogsAsync(id, follow, cancellationToken).ConfigureAwait(false))
        {
            if (index++ < entriesToSkip)
                continue;

            yield return entry;
        }
    }

    /// <summary>Prepares <see cref="GetCurrentRunLogsAsync"/> for a start by counting the entries the earlier runs of the container left behind, so they can be skipped.</summary>
    /// <remarks>The entries are counted rather than compared with the time the container started: the timestamps of the entries and that time do not always come from the same clock (wslc reports the time of its virtual machine), and an entry that looks older than the start of its own run would be skipped.</remarks>
    private async Task PrepareLogsForStartAsync(bool restarting, CancellationToken cancellationToken)
    {
        _logEntriesToSkip = 0;

        if (!restarting)
        {
            // A container that never ran has nothing to skip, and starting one that already runs (adopted through a
            // reuse identifier) changes nothing: what it logged belongs to the run that is still going.
            var info = await InspectAsync(cancellationToken).ConfigureAwait(false);
            if (info.State is ContainerState.Created or ContainerState.Running or ContainerState.Paused)
                return;
        }

        var count = 0;
        await foreach (var _ in Runtime.GetLogsAsync(Id, follow: false, cancellationToken).ConfigureAwait(false))
            count++;

        _logEntriesToSkip = count;
    }
}
