using System.Text.RegularExpressions;

namespace Meziantou.Framework.TemporaryContainers.Strategies;

internal sealed class LogMessageWaitStrategy(Regex pattern, int occurrences) : IWaitStrategy
{
    public async Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);

        var count = 0;
        await foreach (var entry in container.GetLogsAsync(cancellationToken).ConfigureAwait(false))
        {
            if (pattern.IsMatch(entry.Message))
            {
                count++;
                if (count >= occurrences)
                    return;
            }
        }

        throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The log pattern '{pattern}' matched {count} time(s) before the log stream ended (expected {occurrences})."));
    }
}
