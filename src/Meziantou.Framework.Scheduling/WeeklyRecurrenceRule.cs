using System.Text;

namespace Meziantou.Framework.Scheduling;

public sealed class WeeklyRecurrenceRule : RecurrenceRule
{
    public IList<Month> ByMonths { get; set; } = new List<Month>();
    public IList<DayOfWeek> ByWeekDays { get; set; } = new List<DayOfWeek>();

    protected override IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate)
    {
        var byWeekDays = ByWeekDays?.ToList();
        if (IsEmpty(byWeekDays))
        {
            byWeekDays = new List<DayOfWeek> { startDate.DayOfWeek };
        }

        var dayOffsets = byWeekDays.Select(day => (day - WeekStart + 7) % 7).Distinct().OrderBy(a => a).ToList();
        var startOfWeek = Extensions.StartOfWeek(startDate, WeekStart);

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

            if (b)
            {
                foreach (var dayOffset in dayOffsets)
                {
                    var next = startOfWeek.AddDays(dayOffset);
                    if (next >= startDate)
                        yield return next;
                }
            }

            startOfWeek = startOfWeek.AddDays(7 * Interval);
        }
    }

    public override string Text
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append("FREQ=WEEKLY");

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
