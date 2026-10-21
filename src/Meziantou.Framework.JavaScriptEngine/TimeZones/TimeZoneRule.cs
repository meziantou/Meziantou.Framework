using System.Runtime.InteropServices;
namespace Meziantou.Framework.JavaScriptEngine.TimeZones;

/// <summary>How the day of a rule is given (the ON field of a tz Rule line).</summary>
internal enum TimeZoneRuleDayKind : byte
{
    /// <summary>A day of the month ("5").</summary>
    DayOfMonth,

    /// <summary>The last given weekday of the month ("lastSun").</summary>
    LastWeekday,

    /// <summary>The first given weekday on or after a day ("Sun&gt;=8").</summary>
    WeekdayOnOrAfter,

    /// <summary>The last given weekday on or before a day ("Sun&lt;=25").</summary>
    WeekdayOnOrBefore,
}

/// <summary>The clock of a time of day in the tz source (the w, s and u suffixes).</summary>
internal enum TimeZoneTimeKind : byte
{
    /// <summary>The local wall clock (standard time plus the daylight saving in effect).</summary>
    Wall,

    /// <summary>The local standard time.</summary>
    Standard,

    /// <summary>Universal time.</summary>
    Universal,
}

/// <summary>A rule that repeats every year (a tz Rule line whose TO field is "max").</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TimeZoneRuleEntry(byte Month, TimeZoneRuleDayKind DayKind, byte Day, byte Weekday, int AtSeconds, TimeZoneTimeKind AtKind, int SaveSeconds, int TypeIndex)
{
    /// <summary>Gets the local date and time of the rule in a year, in seconds since 1970-01-01 (without any offset).</summary>
    public long GetLocalSeconds(long year)
    {
        long days;
        switch (DayKind)
        {
            case TimeZoneRuleDayKind.DayOfMonth:
                days = CalendarMath.DaysFromCivil(year, Month, Day);
                break;

            case TimeZoneRuleDayKind.LastWeekday:
                days = CalendarMath.DaysFromCivil(year, Month, CalendarMath.DaysInMonth(year, Month));
                days -= CalendarMath.FloorModulo(CalendarMath.DayOfWeek(days) - Weekday, 7);
                break;

            case TimeZoneRuleDayKind.WeekdayOnOrAfter:
                days = CalendarMath.DaysFromCivil(year, Month, Day);
                days += CalendarMath.FloorModulo(Weekday - CalendarMath.DayOfWeek(days), 7);
                break;

            default:
                days = CalendarMath.DaysFromCivil(year, Month, Day);
                days -= CalendarMath.FloorModulo(CalendarMath.DayOfWeek(days) - Weekday, 7);
                break;
        }

        return (days * CalendarMath.SecondsPerDay) + AtSeconds;
    }
}

/// <summary>
/// The rules that apply every year after the last explicit transition of a zone (like the TZ string of a TZif file,
/// but with the full expressiveness of the tz rules).
/// </summary>
internal sealed class TimeZoneRecurringRule
{
    public TimeZoneRecurringRule(int firstYear, int standardOffsetSeconds, TimeZoneRuleEntry[] entries)
    {
        FirstYear = firstYear;
        StandardOffsetSeconds = standardOffsetSeconds;
        Entries = entries;
    }

    /// <summary>Gets the first year whose transitions are computed from the rules.</summary>
    public int FirstYear { get; }

    public int StandardOffsetSeconds { get; }

    public TimeZoneRuleEntry[] Entries { get; }

    /// <summary>Computes the transitions of a year, sorted by time.</summary>
    /// <returns>The number of transitions written to <paramref name="times"/> and <paramref name="types"/>.</returns>
    public int GetTransitions(long year, Span<long> times, Span<int> types)
    {
        var entries = Entries;
        var count = entries.Length;
        Span<long> localTimes = stackalloc long[count];
        Span<int> order = stackalloc int[count];

        // The daylight saving in effect at the start of the year is the one of the last rule of the previous year
        var previousSave = 0;
        var latestPrevious = long.MinValue;
        for (var i = 0; i < count; i++)
        {
            var local = entries[i].GetLocalSeconds(year - 1);
            if (local > latestPrevious)
            {
                latestPrevious = local;
                previousSave = entries[i].SaveSeconds;
            }
        }

        for (var i = 0; i < count; i++)
        {
            localTimes[i] = entries[i].GetLocalSeconds(year);
            order[i] = i;
        }

        // Insertion sort: there are usually two rules
        for (var i = 1; i < count; i++)
        {
            var current = order[i];
            var j = i - 1;
            while (j >= 0 && localTimes[order[j]] > localTimes[current])
            {
                order[j + 1] = order[j];
                j--;
            }

            order[j + 1] = current;
        }

        for (var i = 0; i < count; i++)
        {
            var entry = entries[order[i]];
            var time = localTimes[order[i]];
            time -= entry.AtKind switch
            {
                TimeZoneTimeKind.Universal => 0,
                TimeZoneTimeKind.Standard => StandardOffsetSeconds,
                _ => StandardOffsetSeconds + previousSave,
            };

            times[i] = time;
            types[i] = entry.TypeIndex;
            previousSave = entry.SaveSeconds;
        }

        return count;
    }
}
