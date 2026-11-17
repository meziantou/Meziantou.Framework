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

    /// <summary>Converts a time, in milliseconds since the Unix epoch, to the closest <see cref="DateTime"/>: the times past the bounds of <see cref="DateTime"/> convert to these bounds.</summary>
    public static DateTime FromMilliseconds(double milliseconds)
    {
        // The conversion to double rounds the number of ticks, so the bounds of DateTime may not round-trip without the clamp
        var ticks = Math.Round(milliseconds * TimeSpan.TicksPerMillisecond, MidpointRounding.AwayFromZero);
        var minTicks = DateTime.MinValue.Ticks - DateTime.UnixEpoch.Ticks;
        var maxTicks = DateTime.MaxValue.Ticks - DateTime.UnixEpoch.Ticks;
        var offset = ticks <= minTicks ? minTicks : ticks >= maxTicks ? maxTicks : Math.Clamp((long)ticks, minTicks, maxTicks);
        return DateTime.UnixEpoch.AddTicks(offset);
    }

    /// <summary>Formats a time as a category, or returns <see langword="null"/> when it is not finite or outside the bounds of <see cref="DateTime"/>, as the times a mark derives from its data can be.</summary>
    public static string? FormatCategory(double milliseconds)
    {
        if (!(milliseconds >= ToMilliseconds(DateTime.MinValue) && milliseconds <= ToMilliseconds(DateTime.MaxValue)))
            return null;

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
