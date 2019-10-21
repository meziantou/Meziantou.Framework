#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Meziantou.Framework.Scheduling
{
    public class MonthlyRecurrenceRule : RecurrenceRule
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
                    result = FilterBySetPosition(result.Distinct().OrderBy(d => d).ToList(), BySetPositions);

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
                    sb.Append(string.Join(",", ByMonths.Select(month => (int)month)));
                }

                if (!IsEmpty(ByMonthDays))
                {
                    sb.Append(";BYMONTHDAY=");
                    sb.Append(string.Join(",", ByMonthDays));
                }

                if (!IsEmpty(ByWeekDays))
                {
                    sb.Append(";BYDAY=");
                    sb.Append(string.Join(",", ByWeekDays));
                }

                if (!IsEmpty(BySetPositions))
                {
                    sb.Append(";BYSETPOS=");
                    sb.Append(string.Join(",", BySetPositions));
                }

                return sb.ToString();
            }
        }
    }
}
