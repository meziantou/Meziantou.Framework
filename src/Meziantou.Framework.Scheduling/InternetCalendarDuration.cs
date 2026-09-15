namespace Meziantou.Framework.Scheduling;

/// <summary>A positive DURATION value (RFC 5545 section 3.3.6), made of nominal days and of an exact time.</summary>
internal sealed class InternetCalendarDuration
{
    // Beyond this many days, any duration exceeds the range of DateTime.
    private const long MaxDays = 3_652_059;

    private InternetCalendarDuration(string text, int days, TimeSpan time)
    {
        Text = text;
        Days = days;
        Time = time;
    }

    /// <summary>Gets the value as written.</summary>
    public string Text { get; }

    /// <summary>Gets the nominal days of the duration, weeks included.</summary>
    public int Days { get; }

    /// <summary>Gets the exact part of the duration: its hours, minutes and seconds.</summary>
    public TimeSpan Time { get; }

    /// <summary>Parses a dur-value: <c>["+"] "P" (dur-date / dur-time / dur-week)</c>.</summary>
    /// <remarks>A negative duration is rejected, as the duration of an event is positive (RFC 5545 section 3.8.2.5).</remarks>
    public static bool TryParse(string value, [NotNullWhen(returnValue: true)] out InternetCalendarDuration? duration, out string? error)
    {
        duration = null;
        var index = 0;
        if (index < value.Length && value[index] is '+' or '-')
        {
            if (value[index] is '-')
            {
                error = $"The DURATION value '{value}' is negative";
                return false;
            }

            index++;
        }

        if (index >= value.Length || char.ToUpperInvariant(value[index]) is not 'P')
            return Fail(value, out error);

        index++;
        long days = 0;
        long seconds = 0;
        var hasDate = false;
        if (TryReadNumber(value, ref index, out var number))
        {
            switch (index < value.Length ? char.ToUpperInvariant(value[index]) : '\0')
            {
                case 'W':
                    // dur-week has no time part.
                    if (index + 1 != value.Length || number > MaxDays / 7)
                        return Fail(value, out error);

                    duration = new InternetCalendarDuration(value, (int)(number * 7), TimeSpan.Zero);
                    error = null;
                    return true;

                case 'D':
                    if (number > MaxDays)
                        return Fail(value, out error);

                    days = number;
                    hasDate = true;
                    index++;
                    break;

                default:
                    return Fail(value, out error);
            }
        }

        if (index < value.Length)
        {
            if (char.ToUpperInvariant(value[index]) is not 'T')
                return Fail(value, out error);

            index++;

            // dur-time = "T" (dur-hour / dur-minute / dur-second), where each unit may only be followed by a smaller one. A
            // skipped unit, as in PT1H30S, is tolerated.
            var lastUnit = 0;
            var hasTime = false;
            while (index < value.Length)
            {
                if (!TryReadNumber(value, ref index, out number) || index >= value.Length)
                    return Fail(value, out error);

                var (unit, factor) = char.ToUpperInvariant(value[index]) switch
                {
                    'H' => (1, 3600L),
                    'M' => (2, 60L),
                    'S' => (3, 1L),
                    _ => (0, 0L),
                };

                if (unit <= lastUnit || number > MaxDays * 86400 / factor)
                    return Fail(value, out error);

                seconds += number * factor;
                if (seconds > MaxDays * 86400)
                    return Fail(value, out error);

                lastUnit = unit;
                hasTime = true;
                index++;
            }

            if (!hasTime)
                return Fail(value, out error);
        }
        else if (!hasDate)
        {
            return Fail(value, out error);
        }

        duration = new InternetCalendarDuration(value, (int)days, TimeSpan.FromSeconds(seconds));
        error = null;
        return true;
    }

    /// <summary>Computes the end of an event starting at <paramref name="start"/>.</summary>
    /// <remarks>
    /// RFC 5545 section 3.3.6: the days are nominal, so they are added to the wall clock of a start in a time zone, and the time
    /// is exact, so it is added to the instant that wall clock denotes, read as section 3.3.5 requires. The end is expressed
    /// in the frame of the start: a wall clock in its time zone, or a value of the same kind.
    /// </remarks>
    public bool TryGetEnd(DateTime start, bool isAllDay, TimeZoneInfo? timeZone, out DateTime end)
    {
        end = default;
        if (Days > (DateTime.MaxValue - start).TotalDays)
            return false;

        var wallClock = start.AddDays(Days);
        if (isAllDay || Time == TimeSpan.Zero)
        {
            end = wallClock;
            return true;
        }

        if (timeZone is null || start.Kind is not DateTimeKind.Unspecified)
        {
            if (Time > DateTime.MaxValue - wallClock)
                return false;

            end = wallClock + Time;
            return true;
        }

        if (Utilities.TryToDateTimeOffset(wallClock, timeZone, out var instant) is not InstantConversion.Success)
            return false;

        var utc = instant.UtcDateTime;
        if (Time > DateTime.MaxValue - utc)
            return false;

        var result = TimeZoneInfo.ConvertTimeFromUtc(utc + Time, timeZone);
        if (result == DateTime.MaxValue || result == DateTime.MinValue)
            return false;

        end = DateTime.SpecifyKind(result, DateTimeKind.Unspecified);
        return true;
    }

    private static bool TryReadNumber(string value, ref int index, out long number)
    {
        number = 0;
        var start = index;
        while (index < value.Length && char.IsAsciiDigit(value[index]))
        {
            // Capped so a long run of digits cannot overflow; any capped value is rejected as too large.
            number = Math.Min(number * 10 + (value[index] - '0'), long.MaxValue / 100);
            index++;
        }

        return index > start;
    }

    private static bool Fail(string value, out string? error)
    {
        error = $"The DURATION value '{value}' is not a duration";
        return false;
    }
}
