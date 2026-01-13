namespace Meziantou.Framework.Scheduling;

/// <summary>Represents a weekly recurrence rule.</summary>
/// <example>
/// <code>
/// var rrule = new WeeklyRecurrenceRule { Interval = 2, ByWeekDays = { DayOfWeek.Monday, DayOfWeek.Wednesday } };
/// var nextOccurrences = rrule.GetNextOccurrences(DateTime.Now).ToArray();
/// </code>
/// </example>
public sealed class WeeklyRecurrenceRule : RecurrenceRule
{
    /// <summary>Limits occurrences to specific months.</summary>
    public IList<Month> ByMonths { get; set; } = new List<Month>();

    /// <summary>Limits occurrences to specific days of the week.</summary>
    public IList<DayOfWeek> ByWeekDays { get; set; } = new List<DayOfWeek>();

    protected override IEnumerable<DateTime> GetNextOccurrencesInternal(DateTime startDate)
    {
        var hasTimeFilters = !IsEmpty(ByHours) || !IsEmpty(ByMinutes) || !IsEmpty(BySeconds);
        var byWeekDays = ByWeekDays?.ToList();
        if (IsEmpty(byWeekDays))
        {
            byWeekDays = [startDate.DayOfWeek];
        }

        var dayOffsets = byWeekDays.Select(day => (day - WeekStart + 7) % 7).Distinct().Order().ToArray();
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
                    {
                        if (hasTimeFilters)
                        {
                            foreach (var occurrence in ExpandByTime(next))
                            {
                                yield return occurrence;
                            }
                        }
                        else
                        {
                            yield return next;
                        }
                    }
                }
            }

            startOfWeek = startOfWeek.AddDays(7 * Interval);
        }
    }

    private IEnumerable<DateTime> ExpandByTime(DateTime date)
    {
        var hours = IsEmpty(ByHours) ? [date.Hour] : ByHours;
        var minutes = IsEmpty(ByMinutes) ? [date.Minute] : ByMinutes;
        var seconds = IsEmpty(BySeconds) ? [date.Second] : BySeconds;

        var dateOnly = date.Date;
        foreach (var hour in hours)
        {
            foreach (var minute in minutes)
            {
                foreach (var second in seconds)
                {
                    var result = dateOnly.AddHours(hour).AddMinutes(minute).AddSeconds(second);
                    if (result >= date)
                    {
                        yield return result;
                    }
                }
            }
        }
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
