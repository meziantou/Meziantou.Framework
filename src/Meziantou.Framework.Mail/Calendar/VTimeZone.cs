using System.Globalization;

namespace Meziantou.Framework.Mail;

/// <summary>The definition of a time zone of an iCalendar object (<c>VTIMEZONE</c>): its observances (<c>STANDARD</c> and <c>DAYLIGHT</c>), with their offsets and the rules of their onsets.</summary>
public sealed class VTimeZone : CalendarComponent
{
    /// <summary>Creates a time zone.</summary>
    public VTimeZone()
        : base("VTIMEZONE")
    {
    }

    /// <summary>Gets or sets the identifier of the time zone (<c>TZID</c>), which the local times refer to.</summary>
    public string? Id
    {
        get => GetProperty("TZID")?.Value;
        set => SetValue("TZID", value);
    }

    /// <summary>Returns the offset from UTC of a local time: the offset of the observance whose last onset is before the time.</summary>
    /// <param name="localTime">The local time.</param>
    /// <returns>The offset, or <see langword="null"/> when the time zone has no observance with an offset.</returns>
    public TimeSpan? GetUtcOffset(DateTime localTime)
    {
        DateTime? bestOnset = null;
        TimeSpan? bestOffset = null;
        TimeSpan? earliestFrom = null;
        DateTime? earliestStart = null;
        foreach (var observance in Components)
        {
            if (observance.Name is not ("STANDARD" or "DAYLIGHT") || ParseOffset(observance.GetProperty("TZOFFSETTO")?.Value) is not { } offsetTo)
            {
                continue;
            }

            if (observance.GetDateTime("DTSTART") is not { } start)
            {
                continue;
            }

            if (earliestStart is null || start.Value < earliestStart)
            {
                earliestStart = start.Value;
                earliestFrom = ParseOffset(observance.GetProperty("TZOFFSETFROM")?.Value) ?? offsetTo;
            }

            var onset = GetLastOnset(observance, start.Value, localTime);
            if (onset is not null && (bestOnset is null || onset > bestOnset))
            {
                bestOnset = onset;
                bestOffset = offsetTo;
            }
        }

        return bestOffset ?? earliestFrom;
    }

    private static DateTime? GetLastOnset(CalendarComponent observance, DateTime start, DateTime localTime)
    {
        if (start > localTime)
        {
            return null;
        }

        DateTime? last = start;
        if (observance.GetProperty("RRULE") is { } rrule && RecurrenceRule.Parse(rrule.Value) is { } rule)
        {
            try
            {
                foreach (var onset in rule.GetOccurrences(DateTime.SpecifyKind(start, DateTimeKind.Unspecified)))
                {
                    if (onset > localTime)
                    {
                        break;
                    }

                    last = onset;
                }
            }
            catch (NotSupportedException)
            {
            }
        }

        foreach (var rdate in observance.GetProperties("RDATE"))
        {
            foreach (var value in rdate.Value.Split(','))
            {
                if (CalendarDateTime.Parse(value) is { } date && date.Value <= localTime && date.Value > last)
                {
                    last = date.Value;
                }
            }
        }

        return last;
    }

    /// <summary>Reads an offset such as <c>+0100</c>, <c>-0500</c> or <c>+053000</c>.</summary>
    internal static TimeSpan? ParseOffset(string? value)
    {
        value = value?.Trim();
        if (value is null || value.Length is not (5 or 7) || value[0] is not ('+' or '-'))
        {
            return null;
        }

        if (!int.TryParse(value.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours) || !int.TryParse(value.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
        {
            return null;
        }

        var seconds = 0;
        if (value.Length == 7 && !int.TryParse(value.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out seconds))
        {
            return null;
        }

        if (hours > 23 || minutes > 59 || seconds > 59)
        {
            return null;
        }

        var offset = new TimeSpan(hours, minutes, seconds);
        return value[0] == '-' ? -offset : offset;
    }
}
