namespace Meziantou.Framework.Scheduling;

/// <summary>Represents a daily recurrence rule.</summary>
/// <example>
/// <code>
/// var rrule = new DailyRecurrenceRule { Interval = 2, Occurrences = 10 };
/// var nextOccurrences = rrule.GetNextOccurrences(DateTime.Now).ToArray();
/// </code>
/// </example>
public sealed class DailyRecurrenceRule : RecurrenceRule
{
    /// <summary>Limits occurrences to specific months.</summary>
    public IList<Month> ByMonths { get; set; } = new List<Month>();

    /// <summary>Limits occurrences to specific days of the month.</summary>
    public IList<int> ByMonthDays { get; set; } = new List<int>();

    /// <summary>Limits occurrences to specific days of the week.</summary>
    public IList<DayOfWeek> ByWeekDays { get; set; } = new List<DayOfWeek>();

    protected override IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate)
    {
        while (true)
        {
            var b = true;

            if (!IsEmpty(ByMonths))
            {
                if (!ByMonths.Contains((Month)startDate.Month))
                {
                    b = false;
                }
            }

            if (!IsEmpty(ByMonthDays))
            {
                if (!ByMonthDays.Contains(startDate.Day))
                {
                    b = false;
                }
            }

            if (!IsEmpty(ByWeekDays))
            {
                if (!ByWeekDays.Contains(startDate.DayOfWeek))
                {
                    b = false;
                }
            }

            if (b)
            {
                yield return startDate;
            }

            startDate = startDate.AddDays(Interval);
        }

        // ReSharper disable once FunctionNeverReturns (UNTIL & COUNT are handled by GetNextOccurrences)
    }

    /// <inheritdoc />
    public override string Text
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append("FREQ=DAILY");

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

            if (!IsEmpty(BySetPositions))
            {
                sb.Append(";BYSETPOS=");
                sb.AppendJoin(',', BySetPositions);
            }

            return sb.ToString();
        }
    }
}
