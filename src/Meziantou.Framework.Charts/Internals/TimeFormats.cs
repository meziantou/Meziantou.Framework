using System.Globalization;

namespace Meziantou.Framework.Charts;

/// <summary>Formats the times of the charts, in UTC, with the invariant culture.</summary>
internal static class TimeFormats
{
    // A day inside the bounds of DateTime, so that converting the milliseconds back to a DateTime cannot round out of them
    public static readonly double MinMilliseconds = ToMilliseconds(DateTime.MinValue.AddDays(1));
    public static readonly double MaxMilliseconds = ToMilliseconds(DateTime.MaxValue.AddDays(-1));

    /// <summary>Returns whether a time, in milliseconds since the Unix epoch, converts to a <see cref="DateTime"/>.</summary>
    public static bool IsInRange(double milliseconds) => milliseconds >= MinMilliseconds && milliseconds <= MaxMilliseconds;

    public static double ToMilliseconds(DateTime utc) => (utc - DateTime.UnixEpoch).Ticks / (double)TimeSpan.TicksPerMillisecond;

    public static DateTime FromMilliseconds(double milliseconds) => DateTime.UnixEpoch.AddTicks((long)Math.Round(milliseconds * TimeSpan.TicksPerMillisecond, MidpointRounding.AwayFromZero));

    public static string FormatCategory(double milliseconds)
    {
        var date = FromMilliseconds(milliseconds);
        return date.TimeOfDay == TimeSpan.Zero
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a tick by the largest unit it does not fall on, as d3's multi-scale time format.</summary>
    public static string FormatTick(double milliseconds)
    {
        var date = FromMilliseconds(milliseconds);
        var format = date switch
        {
            { Millisecond: not 0 } => ".fff",
            { Second: not 0 } => ":ss",
            { Minute: not 0 } or { Hour: not 0 } => "HH:mm",
            { Day: not 1 } => "MMM d",
            { Month: not 1 } => "MMM",
            _ => "yyyy",
        };

        return date.ToString(format, CultureInfo.InvariantCulture);
    }
}
