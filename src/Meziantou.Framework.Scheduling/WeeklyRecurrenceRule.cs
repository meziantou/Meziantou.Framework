namespace Meziantou.Framework.Scheduling;

/// <summary>Represents a weekly recurrence rule.</summary>
/// <example>
/// <code>
/// var rrule = new WeeklyRecurrenceRule { Interval = 2, ByWeekDays = { DayOfWeek.Monday, DayOfWeek.Wednesday } };
/// var nextOccurrences = rrule.GetNextOccurrences(DateTime.Now).ToArray();
/// </code>
/// </example>
internal sealed class WeeklyRecurrenceRule : RecurrenceRule
{
    /// <summary>Limits occurrences to specific days of the week.</summary>
    public IList<DayOfWeek> ByWeekDays { get; set; } = new List<DayOfWeek>();

    protected override IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate)
    {
        return GetNextOccurrencesInternal(startDate, endBound: null);
    }

    private protected override IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate, DateTime? endBound)
    {
        return RecurrenceRuleEvaluator.Evaluate(Frequency.Weekly, this, startDate, endBound, weekDays: ByWeekDays, months: ByMonths, monthDays: ByMonthDays);
    }

    /// <inheritdoc />
    public override string Text
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append("FREQ=WEEKLY");

            if (Interval != 1)
            {
                sb.Append(";INTERVAL=");
                sb.Append(Interval.ToString(CultureInfo.InvariantCulture));
            }

            if (EndDate.HasValue)
            {
                sb.Append(";UNTIL=");
                sb.Append(EndDateText);
            }

            if (Occurrences.HasValue)
            {
                sb.Append(";COUNT=");
                sb.Append(Occurrences.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (WeekStart != DefaultFirstDayOfWeek)
            {
                sb.Append(";WKST=");
                sb.Append(Utilities.DayOfWeekToString(WeekStart));
            }

            if (!IsEmpty(ByMonths))
            {
                sb.Append(";BYMONTH=");
                AppendValues(sb, ByMonths);
            }

            if (!IsEmpty(ByMonthDays))
            {
                sb.Append(";BYMONTHDAY=");
                AppendValues(sb, ByMonthDays);
            }

            if (!IsEmpty(ByWeekDays))
            {
                sb.Append(";BYDAY=");
                sb.AppendJoin(',', ByWeekDays.Select(Utilities.DayOfWeekToString));
            }

            if (!IsEmpty(ByHours))
            {
                sb.Append(";BYHOUR=");
                AppendValues(sb, ByHours);
            }

            if (!IsEmpty(ByMinutes))
            {
                sb.Append(";BYMINUTE=");
                AppendValues(sb, ByMinutes);
            }

            if (!IsEmpty(BySeconds))
            {
                sb.Append(";BYSECOND=");
                AppendValues(sb, BySeconds);
            }

            if (!IsEmpty(BySetPositions))
            {
                sb.Append(";BYSETPOS=");
                AppendValues(sb, BySetPositions);
            }

            return sb.ToString();
        }
    }
}
