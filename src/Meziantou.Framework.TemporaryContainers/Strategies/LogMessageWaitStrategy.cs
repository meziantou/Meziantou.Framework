using System.Text;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.TemporaryContainers.Strategies;

/// <param name="pattern">The pattern a log line must match.</param>
/// <param name="occurrences">The number of matching lines to wait for.</param>
/// <param name="text">The text the pattern was built from, which describes the strategy better than its escaped pattern.</param>
internal sealed class LogMessageWaitStrategy(Regex pattern, int occurrences, string? text = null) : IWaitStrategy
{
    // A container that dies while starting up closes its log stream, so the wait ends without the message it was
    // looking for. Only the tail is kept: it is where the failure is reported, and a chatty image must not be
    // buffered whole just to describe a failure that may never happen.
    private const int MaxReportedLines = 20;

    // A log stream that ends is not proof that the container is done: '<runtime> logs -f' exits with code 0 and the
    // 'follow' response body ends when the runtime drops the attachment, both of which happen right after start,
    // before the container has written a single line. Re-attaching is the only way to tell that apart from a
    // container that died, so the wait keeps re-attaching while the container runs. The delay backs off so a runtime
    // that keeps ending the stream at once does not re-attach hundreds of times for the whole startup timeout.
    private const int InitialReattachDelayInMilliseconds = 100;
    private const int MaxReattachDelayInMilliseconds = 1000;

    // A runtime that does not answer an inspect says nothing about the container, so the wait asks again, but a runtime
    // that never answers ends it rather than letting it spin until the startup timeout.
    internal const int MaxFailedInspections = 3;

    public Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);

        // Only the logs of the current run count: a container started again keeps the ready message of its earlier run.
        return WaitCoreAsync(ct => container.GetCurrentRunLogsAsync(follow: true, ct), ct => TryInspectAsync(container, ct), cancellationToken);
    }

    /// <summary>The wait itself, taking the log stream and the container state as delegates so both can be faked.</summary>
    internal async Task WaitCoreAsync(Func<CancellationToken, IAsyncEnumerable<LogEntry>> getLogs, Func<CancellationToken, Task<ContainerInfo?>> inspect, CancellationToken cancellationToken)
    {
        var failedInspections = 0;
        for (var attempt = 0; ; attempt++)
        {
            // Attaching to the logs replays them from the beginning, so every attempt counts the matches from scratch.
            var count = 0;
            var tail = new Queue<string>(MaxReportedLines);
            Exception? streamFailure = null;
            try
            {
                await foreach (var entry in getLogs(cancellationToken).ConfigureAwait(false))
                {
                    if (tail.Count == MaxReportedLines)
                        tail.Dequeue();

                    tail.Enqueue(entry.Stream is LogStream.Stderr ? "[stderr] " + entry.Message : entry.Message);

                    if (pattern.IsMatch(entry.Message))
                    {
                        count++;
                        if (count >= occurrences)
                            return;
                    }
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // The runtime dropped the stream in the middle of an entry. That says no more about the container than a
                // stream that ends, so it is handled the same way.
                streamFailure = ex;
            }

            var info = await inspect(cancellationToken).ConfigureAwait(false);
            if (info is null)
            {
                failedInspections++;
                if (failedInspections >= MaxFailedInspections)
                    throw new InvalidOperationException(BuildFailureMessage(info, count, tail), streamFailure);
            }
            else if (info.State is not ContainerState.Running)
            {
                throw new InvalidOperationException(BuildFailureMessage(info, count, tail), streamFailure);
            }
            else
            {
                failedInspections = 0;
            }

            // The container is still alive, so the message it was supposed to print may still come: re-attach and
            // keep waiting until the startup timeout cancels the wait or the container actually exits.
            await Task.Delay(GetReattachDelay(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private static TimeSpan GetReattachDelay(int attempt)
    {
        var delay = InitialReattachDelayInMilliseconds << Math.Min(attempt, 4);
        return TimeSpan.FromMilliseconds(Math.Min(delay, MaxReattachDelayInMilliseconds));
    }

    /// <summary>Describes the failure with everything that explains it: what the container exited with, and what it printed before it did.</summary>
    private string BuildFailureMessage(ContainerInfo? info, int count, Queue<string> tail)
    {
        var message = new StringBuilder();
        message.Append(CultureInfo.InvariantCulture, $"The log pattern '{text ?? pattern.ToString()}' matched {count} time(s) before the log stream ended (expected {occurrences}).");

        if (info is not null)
        {
            message.Append(CultureInfo.InvariantCulture, $" The container is {info.State}");
            if (info.ExitCode is { } exitCode)
            {
                message.Append(CultureInfo.InvariantCulture, $" with exit code {exitCode}");
            }

            if (!string.IsNullOrWhiteSpace(info.Status))
            {
                message.Append(CultureInfo.InvariantCulture, $" ({info.Status})");
            }

            message.Append('.');
        }
        else
        {
            message.Append(" The state of the container could not be read.");
        }

        if (tail.Count == 0)
        {
            message.Append(" The container did not write anything to its log streams.");
            return message.ToString();
        }

        message.Append(CultureInfo.InvariantCulture, $" Last {tail.Count} log line(s):");
        foreach (var line in tail)
        {
            message.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}  {line}");
        }

        return message.ToString();
    }

    /// <summary>Inspects the container without ever throwing: the container may already be gone, and a failure to describe it must not replace the log-pattern failure with an unrelated one. The wait's own token bounds it, since a busy runtime can take a while to answer.</summary>
    private static async Task<ContainerInfo?> TryInspectAsync(TemporaryContainer container, CancellationToken cancellationToken)
    {
        try
        {
            return await container.InspectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"log message '{text ?? pattern.ToString()}' ({occurrences} occurrence(s))");
}
