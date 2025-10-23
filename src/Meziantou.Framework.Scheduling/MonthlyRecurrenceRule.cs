namespace Meziantou.Framework.Scheduling;

public sealed class MonthlyRecurrenceRule : RecurrenceRule
{
    public IList<ByDay> ByWeekDays { get; set; } = new List<ByDay>();
    public IList<int> ByMonthDays { get; set; } = new List<int>();
    public IList<Month> ByMonths { get; set; } = new List<Month>();

    protected override IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate)
    {
        if (IsEmpty(ByMonthDays) && IsEmpty(ByWeekDays))
        {
            while (true)
            {
                yield return startDate;
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
                var resultByMonthDays = ResultByMonthDays(startOfMonth, ByMonthDays);
                var resultByDays = ResultByWeekDaysInMonth(startOfMonth, ByWeekDays);

                var result = Intersect(resultByDays, resultByMonthDays);
                result = FilterBySetPosition(result.Distinct().Order().ToArray(), BySetPositions);

                foreach (var date in result.Where(d => d >= startDate))
                {
                    yield return date;
                }
            }

            startOfMonth = startOfMonth.AddMonths(Interval);
        }

        // ReSharper disable once IteratorNeverReturns
    }

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

            if (!IsEmpty(BySetPositions))
            {
                sb.Append(";BYSETPOS=");
                sb.AppendJoin(',', BySetPositions);
            }

            return sb.ToString();
        }
    }
}
