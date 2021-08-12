using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Meziantou.Framework.Scheduling;

public sealed class YearlyRecurrenceRule : RecurrenceRule
{
    public IList<int>? ByMonthDays { get; set; }
    public IList<ByDay>? ByWeekDays { get; set; }
    public IList<Month>? ByMonths { get; set; }
    //public IList<int> ByWeekNo { get; set; }
    public IList<int>? ByYearDays { get; set; }

    protected override IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate)
    {
        if (IsEmpty(ByMonthDays) && IsEmpty(ByWeekDays) && IsEmpty(ByMonths) && /*IsEmpty(ByWeekNo) && */IsEmpty(ByYearDays))
        {
            while (true)
            {
                yield return startDate;
                startDate = startDate.AddYears(Interval);
            }
        }

        var startOfYear = Extensions.StartOfYear(startDate, keepTime: true);
        while (true)
        {
            var resultByMonthDays = ResultByMonthDays(startDate, startOfYear);
            var resultByWeekDays = ResultByWeekDays(startOfYear);
            var resultByYearDays = ResultByYearDays(startOfYear);
            var resultByMonths = ResultByMonths(startOfYear);
            List<DateTime>? resultByWeekNo = null;// ResultByWeekNo(startDate, startOfYear);

            var result = Intersect(resultByMonths, resultByWeekNo, resultByYearDays, resultByMonthDays, resultByWeekDays);
            result = FilterBySetPosition(result.Distinct().OrderBy(d => d).ToList(), BySetPositions);

            foreach (var date in result.Where(d => d >= startDate))
            {
                yield return date;
            }

            startOfYear = startOfYear.AddYears(Interval);
        }
    }

    private List<DateTime>? ResultByWeekDays(DateTime startOfYear)
    {
        List<DateTime>? result = null;
        if (!IsEmpty(ByWeekDays))
        {
            result = new List<DateTime>();

            if (IsEmpty(ByMonths))
            {
                // 1) Find all dates that match the day of month contraint
                var potentialResults = new Dictionary<ByDay, IList<DateTime>>();
                foreach (var byDay in ByWeekDays)
                {
                    potentialResults.Add(byDay, new List<DateTime>());
                }

                for (var day = startOfYear; day.Year == startOfYear.Year; day = day.AddDays(1))
                {
                    foreach (var byDay in ByWeekDays)
                    {
                        if (byDay.DayOfWeek == day.DayOfWeek)
                        {
                            potentialResults[byDay].Add(day);
                        }
                    }
                }

                // 2) Filter by ordinal
                foreach (var potentialResult in potentialResults)
                {
                    if (potentialResult.Key.Ordinal.HasValue)
                    {
                        int index;
                        if (potentialResult.Key.Ordinal > 0)
                        {
                            index = potentialResult.Key.Ordinal.Value - 1;
                        }
                        else
                        {
                            index = potentialResult.Value.Count + potentialResult.Key.Ordinal.Value;
                        }

                        if (index >= 0 && index < potentialResult.Value.Count)
                        {
                            result.Add(potentialResult.Value[index]);
                        }
                    }
                    else
                    {
                        result.AddRange(potentialResult.Value);
                    }
                }
            }
            else
            {
                for (var dt = startOfYear; dt.Year == startOfYear.Year; dt = dt.AddMonths(1))
                {
                    var resultByWeekDaysInMonth = ResultByWeekDaysInMonth(dt, ByWeekDays);
                    if (resultByWeekDaysInMonth != null)
                    {
                        result.AddRange(resultByWeekDaysInMonth);
                    }
                }
            }

            result.Sort();
        }
        return result;
    }

    private List<DateTime>? ResultByMonthDays(DateTime startDate, DateTime startOfYear)
    {
        List<DateTime>? result = null;

        var monthDays = ByMonthDays;
        if (IsEmpty(ByMonthDays) && IsEmpty(ByWeekDays) && /*IsEmpty(ByWeekNo) &&*/ IsEmpty(ByYearDays))
        {
            monthDays = new List<int> { startDate.Day };
        }

        if (!IsEmpty(monthDays))
        {
            result = new List<DateTime>();
            for (var i = 0; i < 12; i++)
            {
                var startOfMonth = startOfYear.AddMonths(i);
                var daysInMonth = DateTime.DaysInMonth(startOfMonth.Year, startOfMonth.Month);
                foreach (var day in monthDays)
                {
                    if (day >= 1 && day <= daysInMonth)
                    {
                        result.Add(startOfMonth.AddDays(day - 1));
                    }
                    else if (day <= -1 && day >= -daysInMonth)
                    {
                        result.Add(startOfMonth.AddDays(daysInMonth + day));
                    }
                }
            }

            result.Sort();
            //result = FilterBySetPosition(result, BySetPositions).ToList();
        }

        return result;
    }

    private List<DateTime>? ResultByYearDays(DateTime startOfYear)
    {
        List<DateTime>? result = null;
        if (!IsEmpty(ByYearDays))
        {
            result = new List<DateTime>();
            var daysInYear = DateTime.IsLeapYear(startOfYear.Year) ? 366 : 365;
            foreach (var day in ByYearDays)
            {
                if (day >= 1 && day <= daysInYear)
                {
                    result.Add(startOfYear.AddDays(day - 1));
                }
                else if (day <= -1 && day >= -daysInYear)
                {
                    result.Add(startOfYear.AddDays(daysInYear + day));
                }
            }

            result.Sort();
            //result = FilterBySetPosition(result, BySetPositions).ToList();
        }

        return result;
    }

    private List<DateTime>? ResultByMonths(DateTime startOfYear)
    {
        List<DateTime>? result = null;
        if (!IsEmpty(ByMonths))
        {
            result = new List<DateTime>();
            foreach (var month in ByMonths.Distinct().OrderBy(_ => _))
            {
                if (month >= Month.January && month <= Month.December)
                {
                    for (var dt = startOfYear.AddMonths((int)month - 1); dt.Month == (int)month; dt = dt.AddDays(1))
                    {
                        result.Add(dt);
                    }
                }
            }

            //result.Sort();
            //result = FilterBySetPosition(result, BySetPositions).ToList();
        }

        return result;
    }

    //private List<DateTime> ResultByWeekNo(DateTime startDate, DateTime startOfYear)
    //{
    //    List<DateTime> result = null;
    //    if (!IsEmpty(ByWeekNo))
    //    {
    //        //result = new List<DateTime>();

    //        //DateTime week = DateTimeExtensions.FirstDateOfWeekISO8601(startDate.Year, startDate.Month);

    //        ////CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(startDate, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Sunday);

    //        //result.Sort();
    //        //result = FilterBySetPosition(result, BySetPositions).ToList();
    //    }
    //    return result;
    //}

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
                sb.Append(string.Join(",", ByMonths.Cast<int>()));
            }

            //if (!IsEmpty(ByWeekNo))
            //{
            //    sb.Append(";BYWEEKNO=");
            //    sb.Append(string.Join(",", ByWeekNo));
            //}

            if (!IsEmpty(ByYearDays))
            {
                sb.Append(";BYYEARDAY=");
                sb.Append(string.Join(",", ByYearDays));
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
