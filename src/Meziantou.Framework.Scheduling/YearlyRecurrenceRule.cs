namespace Meziantou.Framework.Scheduling;

/// <summary>Represents a yearly recurrence rule.</summary>
/// <example>
/// <code>
/// var rrule = new YearlyRecurrenceRule { ByMonths = { 1 }, ByMonthDays = { 1 } };
/// var nextOccurrences = rrule.GetNextOccurrences(DateTime.Now).ToArray();
/// </code>
/// </example>
internal sealed class YearlyRecurrenceRule : RecurrenceRule
{
    /// <summary>Limits occurrences to specific days of the month.</summary>
    public new IList<int>? ByMonthDays { get; set; }

    /// <summary>Limits occurrences to specific days of the week with optional ordinal positions.</summary>
    public IList<ByDay>? ByWeekDays { get; set; }

    /// <summary>Limits occurrences to specific months.</summary>
    public new IList<int>? ByMonths { get; set; }

    /// <summary>Limits occurrences to specific weeks of the year (1-53, -53 to -1), numbered as RFC 5545 defines using <see cref="RecurrenceRule.WeekStart"/>.</summary>
    public IList<int>? ByWeekNumbers { get; set; }

    /// <summary>Limits occurrences to specific days of the year (1-366).</summary>
    public IList<int>? ByYearDays { get; set; }

    protected override IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate)
    {
        return GetNextOccurrencesInternal(startDate, endBound: null);
    }

    private protected override IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate, DateTime? endBound)
    {
        return RecurrenceRuleEvaluator.Evaluate(Frequency.Yearly, this, startDate, endBound, byDays: ByWeekDays, months: ByMonths, monthDays: ByMonthDays, yearDays: ByYearDays, weekNumbers: ByWeekNumbers);
    }

    /// <inheritdoc />
    public override string Text
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append("FREQ=YEARLY");

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
                sb.AppendJoin(',', ByMonths);
            }

            if (!IsEmpty(ByWeekNumbers))
            {
                sb.Append(";BYWEEKNO=");
                sb.AppendJoin(',', ByWeekNumbers);
            }

            if (!IsEmpty(ByYearDays))
            {
                sb.Append(";BYYEARDAY=");
                sb.AppendJoin(',', ByYearDays);
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
