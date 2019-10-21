#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Meziantou.Framework.Scheduling
{
    public class DailyRecurrenceRule : RecurrenceRule
    {
        public IList<Month> ByMonths { get; set; } = new List<Month>();
        public IList<int> ByMonthDays { get; set; } = new List<int>();
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
                    sb.Append(string.Join(",", ByWeekDays.Select(Utilities.DayOfWeekToString)));
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
