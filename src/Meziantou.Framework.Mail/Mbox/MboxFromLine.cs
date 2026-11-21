using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Mail;

/// <summary>Reads and writes the <c>From </c> line of a message of an mbox file: <c>From sender asctime-date</c>.</summary>
internal static class MboxFromLine
{
    private static readonly string[] Days = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static (string? Sender, DateTimeOffset? Date) Parse(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("From", StringComparison.Ordinal))
        {
            return (null, null);
        }

        // The date is the last five words: day of week, month, day, time, year (some writers add a time zone)
        DateTimeOffset? date = null;
        for (var start = 2; start + 5 <= parts.Length && date is null; start++)
        {
            date = ParseDate(parts.AsSpan(start, 5));
        }

        return (parts[1], date);
    }

    public static string Format(string sender, DateTimeOffset date)
    {
        var utc = date.UtcDateTime;
        var builder = new StringBuilder("From ");
        builder.Append(sender).Append(' ');
        builder.Append(Days[(int)utc.DayOfWeek]).Append(' ').Append(Months[utc.Month - 1]).Append(' ');
        builder.Append(utc.Day.ToString(CultureInfo.InvariantCulture).PadLeft(2)).Append(' ');
        builder.Append(utc.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append(' ');
        builder.Append(utc.Year.ToString("D4", CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    /// <summary>Returns a sender a <c>From </c> line can contain: one word without spaces.</summary>
    public static string CleanSender(string? sender)
    {
        if (string.IsNullOrWhiteSpace(sender))
        {
            return "MAILER-DAEMON";
        }

        var builder = new StringBuilder(sender.Length);
        foreach (var c in sender)
        {
            builder.Append(char.IsWhiteSpace(c) || char.IsControl(c) ? '_' : c);
        }

        return builder.ToString();
    }

    private static DateTimeOffset? ParseDate(ReadOnlySpan<string> parts)
    {
        var monthName = parts[1];
        var month = Array.FindIndex(Months, name => name.Equals(monthName, StringComparison.OrdinalIgnoreCase)) + 1;
        if (!Days.Contains(parts[0], StringComparer.OrdinalIgnoreCase) || month == 0
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var day)
            || !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !TimeSpan.TryParseExact(parts[3], "hh\\:mm\\:ss", CultureInfo.InvariantCulture, out var time)
            || year is < 1 or > 9999 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return null;
        }

        return new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero) + time;
    }
}
