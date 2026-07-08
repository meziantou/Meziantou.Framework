using System.Text.RegularExpressions;
using Meziantou.Framework.TemporaryContainers.Strategies;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Provides factory methods for the built-in <see cref="IWaitStrategy"/> implementations.</summary>
public static class Wait
{
    /// <summary>Waits until a TCP connection can be established on the host port mapped to <paramref name="containerPort"/>.</summary>
    /// <param name="containerPort">The container port to probe.</param>
    /// <returns>A wait strategy.</returns>
    public static IWaitStrategy ForPort(int containerPort)
    {
        return new PortWaitStrategy(containerPort);
    }

    /// <summary>Waits until a log line contains the specified text.</summary>
    /// <param name="substring">The text to look for.</param>
    /// <param name="occurrences">The number of matching lines to wait for.</param>
    /// <returns>A wait strategy.</returns>
    public static IWaitStrategy ForLogMessage(string substring, int occurrences = 1)
    {
        ArgumentNullException.ThrowIfNull(substring);
        return new LogMessageWaitStrategy(new Regex(Regex.Escape(substring), RegexOptions.None, TimeSpan.FromSeconds(1)), occurrences);
    }

    /// <summary>Waits until a log line matches the specified pattern.</summary>
    /// <param name="pattern">The pattern to match against each log line.</param>
    /// <param name="occurrences">The number of matching lines to wait for.</param>
    /// <returns>A wait strategy.</returns>
    public static IWaitStrategy ForLogMessage(Regex pattern, int occurrences = 1)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return new LogMessageWaitStrategy(pattern, occurrences);
    }

    /// <summary>Waits for a fixed amount of time.</summary>
    /// <param name="delay">The delay to wait.</param>
    /// <returns>A wait strategy.</returns>
    public static IWaitStrategy ForDelay(TimeSpan delay)
    {
        return new DelayWaitStrategy(delay);
    }
}
