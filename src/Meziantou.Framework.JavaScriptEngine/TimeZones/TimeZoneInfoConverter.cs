using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine.TimeZones;

/// <summary>
/// Converts a <see cref="TimeZoneInfo"/> whose identifier is not in the time zone database (such as a zone created by
/// <see cref="TimeZoneInfo.CreateCustomTimeZone(string, TimeSpan, string, string)"/>) to a zone of the engine. A zone
/// without daylight saving time is an offset time zone ("+05:00"). Otherwise, the transitions of the adjustment rules are
/// read from the <see cref="TimeZoneInfo"/>, and the last rule, when it never ends, repeats every year.
/// </summary>
internal static class TimeZoneInfoConverter
{
    private const long SecondsPerDay = CalendarMath.SecondsPerDay;

    /// <summary>The range of the transitions read from a zone whose last rule cannot be expressed as a recurring rule.</summary>
    private const int FallbackFirstYear = 1900;

    private const int FallbackLastYear = 2200;

    private static readonly ConditionalWeakTable<TimeZoneInfo, NamedTimeZone> Cache = [];

    /// <summary>Gets the zone of a <see cref="TimeZoneInfo"/>; the result is cached for the lifetime of the instance.</summary>
    public static TimeZoneIdentifier Convert(TimeZoneInfo timeZone)
    {
        var zone = Cache.GetValue(timeZone, CreateZone);
        return new TimeZoneIdentifier(zone.Id, zone.Id, zone);
    }

    /// <summary>Gets whether an identifier is the one of an offset time zone created by <see cref="Convert"/>.</summary>
    public static bool IsOffsetIdentifier(string identifier) => identifier is ['+' or '-', ..];

    private static NamedTimeZone CreateZone(TimeZoneInfo timeZone)
    {
        var rules = timeZone.GetAdjustmentRules();
        var baseOffset = ToSeconds(timeZone.BaseUtcOffset);
        if (!timeZone.SupportsDaylightSavingTime || rules.Length == 0)
            return new NamedTimeZone(FormatOffset(baseOffset), [new TimeZoneLocalTimeType(baseOffset, IsDaylightSavingTime: false, "")], initialType: 0, [], [], rule: null);

        var builder = new Builder(timeZone);
        var firstYear = Math.Max(DateTime.MinValue.Year + 1, rules[0].DateStart.Year - 1);
        var lastRule = rules[^1];
        TimeZoneRecurringRule? recurringRule = null;
        int lastYear;
        if (lastRule.DateEnd.Year == DateTime.MaxValue.Year)
        {
            var ruleFirstYear = Math.Max(firstYear, lastRule.DateStart.Year) + 1;
            recurringRule = CreateRecurringRule(timeZone, lastRule, ruleFirstYear, builder);
            if (recurringRule is null)
            {
                firstYear = Math.Max(firstYear, FallbackFirstYear);
                lastYear = Math.Max(FallbackLastYear, ruleFirstYear);
            }
            else
            {
                lastYear = ruleFirstYear - 1;
            }
        }
        else
        {
            lastYear = Math.Min(DateTime.MaxValue.Year - 1, lastRule.DateEnd.Year + 1);
        }

        var start = DaysFromYear(firstYear) * SecondsPerDay;
        var end = DaysFromYear(lastYear + 1) * SecondsPerDay;
        var initialType = builder.GetTypeIndex(start);
        var times = new List<long>();
        var transitionTypes = new List<byte>();
        var previousOffset = builder.GetOffset(start);
        for (var time = start + SecondsPerDay; time <= end; time += SecondsPerDay)
        {
            var offset = builder.GetOffset(time);
            if (offset == previousOffset)
                continue;

            // The transition is the first second of the day whose offset is the new one
            var low = time - SecondsPerDay;
            var high = time;
            while (high - low > 1)
            {
                var middle = low + ((high - low) / 2);
                if (builder.GetOffset(middle) == previousOffset)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            times.Add(high);
            transitionTypes.Add(checked((byte)builder.GetTypeIndex(high)));
            previousOffset = offset;
        }

        return new NamedTimeZone(timeZone.Id, [.. builder.Types], initialType, [.. times], [.. transitionTypes], recurringRule);
    }

    /// <summary>Expresses an adjustment rule that never ends as a recurring rule, or returns null when the rule does not give the offsets of the zone.</summary>
    private static TimeZoneRecurringRule? CreateRecurringRule(TimeZoneInfo timeZone, TimeZoneInfo.AdjustmentRule rule, int firstYear, Builder builder)
    {
        if (rule.DaylightDelta == TimeSpan.Zero || firstYear + 2 > DateTime.MaxValue.Year)
            return null;

        var standardOffset = ToSeconds(timeZone.BaseUtcOffset + rule.BaseUtcOffsetDelta);
        var save = ToSeconds(rule.DaylightDelta);
        var daylightType = builder.GetTypeIndex(standardOffset + save, isDaylightSavingTime: true);
        var standardType = builder.GetTypeIndex(standardOffset, isDaylightSavingTime: false);
        if (!TryCreateEntry(rule.DaylightTransitionStart, save, daylightType, out var startEntry) || !TryCreateEntry(rule.DaylightTransitionEnd, saveSeconds: 0, standardType, out var endEntry))
            return null;

        var result = new TimeZoneRecurringRule(firstYear, standardOffset, [startEntry, endEntry]);

        // Check the rule against the TimeZoneInfo, which knows the exact semantics of its adjustment rules
        Span<long> times = stackalloc long[2];
        Span<int> types = stackalloc int[2];
        for (var year = firstYear; year <= firstYear + 1; year++)
        {
            var count = result.GetTransitions(year, times, types);
            for (var i = 0; i < count; i++)
            {
                var expectedBefore = i == 0 ? (types[1] == daylightType ? standardOffset + save : standardOffset) : builder.Types[types[i - 1]].OffsetSeconds;
                if (builder.GetOffset(times[i]) != builder.Types[types[i]].OffsetSeconds || builder.GetOffset(times[i] - 1) != expectedBefore)
                    return null;
            }
        }

        return result;
    }

    private static bool TryCreateEntry(TimeZoneInfo.TransitionTime transition, int saveSeconds, int typeIndex, out TimeZoneRuleEntry entry)
    {
        // A transition at a fractional second (such as 23:59:59.999) happens at the next second
        var atSeconds = (int)((transition.TimeOfDay.TimeOfDay.Ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond);
        var month = (byte)transition.Month;
        var weekday = (byte)transition.DayOfWeek;

        // The times of the adjustment rules are the local wall clock times
        entry = transition switch
        {
            { IsFixedDateRule: true } => new TimeZoneRuleEntry(month, TimeZoneRuleDayKind.DayOfMonth, (byte)transition.Day, Weekday: 0, atSeconds, TimeZoneTimeKind.Wall, saveSeconds, typeIndex),
            { Week: >= 5 } => new TimeZoneRuleEntry(month, TimeZoneRuleDayKind.LastWeekday, Day: 0, weekday, atSeconds, TimeZoneTimeKind.Wall, saveSeconds, typeIndex),
            _ => new TimeZoneRuleEntry(month, TimeZoneRuleDayKind.WeekdayOnOrAfter, (byte)(((transition.Week - 1) * 7) + 1), weekday, atSeconds, TimeZoneTimeKind.Wall, saveSeconds, typeIndex),
        };

        return transition.Month is >= 1 and <= 12;
    }

    private static long DaysFromYear(int year) => CalendarMath.DaysFromCivil(year, 1, 1);

    private static int ToSeconds(TimeSpan value) => (int)(value.Ticks / TimeSpan.TicksPerSecond);

    private static string FormatOffset(int offsetSeconds)
    {
        var minutes = Math.Abs(offsetSeconds) / 60;
        return string.Create(CultureInfo.InvariantCulture, $"{(offsetSeconds < 0 ? '-' : '+')}{minutes / 60:00}:{minutes % 60:00}");
    }

    /// <summary>Reads the offsets of a <see cref="TimeZoneInfo"/> and collects the local time types.</summary>
    private sealed class Builder(TimeZoneInfo timeZone)
    {
        public List<TimeZoneLocalTimeType> Types { get; } = [];

        public int GetOffset(long epochSeconds) => ToSeconds(timeZone.GetUtcOffset(DateTimeOffset.FromUnixTimeSeconds(epochSeconds)));

        public int GetTypeIndex(long epochSeconds)
        {
            var instant = DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
            return GetTypeIndex(ToSeconds(timeZone.GetUtcOffset(instant)), timeZone.IsDaylightSavingTime(instant));
        }

        public int GetTypeIndex(int offsetSeconds, bool isDaylightSavingTime)
        {
            for (var i = 0; i < Types.Count; i++)
            {
                if (Types[i].OffsetSeconds == offsetSeconds && Types[i].IsDaylightSavingTime == isDaylightSavingTime)
                    return i;
            }

            Types.Add(new TimeZoneLocalTimeType(offsetSeconds, isDaylightSavingTime, ""));
            return Types.Count - 1;
        }
    }
}
