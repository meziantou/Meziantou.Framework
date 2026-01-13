namespace Meziantou.Framework.Scheduling;

/// <summary>Represents a monthly recurrence rule.</summary>
/// <example>
/// <code>
/// var rrule = new MonthlyRecurrenceRule { ByMonthDays = { -1 } }; // Last day of each month
/// var nextOccurrences = rrule.GetNextOccurrences(DateTime.Now).ToArray();
/// </code>
/// </example>
public sealed class MonthlyRecurrenceRule : RecurrenceRule
{
    /// <summary>Limits occurrences to specific days of the week with optional ordinal positions.</summary>
    public IList<ByDay> ByWeekDays { get; set; } = [];

    protected override IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate)
    {
        var hasTimeFilters = !IsEmpty(ByHours) || !IsEmpty(ByMinutes) || !IsEmpty(BySeconds);

        if (IsEmpty(ByMonthDays) && IsEmpty(ByWeekDays))
        {
            while (true)
            {
                if (hasTimeFilters)
                {
                    foreach (var occurrence in ExpandByTime(startDate))
                    {
                        yield return occurrence;
                    }
                }
                else
                {
                    yield return startDate;
                }
                startDate = startDate.AddMonths(Interval);
            }
        }

        var startOfMonth = Extensions.StartOfMonth(startDate, keepTime: true);
        while (true)
        {
            var b = true;
            if (!IsEmpty(ByMonths))
            {
                if (ByMonths.Contains((Month)startOfMonth.Month))
                {
                    b = false;
                }
            }

            if (b)
            {
                var resultByDays = ResultByWeekDaysInMonth(startOfMonth, ByWeekDays);

                var result = Intersect(resultByDays, Enumerable.Empty<DateTime>());
                result = FilterBySetPosition(result.Distinct().Order().ToArray(), BySetPositions);

                foreach (var date in result.Where(d => d >= startDate))
                {
                    if (hasTimeFilters)
                    {
                        foreach (var occurrence in ExpandByTime(date))
                        {
                            yield return occurrence;
                        }
                    }
                    else
                    {
                        yield return date;
                    }
                }
            }

            startOfMonth = startOfMonth.AddMonths(Interval);
        }

        // ReSharper disable once IteratorNeverReturns
    }

    private IEnumerable<DateTime> ExpandByTime(DateTime date)
    {
        var hours = IsEmpty(ByHours) ? [date.Hour] : ByHours;
        var minutes = IsEmpty(ByMinutes) ? [date.Minute] : ByMinutes;
        var seconds = IsEmpty(BySeconds) ? [date.Second] : BySeconds;

        var dateOnly = date.Date;
        foreach (var hour in hours)
        {
            foreach (var minute in minutes)
            {
                foreach (var second in seconds)
                {
                    var result = dateOnly.AddHours(hour).AddMinutes(minute).AddSeconds(second);
                    if (result >= date)
                    {
                        yield return result;
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public override string Text
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append("FREQ=MONTHLY");

            if (Interval != 1)
            {
                sb.Append(";INTERVAL=");
                sb.Append(Interval);
            }

            if (EndDate.HasValue)
            {
                sb.Append(";UNTIL=");
                sb.Append(Utilities.DateTimeToString(EndDate.Value));
            }

            if (Occurrences.HasValue)
            {
                sb.Append(";COUNT=");
                sb.Append(Occurrences.Value);
            }

            if (WeekStart != DefaultFirstDayOfWeek)
            {
                sb.Append(";WKST=");
                sb.Append(Utilities.DayOfWeekToString(WeekStart));
            }

            if (!IsEmpty(ByMonths))
            {
                sb.Append(";BYMONTH=");
                sb.AppendJoin(',', ByMonths.Cast<int>());
            }

            if (!IsEmpty(ByMonthDays))
            {
                sb.Append(";BYMONTHDAY=");
                sb.AppendJoin(',', ByMonthDays);
            }

            if (!IsEmpty(ByWeekDays))
            {
                sb.Append(";BYDAY=");
                sb.AppendJoin(',', ByWeekDays);
            }

            if (!IsEmpty(ByHours))
            {
                sb.Append(";BYHOUR=");
                sb.AppendJoin(',', ByHours);
            }

            if (!IsEmpty(ByMinutes))
            {
                sb.Append(";BYMINUTE=");
                sb.AppendJoin(',', ByMinutes);
            }

            if (!IsEmpty(BySeconds))
            {
                sb.Append(";BYSECOND=");
                sb.AppendJoin(',', BySeconds);
            }

            if (!IsEmpty(BySetPositions))
            {
                sb.Append(";BYSETPOS=");
                sb.AppendJoin(',', BySetPositions);
            }

            return sb.ToString();
        }
    }
}
