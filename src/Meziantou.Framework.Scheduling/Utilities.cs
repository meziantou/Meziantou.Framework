namespace Meziantou.Framework.Scheduling;

internal static class Utilities
{
    public const string FloatingDateTimeFormat = "yyyyMMddTHHmmss";
    public const string UtcDateTimeFormat = "yyyyMMddTHHmmssZ";

    /// <summary>iCalendar requires CRLF between content lines (RFC 5545 section 3.1), which
    /// <see cref="TextWriter.WriteLine()"/> does not guarantee: it emits <see cref="Environment.NewLine"/>.</summary>
    public const string CrLf = "\r\n";

    /// <summary>The length a content line should not exceed, excluding the line break (RFC 5545 section 3.1).</summary>
    public const int MaxContentLineOctets = 75;

    /// <summary>The date-time forms that denote an instant: a UTC value or a value with an explicit offset.</summary>
    private static readonly string[] InstantDateTimeFormats =
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
    ];

    /// <summary>The date-time forms that denote a wall-clock reading: a floating date-time or a date (RFC 5545 sections 3.3.4 and 3.3.5).</summary>
    private static readonly string[] FloatingDateTimeFormats =
    [
        "yyyyMMddTHHmmss",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyyMMddTHHmm",
        "yyyy-MM-ddTHH:mm",
        "yyyyMMddTHH",
        "yyyy-MM-ddTHH",
        "yyyyMMdd",
    ];

    /// <summary>Writes a content line, folding it so no physical line exceeds <see cref="MaxContentLineOctets"/> UTF-8 octets.</summary>
    /// <remarks>
    /// A fold is a CRLF followed by a single space, which the reader removes. It is only inserted between two characters, so it
    /// never splits the UTF-8 sequence of a character, nor a surrogate pair.
    /// </remarks>
    public static void WriteLine(TextWriter writer, string value)
    {
        if (Encoding.UTF8.GetByteCount(value) <= MaxContentLineOctets)
        {
            writer.Write(value);
            writer.Write(CrLf);
            return;
        }

        var octets = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var isSurrogatePair = char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]);
            var size = GetUtf8Length(c, isSurrogatePair);
            if (octets + size > MaxContentLineOctets)
            {
                writer.Write(CrLf);
                writer.Write(' ');
                octets = 1;
            }

            writer.Write(c);
            if (isSurrogatePair)
            {
                i++;
                writer.Write(value[i]);
            }

            octets += size;
        }

        writer.Write(CrLf);
    }

    private static int GetUtf8Length(char c, bool isSurrogatePair)
    {
        if (isSurrogatePair)
            return 4;

        return c switch
        {
            < (char)0x80 => 1,
            < (char)0x800 => 2,

            // A lone surrogate is encoded as the 3-octet replacement character.
            _ => 3,
        };
    }

    /// <summary>Escapes an iCalendar TEXT value per RFC 5545 section 3.3.11.</summary>
    /// <remarks>
    /// A line feed is escaped as <c>\n</c> and a carriage return is dropped, so a CRLF becomes a single <c>\n</c>. TEXT cannot
    /// hold any other control character but a horizontal tab, so the others are dropped as well.
    /// </remarks>
    public static string EscapeText(string? value)
    {
        if (value is null)
            return "";

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case ';':
                    sb.Append("\\;");
                    break;
                case ',':
                    sb.Append("\\,");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\t':
                    sb.Append(c);
                    break;
                case < (char)0x20 or (char)0x7F:
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>Parses a date or a date-time value.</summary>
    /// <remarks>
    /// A UTC value, or one carrying an offset, denotes an instant and is returned as a <see cref="DateTimeKind.Utc"/> value.
    /// A floating date-time or a date denotes a wall-clock reading and is returned as a <see cref="DateTimeKind.Unspecified"/>
    /// value, a date being read as its first instant.
    /// </remarks>
    public static bool TryParseDateTime(string str, out DateTime result)
    {
        if (DateTime.TryParseExact(str, InstantDateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out result))
            return true;

        return DateTime.TryParseExact(str, FloatingDateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }

    /// <summary>Formats the UNTIL value of a recurrence rule: a DATE value when it was parsed as one, a date-time otherwise.</summary>
    public static string EndDateToString(DateTime value, bool isDate)
    {
        return isDate ? value.ToString("yyyyMMdd", CultureInfo.InvariantCulture) : DateTimeToString(value);
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
            DateTimeKind.Local => LocalToUniversalTime(dt).ToString(UtcDateTimeFormat, CultureInfo.InvariantCulture),

            // Form 1: a floating date-time, interpreted in the reader's own time zone.
            _ => dt.ToString(FloatingDateTimeFormat, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Converts a wall-clock date-time to an instant in <paramref name="timeZone"/> using the disambiguation rules of RFC 5545 section 3.3.5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The instant is outside the range of <see cref="DateTimeOffset"/>.</exception>
    public static DateTimeOffset ToDateTimeOffset(DateTime wallClock, TimeZoneInfo timeZone)
    {
        if (TryToDateTimeOffset(wallClock, timeZone, out var result) is not InstantConversion.Success)
            throw new ArgumentOutOfRangeException(nameof(wallClock), wallClock, "The instant the wall-clock time denotes in the time zone is outside the range of DateTimeOffset.");

        return result;
    }

    /// <summary>Converts a wall-clock date-time to an instant in <paramref name="timeZone"/> using the disambiguation rules of RFC 5545 section 3.3.5.</summary>
    /// <returns>Whether the instant, and its reading in <paramref name="timeZone"/>, are within the range of <see cref="DateTimeOffset"/>, and on which side they fall otherwise.</returns>
    public static InstantConversion TryToDateTimeOffset(DateTime wallClock, TimeZoneInfo timeZone, out DateTimeOffset result)
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

            return Create(local.Ticks, local.Ticks - offset.Ticks, offset, out result);
        }

        if (timeZone.IsInvalidTime(local))
        {
            // The local time is skipped by a forward transition. RFC 5545 reads it with the UTC offset in effect
            // before the gap; rendering that instant in the time zone surfaces 02:30 EST as 03:30 EDT.
            var utcTicks = local.Ticks - GetUtcOffsetBeforeGap(local, timeZone).Ticks;
            if (!IsInRange(utcTicks))
                return Fail(utcTicks, out result);

            var offsetAfterGap = timeZone.GetUtcOffset(new DateTime(utcTicks, DateTimeKind.Utc));
            return Create(utcTicks + offsetAfterGap.Ticks, utcTicks, offsetAfterGap, out result);
        }

        var localOffset = timeZone.GetUtcOffset(local);
        return Create(local.Ticks, local.Ticks - localOffset.Ticks, localOffset, out result);

        static InstantConversion Create(long localTicks, long utcTicks, TimeSpan offset, out DateTimeOffset result)
        {
            if (!IsInRange(utcTicks))
                return Fail(utcTicks, out result);

            if (!IsInRange(localTicks))
                return Fail(localTicks, out result);

            result = new DateTimeOffset(localTicks, offset);
            return InstantConversion.Success;
        }

        static InstantConversion Fail(long ticks, out DateTimeOffset result)
        {
            result = default;
            return ticks < 0 ? InstantConversion.BeforeMinValue : InstantConversion.AfterMaxValue;
        }
    }

    /// <summary>Converts wall-clock occurrences to the instants they denote in <paramref name="timeZone"/>, in increasing order and without duplicates.</summary>
    /// <param name="wallClockOccurrences">The occurrences as wall-clock times, in increasing order.</param>
    /// <param name="timeZone">The time zone the occurrences are expressed in.</param>
    /// <param name="maxCount">The number of instances after which the enumeration ends, as the COUNT rule part does.</param>
    /// <param name="isAfterEnd">Whether an instance, given as its wall-clock time and its instant, is past the end of the recurrence, as the UNTIL rule part does.</param>
    /// <remarks>
    /// <para>A wall-clock time inside the gap of a forward transition is read at the UTC offset in effect before the gap
    /// (RFC 5545 section 3.3.5), which moves it forward by the length of the gap. Such an instance can therefore denote an
    /// instant later than the instances of the wall-clock times that follow it, or the very same instant as one of them.
    /// Only the latter is a duplicate, which RFC 5545 section 3.8.5.3 ignores; the former is a distinct instance.</para>
    /// <para>A moved instance is held back until it can no longer be preceded. Every other wall-clock time maps to its
    /// instants in the same order, so an instance can only precede a moved instant when its wall-clock time is before the
    /// wall-clock reading of that instant, which is later than the time the gap skipped by the length of the gap. Once a
    /// wall-clock time at or after that reading is produced, the moved instance is released. This bound comes from the
    /// transition itself, so it holds for a gap of any length, and nothing is held back when no gap is crossed.</para>
    /// <para>The instances are counted towards <paramref name="maxCount"/> in the order the wall-clock times are generated,
    /// as RFC 5545 section 3.3.10 counts them. An instance that duplicates an instant already generated is the same member
    /// of the recurrence set, so it does not count again. An instance past the end is not part of the recurrence set: a
    /// moved one is skipped, as a later wall-clock time can still denote an earlier instant, and any other one ends the
    /// enumeration.</para>
    /// <para>An instant outside the range of <see cref="DateTimeOffset"/> cannot be returned. One before the range is
    /// skipped, but still counts towards <paramref name="maxCount"/>, and one after the range ends the enumeration.</para>
    /// </remarks>
    public static IEnumerable<DateTimeOffset> ToDateTimeOffsets(IEnumerable<DateTime> wallClockOccurrences, TimeZoneInfo timeZone, int? maxCount = null, Func<DateTime, DateTimeOffset, bool>? isAfterEnd = null)
    {
        if (maxCount <= 0)
            yield break;

        // The moved instances not returned yet, in increasing order since they come from increasing wall-clock times
        Queue<DateTimeOffset>? movedOccurrences = null;
        long? lastReturnedUtcTicks = null;
        long? lastMovedUtcTicks = null;
        var count = 0;
        foreach (var wallClock in wallClockOccurrences)
        {
            var conversion = TryToDateTimeOffset(wallClock, timeZone, out var occurrence);
            if (conversion is InstantConversion.AfterMaxValue)
                break;

            if (conversion is InstantConversion.BeforeMinValue)
            {
                count++;
                if (count >= maxCount)
                    break;

                continue;
            }

            // No wall-clock time from this one on can denote an instant before these ones
            while (movedOccurrences is { Count: > 0 } && movedOccurrences.Peek().DateTime <= wallClock)
            {
                var movedOccurrence = movedOccurrences.Dequeue();
                lastReturnedUtcTicks = movedOccurrence.UtcTicks;
                yield return movedOccurrence;
            }

            var isMoved = occurrence.DateTime != wallClock;
            if (isAfterEnd is not null && isAfterEnd(wallClock, occurrence))
            {
                if (isMoved)
                    continue;

                break;
            }

            if (occurrence.UtcTicks == lastReturnedUtcTicks || occurrence.UtcTicks == lastMovedUtcTicks)
                continue;

            count++;
            if (isMoved)
            {
                movedOccurrences ??= new Queue<DateTimeOffset>();
                movedOccurrences.Enqueue(occurrence);
                lastMovedUtcTicks = occurrence.UtcTicks;
            }
            else
            {
                // A held-back instance was not released, so it is later than this one
                lastReturnedUtcTicks = occurrence.UtcTicks;
                yield return occurrence;
            }

            if (count >= maxCount)
                break;
        }

        if (movedOccurrences is not null)
        {
            while (movedOccurrences.Count > 0)
            {
                yield return movedOccurrences.Dequeue();
            }
        }
    }

    /// <summary>Gets the occurrences that are not before <paramref name="start"/>.</summary>
    /// <param name="occurrences">The occurrences, in increasing order.</param>
    /// <param name="start">The first instant to return occurrences from.</param>
    public static IEnumerable<DateTimeOffset> SkipBefore(IEnumerable<DateTimeOffset> occurrences, DateTimeOffset start)
    {
        var isStartReached = false;
        foreach (var occurrence in occurrences)
        {
            if (!isStartReached)
            {
                if (occurrence.UtcTicks < start.UtcTicks)
                    continue;

                isStartReached = true;
            }

            yield return occurrence;
        }
    }

    /// <summary>Gets the wall-clock reading of <paramref name="instant"/> in <paramref name="timeZone"/>, clamped to the range of <see cref="DateTime"/>.</summary>
    public static DateTime ToWallClockClamped(DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        var offset = timeZone.GetUtcOffset(instant.UtcDateTime);
        return new DateTime(ClampTicks(instant.UtcTicks + offset.Ticks), DateTimeKind.Unspecified);
    }

    /// <summary>Gets the earliest wall-clock time whose occurrence can denote an instant at or after <paramref name="instant"/> in <paramref name="timeZone"/>.</summary>
    /// <remarks>
    /// This is the wall-clock reading of <paramref name="instant"/>, unless it follows a forward transition by less than the
    /// length of the gap. A wall-clock time inside that gap is read at the offset in effect before it (RFC 5545 section 3.3.5),
    /// so it can denote an instant after <paramref name="instant"/> although it is before its reading. The offset one day
    /// earlier is the offset before such a gap, as no time zone skips more than a day.
    /// </remarks>
    public static DateTime GetEarliestWallClock(DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        var offset = timeZone.GetUtcOffset(instant.UtcDateTime);
        var offsetOneDayEarlier = timeZone.GetUtcOffset(new DateTime(ClampTicks(instant.UtcTicks - TimeSpan.TicksPerDay), DateTimeKind.Utc));
        if (offsetOneDayEarlier < offset)
        {
            offset = offsetOneDayEarlier;
        }

        return new DateTime(ClampTicks(instant.UtcTicks + offset.Ticks), DateTimeKind.Unspecified);
    }

    private static bool IsInRange(long ticks) => ticks >= 0 && ticks <= DateTime.MaxValue.Ticks;

    private static long ClampTicks(long ticks)
    {
        if (ticks < 0)
            return 0;

        if (ticks > DateTime.MaxValue.Ticks)
            return DateTime.MaxValue.Ticks;

        return ticks;
    }

    /// <summary>Gets the UTC offset in effect immediately before the forward transition that skips <paramref name="local"/>.</summary>
    private static TimeSpan GetUtcOffsetBeforeGap(DateTime local, TimeZoneInfo timeZone)
    {
        // A gap exists when the offset grows from o1 to o2 (o1 < o2) at instant T, skipping the local times in
        // [T + o1, T + o2). Reading the local time with o1 lands at or after T and so reports o2, and reading it
        // with o2 lands before T and so reports o1. Two probes therefore yield both offsets, and the smaller one
        // is the offset in effect before the gap.
        var first = timeZone.GetUtcOffset(new DateTime(ClampTicks(local.Ticks - timeZone.BaseUtcOffset.Ticks), DateTimeKind.Utc));
        var second = timeZone.GetUtcOffset(new DateTime(ClampTicks(local.Ticks - first.Ticks), DateTimeKind.Utc));
        return first < second ? first : second;
    }

    /// <summary>Expresses <paramref name="value"/> as a wall-clock reading in <paramref name="timeZone"/>.</summary>
    public static DateTime ToWallClock(DateTime value, TimeZoneInfo timeZone)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => TimeZoneInfo.ConvertTimeFromUtc(value, timeZone),
            DateTimeKind.Local => TimeZoneInfo.ConvertTimeFromUtc(LocalToUniversalTime(value), timeZone),
            _ => value,
        };
    }

    /// <summary>Converts a <see cref="DateTimeKind.Local"/> value to UTC, clamped to the range of <see cref="DateTime"/>.</summary>
    /// <remarks>
    /// A local time skipped by a forward transition is read with the UTC offset in effect before the transition, as RFC 5545
    /// section 3.3.5 reads a wall clock: <see cref="TimeZoneInfo.ConvertTime(DateTime, TimeZoneInfo, TimeZoneInfo)"/> throws for
    /// it, and <see cref="DateTime.ToUniversalTime"/> does not apply that offset on every platform. Any other value keeps the
    /// conversion of <see cref="DateTime.ToUniversalTime"/>, which knows which occurrence an ambiguous local time is.
    /// </remarks>
    public static DateTime LocalToUniversalTime(DateTime value)
    {
        var local = TimeZoneInfo.Local;
        if (!local.IsInvalidTime(DateTime.SpecifyKind(value, DateTimeKind.Unspecified)))
            return value.ToUniversalTime();

        return TryToDateTimeOffset(value, local, out var instant) switch
        {
            InstantConversion.Success => instant.UtcDateTime,
            InstantConversion.BeforeMinValue => DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc),
            _ => DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc),
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

    /// <summary>Formats a date-time property that RFC 5545 requires in UTC, such as DTSTAMP (section 3.8.7.2).</summary>
    /// <remarks>A <see cref="DateTimeKind.Local"/> value is converted to UTC, and a <see cref="DateTimeKind.Unspecified"/> one is taken as UTC.</remarks>
    public static string UtcDateTimeToString(DateTime value)
    {
        var utc = value.Kind is DateTimeKind.Local ? LocalToUniversalTime(value) : value;
        return utc.ToString(UtcDateTimeFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>A TZID is written as a property parameter value, a paramtext or a quoted-string (RFC 5545 section 3.1) whose DQUOTE
    /// characters are encoded as RFC 6868 requires, and as a TEXT property value in the VTIMEZONE component. Neither can hold a
    /// control character other than a tab, so an identifier containing one is rejected rather than altered.</summary>
    public static bool IsValidTimeZoneId([NotNullWhen(returnValue: true)] string? id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        foreach (var c in id)
        {
            if ((c < 0x20 && c is not '\t') || c == 0x7F)
                return false;
        }

        return true;
    }

    /// <summary>Formats a TZID as a property parameter value, quoting it when it contains a separator, as a Windows display
    /// name such as <c>(UTC+01:00) Amsterdam, Berlin</c> does, and applying the RFC 6868 encoding.</summary>
    public static string TimeZoneIdToParameterValue(string id)
    {
        return InternetCalendarProperty.EncodeSingleParameterValue(id);
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
