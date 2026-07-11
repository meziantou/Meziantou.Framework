using System.Runtime.CompilerServices;

namespace Meziantou.Framework.TemporaryContainers;

public partial class TemporaryContainer
{
    /// <summary>Streams the container logs, following new lines until the container stops or the enumeration is cancelled.</summary>
    /// <param name="cancellationToken">A cancellation token that stops following the logs.</param>
    /// <returns>An asynchronous sequence of log entries.</returns>
    public async IAsyncEnumerable<LogEntry> GetLogsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var id = RequireId();
        await foreach (var entry in Runtime.GetLogsAsync(id, cancellationToken).ConfigureAwait(false))
            yield return entry;
    }
}
