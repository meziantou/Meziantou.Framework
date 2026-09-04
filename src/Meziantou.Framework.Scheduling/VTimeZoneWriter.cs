namespace Meziantou.Framework.Scheduling;

/// <summary>Writes the VTIMEZONE component (RFC 5545 section 3.6.5) describing a <see cref="TimeZoneInfo"/>.</summary>
internal static class VTimeZoneWriter
{
    public static void Write(TextWriter writer, TimeZoneInfo timeZone, DateTime referenceDate)
    {
        Utilities.WriteLine(writer, "BEGIN:VTIMEZONE");

        // The identifier was validated before any output was written.
        Utilities.WriteLine(writer, "TZID:" + timeZone.Id);

        var rule = SelectRule(timeZone, referenceDate);
        if (rule is null)
        {
            // RFC 5545 section 3.6.5 requires at least one sub-component, so a time zone without daylight
            // saving time is described by a single STANDARD whose FROM and TO offsets are equal.
            var offset = Utilities.UtcOffsetToString(timeZone.BaseUtcOffset);
            Utilities.WriteLine(writer, "BEGIN:STANDARD");
            Utilities.WriteLine(writer, "DTSTART:19700101T000000");
            Utilities.WriteLine(writer, "TZOFFSETFROM:" + offset);
            Utilities.WriteLine(writer, "TZOFFSETTO:" + offset);
            Utilities.WriteLine(writer, "END:STANDARD");
        }
        else
        {
#if NET6_0_OR_GREATER
            var standardOffset = timeZone.BaseUtcOffset + rule.BaseUtcOffsetDelta;
#else
            // TimeZoneInfo.AdjustmentRule.BaseUtcOffsetDelta does not exist before .NET 6.
            var standardOffset = timeZone.BaseUtcOffset;
#endif
            var daylightOffset = standardOffset + rule.DaylightDelta;

            // The sub-components are anchored in the year of the events rather than in the year the rule
            // starts, so a consumer applies the transitions to them instead of only to later occurrences.
            var year = referenceDate.Year < 1970 ? 1970 : referenceDate.Year;

            WriteSubComponent(writer, "DAYLIGHT", rule.DaylightTransitionStart, year, standardOffset, daylightOffset);
            WriteSubComponent(writer, "STANDARD", rule.DaylightTransitionEnd, year, daylightOffset, standardOffset);
        }

        Utilities.WriteLine(writer, "END:VTIMEZONE");
    }

    private static void WriteSubComponent(TextWriter writer, string name, TimeZoneInfo.TransitionTime transition, int year, TimeSpan from, TimeSpan to)
    {
        Utilities.WriteLine(writer, "BEGIN:" + name);

        // The DTSTART of a sub-component is a local time expressed in the TZOFFSETFROM offset, and never carries a TZID.
        Utilities.WriteLine(writer, "DTSTART:" + GetOnset(transition, year).ToString(Utilities.FloatingDateTimeFormat, CultureInfo.InvariantCulture));
        Utilities.WriteLine(writer, "TZOFFSETFROM:" + Utilities.UtcOffsetToString(from));
        Utilities.WriteLine(writer, "TZOFFSETTO:" + Utilities.UtcOffsetToString(to));
        Utilities.WriteLine(writer, "RRULE:" + GetRecurrenceRule(transition));
        Utilities.WriteLine(writer, "END:" + name);
    }

    private static string GetRecurrenceRule(TimeZoneInfo.TransitionTime transition)
    {
        var month = transition.Month.ToString(CultureInfo.InvariantCulture);
        if (transition.IsFixedDateRule)
            return "FREQ=YEARLY;BYMONTH=" + month + ";BYMONTHDAY=" + transition.Day.ToString(CultureInfo.InvariantCulture);

        // TimeZoneInfo.TransitionTime.Week is 1 to 5, where 5 means the last occurrence in the month: the BYDAY ordinal -1.
        var ordinal = transition.Week >= 5 ? "-1" : transition.Week.ToString(CultureInfo.InvariantCulture);
        return "FREQ=YEARLY;BYMONTH=" + month + ";BYDAY=" + ordinal + Utilities.DayOfWeekToString(transition.DayOfWeek);
    }

    private static DateTime GetOnset(TimeZoneInfo.TransitionTime transition, int year)
    {
        DateTime date;
        if (transition.IsFixedDateRule)
        {
            date = new DateTime(year, transition.Month, Math.Min(transition.Day, DateTime.DaysInMonth(year, transition.Month)));
        }
        else
        {
            var firstOfMonth = new DateTime(year, transition.Month, 1);
            var daysToFirstMatch = ((int)transition.DayOfWeek - (int)firstOfMonth.DayOfWeek + 7) % 7;
            date = firstOfMonth.AddDays(daysToFirstMatch + (7 * (transition.Week - 1)));
            if (date.Month != transition.Month)
            {
                // The requested week does not exist in this month, so the last one is used.
                date = date.AddDays(-7);
            }
        }

        return date + transition.TimeOfDay.TimeOfDay;
    }

    private static TimeZoneInfo.AdjustmentRule? SelectRule(TimeZoneInfo timeZone, DateTime referenceDate)
    {
        // Only one rule is written: the runtime exposes the whole time zone history on Unix but usually a
        // single rule on Windows, so writing every rule would make the output vary by platform.
        TimeZoneInfo.AdjustmentRule? applicable = null;
        foreach (var rule in timeZone.GetAdjustmentRules())
        {
            // A period without daylight saving time is modelled as a rule with a zero delta.
            if (rule.DaylightDelta == TimeSpan.Zero)
                continue;

            // A rule that has already ended cannot describe the events. A time zone that once observed
            // daylight saving time and no longer does is left with no rule at all, and so is written as
            // a single STANDARD component.
            if (rule.DateEnd < referenceDate.Date)
                continue;

            applicable ??= rule;

            // The rule is expanded to an open-ended yearly recurrence, so the one to write is the one stating
            // the ongoing pattern. Unix materializes each year as its own fixed-date rule and adds a single
            // open-ended floating rule for the pattern itself; the fixed ones only hold for their own year.
            if (!rule.DaylightTransitionStart.IsFixedDateRule && !rule.DaylightTransitionEnd.IsFixedDateRule)
                return rule;
        }

        return applicable;
    }
}
