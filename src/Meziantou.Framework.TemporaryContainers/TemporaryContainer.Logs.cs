using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Meziantou.Framework.TemporaryContainers;

public partial class TemporaryContainer
{
    /// <summary>Streams the container logs, following new lines until the container stops or the enumeration is cancelled.</summary>
    /// <param name="cancellationToken">A cancellation token that stops following the logs.</param>
    /// <returns>An asynchronous sequence of log entries.</returns>
    public async IAsyncEnumerable<LogEntry> GetLogsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var id = RequireId();
        var includeTimestamps = Adapter.LogsIncludeTimestamps;

        var channel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        var instance = Cli.ExecuteStreaming(
            Adapter.BuildLogsArguments(id),
            line => channel.Writer.TryWrite(ParseLog(line, LogStream.Stdout, includeTimestamps)),
            line => channel.Writer.TryWrite(ParseLog(line, LogStream.Stderr, includeTimestamps)),
            cancellationToken);

        var completion = CompleteChannelWhenDoneAsync(instance, channel.Writer);
        try
        {
            await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return entry;
        }
        finally
        {
            try
            {
                instance.Kill();
            }
            catch
            {
                // The process may already have exited.
            }

            await completion.ConfigureAwait(false);
        }
    }

    private static async Task CompleteChannelWhenDoneAsync(ProcessInstance instance, ChannelWriter<LogEntry> writer)
    {
        try
        {
            await instance.ConfigureAwait(false);
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
    }

    private static LogEntry ParseLog(string line, LogStream stream, bool includeTimestamps)
    {
        if (includeTimestamps)
        {
            var spaceIndex = line.IndexOf(' ', StringComparison.Ordinal);
            if (spaceIndex > 0 && DateTimeOffset.TryParse(line[..spaceIndex], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
                return new LogEntry(stream, line[(spaceIndex + 1)..], timestamp);
        }

        return new LogEntry(stream, line, Timestamp: null);
    }
}
