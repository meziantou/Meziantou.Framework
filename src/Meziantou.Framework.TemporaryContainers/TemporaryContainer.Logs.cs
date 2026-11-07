using System.Runtime.CompilerServices;
using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

public partial class TemporaryContainer
{
    /// <summary>Streams the container logs, from the creation of the container, following new lines until the container stops or the enumeration is cancelled.</summary>
    /// <param name="cancellationToken">A cancellation token that stops following the logs.</param>
    /// <returns>An asynchronous sequence of log entries.</returns>
    public IAsyncEnumerable<LogEntry> GetLogsAsync(CancellationToken cancellationToken = default)
        => GetLogsAsync(new ContainerLogsOptions(), cancellationToken);

    /// <summary>Streams the container logs selected by <paramref name="options"/>.</summary>
    /// <param name="options">The entries to return: a time range, the last entries, the streams, and whether to follow the new entries.</param>
    /// <param name="cancellationToken">A cancellation token that stops following the logs.</param>
    /// <returns>An asynchronous sequence of log entries.</returns>
    /// <exception cref="ArgumentException">Both <see cref="ContainerLogsOptions.IncludeStandardOutput"/> and <see cref="ContainerLogsOptions.IncludeStandardError"/> are <see langword="false"/>.</exception>
    /// <exception cref="NotSupportedException"><see cref="ContainerLogsOptions.Since"/> or <see cref="ContainerLogsOptions.Until"/> is set, and the runtime does not record timestamps.</exception>
    public IAsyncEnumerable<LogEntry> GetLogsAsync(ContainerLogsOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IncludeStandardOutput && !options.IncludeStandardError)
            throw new ArgumentException("At least one of the standard output and the standard error must be included.", nameof(options));

        var id = RequireId();

        // The options are copied: changing them while the logs are read must not change what the runtime is asked for.
        return Runtime.GetLogsAsync(id, new ContainerLogsOptions(options), cancellationToken);
    }

    /// <summary>Creates a cursor at the start of the current run of the container: a container that was stopped and started again, or restarted, keeps the logs of its earlier runs, and a wait strategy must not find its ready message there.</summary>
    internal LogCursor CreateCurrentRunLogCursor() => _currentRunLogStart?.Clone() ?? new LogCursor(Runtime.LogsIncludeTimestamps);

    /// <summary>Streams the entries after a cursor, moving it past every entry returned, so that reading again with the same cursor resumes there instead of replaying the log.</summary>
    /// <param name="cursor">The position to read from.</param>
    /// <param name="follow">Whether the stream keeps following new entries, or ends with the entries written so far.</param>
    /// <param name="tail">The number of entries to read from the end of the logs, when the runtime records timestamps. The cursor of a runtime that records none counts entries from the creation of the container, so the whole log is read.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    internal async IAsyncEnumerable<LogEntry> GetLogsAfterAsync(LogCursor cursor, bool follow, int? tail, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = RequireId();
        var options = new ContainerLogsOptions
        {
            Follow = follow,

            // The runtimes record nanoseconds, which parsing rounds to the 100 ns of DateTimeOffset, so an entry can
            // look later than it is. Asking for one tick earlier returns every entry that rounds to the time of the
            // cursor, and the cursor drops the ones it already returned.
            Since = cursor.Since?.AddTicks(-1),
            Tail = cursor.UsesTimestamps ? tail : null,
        };

        var reader = cursor.BeginRead();
        await foreach (var entry in Runtime.GetLogsAsync(id, options, cancellationToken).ConfigureAwait(false))
        {
            if (reader.TryAdvance(entry))
                yield return entry;
        }
    }

    /// <summary>Prepares <see cref="CreateCurrentRunLogCursor"/> for a start by finding where the entries the earlier runs of the container left behind end.</summary>
    /// <remarks>
    /// The position is the timestamp of the last entry, which the runtime records with the same clock as every other
    /// entry, rather than the time the container started: that time does not always come from the same clock (wslc
    /// reports the time of its virtual machine), and an entry that looks older than the start of its own run would be
    /// skipped. Unlike a number of entries, a timestamp still holds when the log driver rotates or truncates the log.
    /// </remarks>
    private async Task PrepareLogsForStartAsync(bool restarting, CancellationToken cancellationToken)
    {
        _currentRunLogStart = null;

        if (!restarting)
        {
            // A container that never ran has nothing to skip, and starting one that already runs (adopted through a
            // reuse identifier) changes nothing: what it logged belongs to the run that is still going.
            var info = await InspectAsync(cancellationToken).ConfigureAwait(false);
            if (info.State is ContainerState.Created or ContainerState.Running or ContainerState.Paused)
                return;
        }

        if (Runtime.LogsIncludeTimestamps)
        {
            DateTimeOffset? last = null;
            await foreach (var entry in Runtime.GetLogsAsync(Id, new ContainerLogsOptions { Follow = false, Tail = 1 }, cancellationToken).ConfigureAwait(false))
            {
                if (entry.Timestamp is { } timestamp && (last is null || timestamp > last))
                    last = timestamp;
            }

            if (last is { } lastTimestamp)
                _currentRunLogStart = LogCursor.After(lastTimestamp);

            return;
        }

        var count = 0;
        await foreach (var _ in Runtime.GetLogsAsync(Id, new ContainerLogsOptions { Follow = false }, cancellationToken).ConfigureAwait(false))
            count++;

        _currentRunLogStart = LogCursor.AfterEntries(count);
    }
}
