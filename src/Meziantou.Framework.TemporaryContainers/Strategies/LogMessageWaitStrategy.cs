using System.Text;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.TemporaryContainers.Strategies;

internal sealed class LogMessageWaitStrategy(Regex pattern, int occurrences) : IWaitStrategy
{
    // A container that dies while starting up closes its log stream, so the wait ends without the message it was
    // looking for. Only the tail is kept: it is where the failure is reported, and a chatty image must not be
    // buffered whole just to describe a failure that may never happen.
    private const int MaxReportedLines = 20;

    public async Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);

        var count = 0;
        var tail = new Queue<string>(MaxReportedLines);
        await foreach (var entry in container.GetLogsAsync(cancellationToken).ConfigureAwait(false))
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

        throw new InvalidOperationException(await BuildFailureMessageAsync(container, count, tail).ConfigureAwait(false));
    }

    /// <summary>Describes the failure with everything that explains it: what the container exited with, and what it printed before it did.</summary>
    private async Task<string> BuildFailureMessageAsync(TemporaryContainer container, int count, Queue<string> tail)
    {
        var message = new StringBuilder();
        message.Append(CultureInfo.InvariantCulture, $"The log pattern '{pattern}' matched {count} time(s) before the log stream ended (expected {occurrences}).");

        if (await TryInspectAsync(container).ConfigureAwait(false) is { } info)
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
