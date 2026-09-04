namespace Meziantou.Framework.Scheduling;

internal static class Utilities
{
    public const string FloatingDateTimeFormat = "yyyyMMddTHHmmss";
    public const string UtcDateTimeFormat = "yyyyMMddTHHmmssZ";

    /// <summary>iCalendar requires CRLF between content lines (RFC 5545 section 3.1), which
    /// <see cref="TextWriter.WriteLine()"/> does not guarantee: it emits <see cref="Environment.NewLine"/>.</summary>
    public const string CrLf = "\r\n";

    public static void WriteLine(TextWriter writer, string value)
    {
        writer.Write(value);
        writer.Write(CrLf);
    }

    public static DateTime ParseDateTime(string str)
    {
        string[] formats =
        [
            // Basic formats
            "yyyyMMddTHHmmsszzz",
            "yyyyMMddTHHmmsszz",
            "yyyyMMddTHHmmssZ",
            // Extended formats
            "yyyy-MM-ddTHH:mm:sszzz",
            "yyyy-MM-ddTHH:mm:sszz",
            "yyyy-MM-ddTHH:mm:ssZ",
            // All of the above with reduced accuracy
            "yyyyMMddTHHmmzzz",
            "yyyyMMddTHHmmzz",
            "yyyyMMddTHHmmZ",
            "yyyy-MM-ddTHH:mmzzz",
            "yyyy-MM-ddTHH:mmzz",
            "yyyy-MM-ddTHH:mmZ",
            // Accuracy reduced to hours
            "yyyyMMddTHHzzz",
            "yyyyMMddTHHzz",
            "yyyyMMddTHHZ",
            "yyyy-MM-ddTHHzzz",
            "yyyy-MM-ddTHHzz",
            "yyyy-MM-ddTHHZ",
            // Accuracy reduced to date
            "yyyyMMdd",
        ];

        var dateTime = DateTime.ParseExact(str, formats, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        return dateTime;
    }

    public static string DayOfWeekToString(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Sunday => "SU",
            DayOfWeek.Monday => "MO",
            DayOfWeek.Tuesday => "TU",
            DayOfWeek.Wednesday => "WE",
            DayOfWeek.Thursday => "TH",
            DayOfWeek.Friday => "FR",
            DayOfWeek.Saturday => "SA",
            _ => throw new ArgumentOutOfRangeException(nameof(dayOfWeek), dayOfWeek, message: null),
        };
    }

    /// <summary>Formats a date-time using one of the forms defined in RFC 5545 section 3.3.5.</summary>
    public static string DateTimeToString(DateTime dt)
    {
        return dt.Kind switch
        {
            // Form 2: a date-time in UTC.
            DateTimeKind.Utc => dt.ToString(UtcDateTimeFormat, CultureInfo.InvariantCulture),

            // A local time is only meaningful to a reader that also knows the offset, and the
            // output carries no TZID parameter to convey it, so it is written as UTC.
            DateTimeKind.Local => dt.ToUniversalTime().ToString(UtcDateTimeFormat, CultureInfo.InvariantCulture),

            // Form 1: a floating date-time, interpreted in the reader's own time zone.
            _ => dt.ToString(FloatingDateTimeFormat, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Converts a wall-clock date-time to an instant in <paramref name="timeZone"/> using the disambiguation rules of RFC 5545 section 3.3.5.</summary>
    public static DateTimeOffset ToDateTimeOffset(DateTime wallClock, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);

        if (timeZone.IsAmbiguousTime(local))
        {
            // The local time occurs twice and RFC 5545 keeps the first occurrence. Since instant = local - offset,
            // the first occurrence is the reading with the greatest offset. TimeZoneInfo.GetUtcOffset returns the
            // standard offset here, which is the second occurrence, so the ambiguous offsets are inspected instead.
            var offsets = timeZone.GetAmbiguousTimeOffsets(local);
            var offset = offsets[0];
            for (var i = 1; i < offsets.Length; i++)
            {
                if (offsets[i] > offset)
                {
                    offset = offsets[i];
                }
            }

            return new DateTimeOffset(local, offset);
        }

        if (timeZone.IsInvalidTime(local))
        {
            // The local time is skipped by a forward transition. RFC 5545 reads it with the UTC offset in effect
            // before the gap; rendering that instant in the time zone surfaces 02:30 EST as 03:30 EDT.
            return TimeZoneInfo.ConvertTime(new DateTimeOffset(local, GetUtcOffsetBeforeGap(local, timeZone)), timeZone);
        }

        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }

    /// <summary>Converts wall-clock occurrences to instants in <paramref name="timeZone"/>, dropping the duplicates
    /// that a forward transition creates.</summary>
    public static IEnumerable<DateTimeOffset> ToDateTimeOffsets(IEnumerable<DateTime> wallClockOccurrences, TimeZoneInfo timeZone)
    {
        DateTime? lastInstant = null;
        foreach (var wallClock in wallClockOccurrences)
        {
            var occurrence = ToDateTimeOffset(wallClock, timeZone);
            if (IsDuplicate(lastInstant, occurrence))
                continue;

            lastInstant = occurrence.UtcDateTime;
            yield return occurrence;
        }
    }

    /// <summary>RFC 5545 section 3.8.5.3: duplicate instances are ignored.</summary>
    /// <remarks>
    /// <para>Every local time in the gap of a forward transition is read at the UTC offset in effect before the
    /// gap, which maps the whole gap onto the instants the hour after it also denotes. A sub-hourly recurrence
    /// therefore repeats a run of instants, and the repeats are neither adjacent to nor ordered after the
    /// instances they duplicate.</para>
    /// <para>Keeping only what is strictly after the last instant produced removes every such repeat, and leaves
    /// the recurrence set increasing. The duplicate and the instance it repeats convert to the very same value,
    /// so which of the two is kept does not matter.</para>
    /// </remarks>
    public static bool IsDuplicate(DateTime? lastInstant, DateTimeOffset occurrence)
    {
        return lastInstant.HasValue && occurrence.UtcDateTime <= lastInstant.Value;
    }

    /// <summary>Gets the UTC offset in effect immediately before the forward transition that skips <paramref name="local"/>.</summary>
    private static TimeSpan GetUtcOffsetBeforeGap(DateTime local, TimeZoneInfo timeZone)
    {
        // A gap exists when the offset grows from o1 to o2 (o1 < o2) at instant T, skipping the local times in
        // [T + o1, T + o2). Reading the local time with o1 lands at or after T and so reports o2, and reading it
        // with o2 lands before T and so reports o1. Two probes therefore yield both offsets, and the smaller one
        // is the offset in effect before the gap.
        var first = timeZone.GetUtcOffset(DateTime.SpecifyKind(local - timeZone.BaseUtcOffset, DateTimeKind.Utc));
        var second = timeZone.GetUtcOffset(DateTime.SpecifyKind(local - first, DateTimeKind.Utc));
        return first < second ? first : second;
    }

    /// <summary>Expresses <paramref name="value"/> as a wall-clock reading in <paramref name="timeZone"/>.</summary>
    public static DateTime ToWallClock(DateTime value, TimeZoneInfo timeZone)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => TimeZoneInfo.ConvertTimeFromUtc(value, timeZone),
            DateTimeKind.Local => TimeZoneInfo.ConvertTime(value, TimeZoneInfo.Local, timeZone),
            _ => value,
        };
    }

    /// <summary>Formats a UTC offset as the utc-offset value type (RFC 5545 section 3.3.14).</summary>
    public static string UtcOffsetToString(TimeSpan offset)
    {
        // RFC 5545 forbids "-0000", so a zero offset is always written with a plus sign.
        var sign = offset.Ticks < 0 ? '-' : '+';
        var absolute = offset.Duration();

        var sb = new StringBuilder(7);
        sb.Append(sign);
        sb.Append(((int)absolute.TotalHours).ToString("00", CultureInfo.InvariantCulture));
        sb.Append(absolute.Minutes.ToString("00", CultureInfo.InvariantCulture));
        if (absolute.Seconds is not 0)
        {
            sb.Append(absolute.Seconds.ToString("00", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>A TZID written as a property parameter must be a paramtext that needs no DQUOTE
    /// (RFC 5545 section 3.2); rather than quote or escape, an identifier outside this set is rejected.</summary>
    public static bool IsValidTimeZoneId([NotNullWhen(returnValue: true)] string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 100)
            return false;

        foreach (var c in id)
        {
            // Covers IANA identifiers (America/Argentina/Buenos_Aires, Etc/GMT+5) and Windows ones (Romance Standard Time).
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('/' or '_' or '-' or '+' or '.' or ' '))
                return false;
        }

        return true;
    }

    public static string StatusToString(EventStatus status)
    {
        return status switch
        {
            EventStatus.Tentative => "TENTATIVE",
            EventStatus.Confirmed => "CONFIRMED",
            EventStatus.Cancelled => "CANCELLED",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, message: null),
        };
    }
}
