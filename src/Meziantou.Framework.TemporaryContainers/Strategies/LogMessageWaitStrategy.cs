using System.Text;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.TemporaryContainers.Strategies;

internal sealed class LogMessageWaitStrategy(Regex pattern, int occurrences) : IWaitStrategy
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

    public Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);

        return WaitCoreAsync(container.GetLogsAsync, () => TryInspectAsync(container), cancellationToken);
    }

    /// <summary>The wait itself, taking the log stream and the container state as delegates so both can be faked.</summary>
    internal async Task WaitCoreAsync(Func<CancellationToken, IAsyncEnumerable<LogEntry>> getLogs, Func<Task<ContainerInfo?>> inspect, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            // Attaching to the logs replays them from the beginning, so every attempt counts the matches from scratch.
            var count = 0;
            var tail = new Queue<string>(MaxReportedLines);
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

            var info = await inspect().ConfigureAwait(false);
            if (info?.State is not ContainerState.Running)
                throw new InvalidOperationException(BuildFailureMessage(info, count, tail));

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
        message.Append(CultureInfo.InvariantCulture, $"The log pattern '{pattern}' matched {count} time(s) before the log stream ended (expected {occurrences}).");

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

    /// <summary>Inspects the container without ever throwing: the container may already be gone, and a failure to
    /// describe it must not replace the log-pattern failure with an unrelated one.</summary>
    private static async Task<ContainerInfo?> TryInspectAsync(TemporaryContainer container)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await container.InspectAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
