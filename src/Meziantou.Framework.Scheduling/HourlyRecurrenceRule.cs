namespace Meziantou.Framework.Scheduling;

/// <summary>Represents an hourly recurrence rule.</summary>
/// <example>
/// <code>
/// var rrule = new HourlyRecurrenceRule { Interval = 2, Occurrences = 10 };
/// var nextOccurrences = rrule.GetNextOccurrences(DateTime.Now).ToArray();
/// </code>
/// </example>
public sealed class HourlyRecurrenceRule : RecurrenceRule
{
    /// <summary>Limits occurrences to specific days of the week.</summary>
    public IList<DayOfWeek> ByWeekDays { get; set; } = [];

    protected override IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate)
    {
        var hasTimeFilters = !IsEmpty(ByMinutes) || !IsEmpty(BySeconds);
        var current = startDate;

        while (true)
        {
            var matches = true;

            if (!IsEmpty(ByWeekDays) && !ByWeekDays.Contains(current.DayOfWeek))
                matches = false;

            if (matches)
            {
                if (hasTimeFilters)
                {
                    foreach (var occurrence in ExpandByTime(current))
                    {
                        yield return occurrence;
                    }
                }
                else
                {
                    yield return current;
                }
            }

            current = current.AddHours(Interval);
        }

        // ReSharper disable once FunctionNeverReturns (UNTIL & COUNT are handled by GetNextOccurrences)
    }

    private IEnumerable<DateTime> ExpandByTime(DateTime date)
    {
        var minutes = IsEmpty(ByMinutes) ? [date.Minute] : ByMinutes;
        var seconds = IsEmpty(BySeconds) ? [date.Second] : BySeconds;

        var dateHour = new DateTime(date.Year, date.Month, date.Day, date.Hour, 0, 0, date.Kind);
        foreach (var minute in minutes)
        {
            foreach (var second in seconds)
            {
                var result = dateHour.AddMinutes(minute).AddSeconds(second);
                if (result >= date)
                {
                    yield return result;
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
            sb.Append("FREQ=HOURLY");

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
                sb.AppendJoin(',', ByWeekDays.Select(Utilities.DayOfWeekToString));
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
