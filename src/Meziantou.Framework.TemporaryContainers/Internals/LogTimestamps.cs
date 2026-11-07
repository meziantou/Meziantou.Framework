namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Formats the times given to the runtimes to select log entries.</summary>
internal static class LogTimestamps
{
    /// <summary>Formats a time as RFC 3339 in UTC, with the 100 ns precision of <see cref="DateTimeOffset"/>, the format the <c>--since</c> and <c>--until</c> options of the CLIs accept.</summary>
    public static string FormatRfc3339(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    /// <summary>Formats a time as the number of seconds since the Unix epoch, with a 9-digit fraction, the format the <c>since</c> and <c>until</c> parameters of the Docker Engine API accept.</summary>
    public static string FormatUnix(DateTimeOffset value)
    {
        // The API reads a time before the epoch as "no limit", which a time that old means anyway.
        var ticks = Math.Max(0, (value - DateTimeOffset.UnixEpoch).Ticks);
        var seconds = ticks / TimeSpan.TicksPerSecond;
        var nanoseconds = ticks % TimeSpan.TicksPerSecond * 100;
        return string.Create(CultureInfo.InvariantCulture, $"{seconds}.{nanoseconds:D9}");
    }
}
