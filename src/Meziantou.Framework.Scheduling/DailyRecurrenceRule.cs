namespace Meziantou.Framework.Scheduling;

/// <summary>Represents a daily recurrence rule.</summary>
/// <example>
/// <code>
/// var rrule = new DailyRecurrenceRule { Interval = 2, Occurrences = 10 };
/// var nextOccurrences = rrule.GetNextOccurrences(DateTime.Now).ToArray();
/// </code>
/// </example>
internal sealed class DailyRecurrenceRule : RecurrenceRule
{
    /// <summary>Limits occurrences to specific days of the week.</summary>
    public IList<DayOfWeek> ByWeekDays { get; set; } = new List<DayOfWeek>();

    protected override IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate)
    {
        return GetNextOccurrencesInternal(startDate, endBound: null);
    }

    private protected override IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate, DateTime? endBound)
    {
        return RecurrenceRuleEvaluator.Evaluate(Frequency.Daily, this, startDate, endBound, weekDays: ByWeekDays, months: ByMonths, monthDays: ByMonthDays);
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
                sb.Append(EndDateText);
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
                sb.AppendJoin(',', ByMonths);
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
