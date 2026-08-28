using System.Globalization;

namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a date.</summary>
public sealed class AdfDate : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.Date;

    /// <summary>Gets the date, as the number of milliseconds since the Unix epoch.</summary>
    public required string Timestamp { get; init; }

    /// <summary>
    /// Converts <see cref="Timestamp"/> to a <see cref="DateTimeOffset"/>, or returns
    /// <see langword="null"/> when it does not hold a number.
    /// </summary>
    /// <remarks>
    /// The Atlassian documentation shows an example that looks like a number of seconds, but the
    /// APIs return milliseconds. Values small enough to be implausible as milliseconds are read as
    /// seconds.
    /// </remarks>
    public DateTimeOffset? GetDateTimeOffset()
    {
        if (!long.TryParse(Timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return null;

        // 1e11 ms is 1973, 1e11 s is the year 5138: anything below is a number of seconds.
        return Math.Abs(value) < 100_000_000_000L
            ? DateTimeOffset.FromUnixTimeSeconds(value)
            : DateTimeOffset.FromUnixTimeMilliseconds(value);
    }
}
