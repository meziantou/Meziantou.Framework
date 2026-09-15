namespace Meziantou.Framework.Scheduling;

internal sealed class RecurrenceRuleHumanizerEnglish : RecurrenceRuleHumanizer
{
    protected override string GetText(RuleParts rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var sb = new StringBuilder();
        AppendInterval(sb, rule);
        var daySetPositions = GetDaySetPositions(rule);
        AppendDays(sb, rule, daySetPositions?.DayPositions);
        if (daySetPositions is not null)
        {
            AppendTimes(sb, daySetPositions.Hours, daySetPositions.Minutes, daySetPositions.Seconds);
        }
        else
        {
            AppendTimes(sb, rule.ByHours, rule.ByMinutes, rule.BySeconds);
            if (rule.BySetPositions.Count > 0)
            {
                sb.Append(", only the ");
                sb.Append(JoinAnd([.. rule.BySetPositions.Select(GetPositionText)]));
                sb.Append(rule.BySetPositions.Count is 1 ? " occurrence" : " occurrences");
            }
        }

        AppendEnd(sb, rule);
        if (IsWeekStartSignificant(rule))
        {
            sb.Append(", with weeks starting on ");
            sb.Append(DayOfWeekToString(rule.WeekStart));
        }

        return sb.ToString();
    }

    private static void AppendInterval(StringBuilder sb, RuleParts rule)
    {
        var unit = rule.Frequency switch
        {
            Frequency.Secondly => "second",
            Frequency.Minutely => "minute",
            Frequency.Hourly => "hour",
            Frequency.Daily => "day",
            Frequency.Weekly => "week",
            Frequency.Monthly => "month",
            _ => "year",
        };

        sb.Append("every ");
        switch (rule.Interval)
        {
            case 1:
                sb.Append(unit);
                break;

            case 2:
                sb.Append("other ").Append(unit);
                break;

            default:
                sb.Append(ToInvariantString(rule.Interval)).Append(' ').Append(unit).Append('s');
                break;
        }
    }

    private static void AppendDays(StringBuilder sb, RuleParts rule, IList<int>? daySetPositions)
    {
        var days = rule.ByDays;
        var hasOrdinalDays = rule.HasOrdinalDays;
        var isYearly = rule.Frequency is Frequency.Yearly;
        var monthsRendered = false;
        var positional = false;

        if (rule.ByMonthDays.Count is 0 && rule.ByYearDays.Count is 0)
        {
            if (daySetPositions is not null)
            {
                // "the first and last Monday or Tuesday": BYSETPOS selects among the days matching any of the listed weekdays
                sb.Append(" on the ");
                sb.Append(JoinAnd([.. daySetPositions.Select(GetPositionText)]));
                sb.Append(' ');
                sb.Append(GetDayNames(rule.DaysOfWeek, " or ", abbreviate: true, plural: false));
                positional = true;
            }
            else if (rule.ByWeekNumbers.Count > 0)
            {
                // "Monday of week 20": the week numbers select the weeks, and the week days the days of those weeks
                var daysOfWeek = rule.DaysOfWeek;
                sb.Append(" on ");
                if (days.Count is 0 || hasOrdinalDays || IsFullWeek(daysOfWeek))
                {
                    sb.Append("every day");
                }
                else if (IsWeekday(daysOfWeek))
                {
                    sb.Append("weekdays");
                }
                else if (IsWeekendDay(daysOfWeek))
                {
                    sb.Append("weekend days");
                }
                else
                {
                    sb.Append(GetDayNames(daysOfWeek, " and ", abbreviate: false, plural: rule.ByWeekNumbers.Count > 1));
                }

                sb.Append(" of ");
                sb.Append(GetWeekNumbersText(rule.ByWeekNumbers));
                if (hasOrdinalDays)
                {
                    sb.Append(" if it is ");
                    sb.Append(GetDayConditionText(rule));
                }
            }
            else if (days.Count > 0)
            {
                if (hasOrdinalDays)
                {
                    sb.Append(" on ");
                    sb.Append(JoinAnd([.. days.Select(day => day.Ordinal is { } ordinal ? "the " + GetPositionText(ordinal) + " " + DayOfWeekToString(day.DayOfWeek) : "every " + DayOfWeekToString(day.DayOfWeek))]));
                    positional = true;
                }
                else
                {
                    var plural = rule.Frequency is Frequency.Monthly or Frequency.Yearly;
                    sb.Append(" on ");
                    sb.Append(GetDayNames(rule.DaysOfWeek, " and ", abbreviate: plural, plural));
                }
            }
        }
        else
        {
            var monthDaysAllPositive = rule.ByMonthDays.All(day => day > 0);

            // "Friday the 13th"
            var isWeekdayOfMonthDay = days.Count is 1 && !hasOrdinalDays && rule.ByMonthDays.Count > 0 && monthDaysAllPositive && rule.ByYearDays.Count is 0;

            // Every day part restricts the days selected by the other ones, so the first one selects the days and the other ones are conditions
            var conditions = new List<string>();
            sb.Append(" on ");
            if (rule.ByYearDays.Count > 0)
            {
                sb.Append("the ").Append(GetDayNumbersText(rule.ByYearDays, " and ")).Append(" day of the year");
                if (rule.ByMonthDays.Count > 0)
                {
                    conditions.Add("the " + GetDayNumbersText(rule.ByMonthDays, " or ") + (monthDaysAllPositive ? "" : " day") + " of the month");
                }
            }
            else
            {
                var monthDaysText = GetDayNumbersText(rule.ByMonthDays, " and ");
                if (isWeekdayOfMonthDay)
                {
                    sb.Append(DayOfWeekToString(days[0].DayOfWeek)).Append(" the ").Append(monthDaysText);
                }
                else if (isYearly && rule.ByMonths.Count is 1 && monthDaysAllPositive)
                {
                    sb.Append(MonthToString(rule.ByMonths[0])).Append(" the ").Append(monthDaysText);
                    monthsRendered = true;
                }
                else
                {
                    sb.Append("the ").Append(monthDaysText);
                    if (!monthDaysAllPositive)
                    {
                        sb.Append(" day");
                    }

                    if (isYearly)
                    {
                        if (rule.ByMonths.Count > 0)
                        {
                            sb.Append(" of ").Append(GetMonthsText(rule.ByMonths));
                            monthsRendered = true;
                        }
                        else
                        {
                            sb.Append(" of every month");
                        }
                    }
                }
            }

            if (rule.ByWeekNumbers.Count > 0)
            {
                conditions.Add("in " + GetWeekNumbersText(rule.ByWeekNumbers));
            }

            if (days.Count > 0 && !isWeekdayOfMonthDay)
            {
                conditions.Add(GetDayConditionText(rule));
            }

            if (conditions.Count > 0)
            {
                sb.Append(" if it is ");
                sb.Append(JoinAnd(conditions));
            }
        }

        if (!monthsRendered && rule.ByMonths.Count > 0)
        {
            sb.Append(positional && isYearly ? " of " : " in ");
            sb.Append(GetMonthsText(rule.ByMonths));
        }
    }

    private static void AppendTimes(StringBuilder sb, IList<int> hours, IList<int> minutes, IList<int> seconds)
    {
        var times = GetListedTimes(hours, minutes, seconds);
        if (times is not null)
        {
            sb.Append(" at ");
            sb.Append(JoinAnd([.. times.Select(time => FormatTime(time.Hour, time.Minute, time.Second))]));
            return;
        }

        var parts = new List<string>();
        AddTimeComponent(parts, hours, "hour");
        AddTimeComponent(parts, minutes, "minute");
        AddTimeComponent(parts, seconds, "second");
        if (parts.Count > 0)
        {
            sb.Append(" at ");
            sb.Append(string.Join(", ", parts));
        }

        static void AddTimeComponent(List<string> parts, IList<int> values, string unit)
        {
            if (values.Count is 0)
                return;

            parts.Add(unit + (values.Count is 1 ? " " : "s ") + JoinAnd([.. values.Select(ToInvariantString)]));
        }
    }

    private static void AppendEnd(StringBuilder sb, RuleParts rule)
    {
        if (rule.Occurrences is { } occurrences)
        {
            sb.Append(" for ");
            sb.Append(ToInvariantString(occurrences));
            sb.Append(occurrences is 1 ? " time" : " times");
        }

        if (rule.EndDate is { } endDate)
        {
            sb.Append(" until ");
            sb.Append(MonthToString(endDate.Month));
            sb.Append(' ');
            sb.Append(ToInvariantString(endDate.Day));
            sb.Append(", ");
            sb.Append(ToInvariantString(endDate.Year));

            // A DATE-TIME ends the recurrence at its time of day, midnight included, so only a DATE omits it
            if (!rule.IsEndDateDate)
            {
                sb.Append(" at ");
                sb.Append(FormatTime(endDate.Hour, endDate.Minute, endDate.Second is 0 ? null : endDate.Second));
                if (endDate.Kind is DateTimeKind.Utc)
                {
                    sb.Append(" UTC");
                }
            }
        }
    }

    private static string JoinAnd(IList<string> items) => JoinList(items, ", ", " and ");

    private static string JoinOr(IList<string> items) => JoinList(items, ", ", " or ");

    private static string FormatTime(int hour, int minute, int? second)
    {
        var result = ToInvariantString(hour) + ":" + ToTwoDigitString(minute);
        if (second is { } s)
        {
            result += ":" + ToTwoDigitString(s);
        }

        return result;
    }

    private static string GetDayNames(List<DayOfWeek> daysOfWeek, string lastSeparator, bool abbreviate, bool plural)
    {
        if (abbreviate)
        {
            if (IsWeekday(daysOfWeek))
                return plural ? "weekdays" : "weekday";

            if (IsWeekendDay(daysOfWeek))
                return plural ? "weekend days" : "weekend day";

            if (!plural && IsFullWeek(daysOfWeek))
                return "day";
        }

        return JoinList([.. daysOfWeek.Select(day => plural ? DayOfWeekToString(day) + "s" : DayOfWeekToString(day))], ", ", lastSeparator);
    }

    private static string GetDayConditionText(RuleParts rule)
    {
        if (!rule.HasOrdinalDays)
        {
            var daysOfWeek = rule.DaysOfWeek;
            if (IsWeekday(daysOfWeek))
                return "a weekday";

            if (IsWeekendDay(daysOfWeek))
                return "a weekend day";
        }

        // Within a YEARLY rule, a numbered day is counted in the year unless BYMONTH narrows it to the month
        var scope = rule.Frequency is Frequency.Yearly && rule.ByMonths.Count is 0 ? " of the year" : "";
        return JoinOr([.. rule.ByDays.Select(day => day.Ordinal is { } ordinal ? "the " + GetPositionText(ordinal) + " " + DayOfWeekToString(day.DayOfWeek) + scope : "a " + DayOfWeekToString(day.DayOfWeek))]);
    }

    private static string GetMonthsText(IList<int> months) => JoinAnd([.. months.Select(MonthToString)]);

    /// <summary>Gets the text of week numbers such as "week 20", "weeks 1 and 2", or "week 1 and the last week of the year".</summary>
    private static string GetWeekNumbersText(IList<int> weekNumbers)
    {
        if (weekNumbers.All(week => week > 0))
            return (weekNumbers.Count is 1 ? "week " : "weeks ") + JoinAnd([.. weekNumbers.Select(ToInvariantString)]);

        return JoinAnd([.. weekNumbers.Select(week => week > 0 ? "week " + ToInvariantString(week) : "the " + GetPositionText(week) + " week")]) + " of the year";
    }

    /// <summary>Gets the text of day numbers such as "1st and 15th" or "1st and last day" (without the leading article).</summary>
    private static string GetDayNumbersText(IList<int> values, string lastSeparator)
    {
        return JoinList([.. values.Select(value => value > 0 ? GetOrdinalNumber(value) : GetPositionText(value))], ", ", lastSeparator);
    }

    /// <summary>Gets the position text such as "first", "5th", "last", or "second to last".</summary>
    private static string GetPositionText(int position)
    {
        return position switch
        {
            1 => "first",
            2 => "second",
            3 => "third",
            4 => "fourth",
            -1 => "last",
            int.MinValue => ToInvariantString(position),
            < 0 => GetPositionText(-position) + " to last",
            _ => GetOrdinalNumber(position),
        };
    }

    private static string GetOrdinalNumber(int value)
    {
        var suffix = (value % 100) switch
        {
            11 or 12 or 13 => "th",
            _ => (value % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            },
        };

        return ToInvariantString(value) + suffix;
    }

    private static string DayOfWeekToString(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Sunday => "Sunday",
            DayOfWeek.Monday => "Monday",
            DayOfWeek.Tuesday => "Tuesday",
            DayOfWeek.Wednesday => "Wednesday",
            DayOfWeek.Thursday => "Thursday",
            DayOfWeek.Friday => "Friday",
            DayOfWeek.Saturday => "Saturday",
            _ => ToInvariantString((int)dayOfWeek),
        };
    }

    private static string MonthToString(int month)
    {
        return month switch
        {
            1 => "January",
            2 => "February",
            3 => "March",
            4 => "April",
            5 => "May",
            6 => "June",
            7 => "July",
            8 => "August",
            9 => "September",
            10 => "October",
            11 => "November",
            12 => "December",
            _ => ToInvariantString(month),
        };
    }
}
