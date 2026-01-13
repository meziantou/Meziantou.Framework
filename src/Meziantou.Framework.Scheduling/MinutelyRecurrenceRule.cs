namespace Meziantou.Framework.Scheduling;

/// <summary>Represents a minutely recurrence rule.</summary>
/// <example>
/// <code>
/// var rrule = new MinutelyRecurrenceRule { Interval = 15, Occurrences = 10 };
/// var nextOccurrences = rrule.GetNextOccurrences(DateTime.Now).ToArray();
/// </code>
/// </example>
public sealed class MinutelyRecurrenceRule : RecurrenceRule
{
    /// <summary>Limits occurrences to specific days of the week.</summary>
    public IList<DayOfWeek> ByWeekDays { get; set; } = [];

    protected override IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate)
    {
        var hasSecondsFilter = !IsEmpty(BySeconds);
        var current = startDate;

        while (true)
        {
            var matches = true;

            if (!IsEmpty(ByMonths) && !ByMonths.Contains((Month)current.Month))
                matches = false;

            if (!IsEmpty(ByMonthDays) && !ByMonthDays.Contains(current.Day))
                matches = false;

            if (!IsEmpty(ByWeekDays) && !ByWeekDays.Contains(current.DayOfWeek))
                matches = false;

            if (!IsEmpty(ByHours) && !ByHours.Contains(current.Hour))
                matches = false;

            if (matches)
            {
                if (hasSecondsFilter)
                {
                    foreach (var second in BySeconds)
                    {
                        var result = new DateTime(current.Year, current.Month, current.Day, current.Hour, current.Minute, second, current.Kind);
                        if (result >= startDate)
                        {
                            yield return result;
                        }
                    }
                }
                else
                {
                    yield return current;
                }
            }

            current = current.AddMinutes(Interval);
        }
    }

    /// <inheritdoc />
    public override string Text
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append("FREQ=MINUTELY");

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

            if (!IsEmpty(ByHours))
            {
                sb.Append(";BYHOUR=");
                sb.AppendJoin(',', ByHours);
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
