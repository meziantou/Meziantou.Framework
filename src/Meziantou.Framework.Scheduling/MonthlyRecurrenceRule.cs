namespace Meziantou.Framework.Scheduling;

/// <summary>Represents a monthly recurrence rule.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Scheduling.Tests/RecurrenceRuleTests.cs" ref:region="MonthlyRecurrenceRule_Example" ref:dedent="true">
/// // Last day of each month
/// var rrule = RecurrenceRule.Parse("FREQ=MONTHLY;BYMONTHDAY=-1");
/// var nextOccurrences = rrule.GetNextOccurrences(new DateTime(2024, 01, 15, 09, 00, 00)).Take(3).ToArray();
///
/// Assert.Equal(
///     [
///         new DateTime(2024, 01, 31, 09, 00, 00),
///         new DateTime(2024, 02, 29, 09, 00, 00),
///         new DateTime(2024, 03, 31, 09, 00, 00),
///     ],
///     nextOccurrences);
/// </code>
/// </example>
internal sealed class MonthlyRecurrenceRule : RecurrenceRule
{
    /// <summary>Limits occurrences to specific days of the week with optional ordinal positions.</summary>
    public IList<ByDay> ByWeekDays { get; set; } = [];

    protected override IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate)
    {
        return GetNextOccurrencesInternal(startDate, endBound: null);
    }

    private protected override IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate, DateTime? endBound)
    {
        return RecurrenceRuleEvaluator.Evaluate(Frequency.Monthly, this, startDate, endBound, byDays: ByWeekDays, months: ByMonths, monthDays: ByMonthDays);
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
                sb.AppendJoin(',', ByWeekDays);
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

            AppendExtensionParts(sb);
            return sb.ToString();
        }
    }
}
