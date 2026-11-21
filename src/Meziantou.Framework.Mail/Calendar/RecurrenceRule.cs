using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Mail;

/// <summary>A recurrence rule of an iCalendar object (<c>RRULE</c>, [RFC 5545] 3.3.10), such as <c>FREQ=WEEKLY;BYDAY=MO,WE;COUNT=10</c>.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Mail.Tests/CalendarTests.cs" ref:region="RecurrenceRule_GetOccurrences" ref:dedent="true">
/// var rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=-1FR;COUNT=3")!;
/// var occurrences = rule.GetOccurrences(new DateTime(2026, 1, 30, 17, 0, 0)).ToList();
/// Assert.Equal([new DateTime(2026, 1, 30, 17, 0, 0), new DateTime(2026, 2, 27, 17, 0, 0), new DateTime(2026, 3, 27, 17, 0, 0)], occurrences);
/// </code>
/// </example>
public sealed class RecurrenceRule
{
    private const int MaxEmptyPeriods = 2000;

    /// <summary>Gets or sets the frequency.</summary>
    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Daily;

    /// <summary>Gets or sets the interval between the periods: 2 for every other week of a weekly rule.</summary>
    public int Interval { get; set; } = 1;

    /// <summary>Gets or sets the number of occurrences, or <see langword="null"/>.</summary>
    public int? Count { get; set; }

    /// <summary>Gets or sets the last date of the occurrences, or <see langword="null"/>.</summary>
    public CalendarDateTime? Until { get; set; }

    /// <summary>Gets the days of the week (<c>BYDAY</c>).</summary>
    public IList<RecurrenceDay> ByDay { get; } = [];

    /// <summary>Gets the days of the month, negative from the end (<c>BYMONTHDAY</c>).</summary>
    public IList<int> ByMonthDay { get; } = [];

    /// <summary>Gets the months, from 1 to 12 (<c>BYMONTH</c>).</summary>
    public IList<int> ByMonth { get; } = [];

    /// <summary>Gets the positions of the occurrences in each period, negative from the end (<c>BYSETPOS</c>).</summary>
    public IList<int> BySetPosition { get; } = [];

    /// <summary>Gets or sets the first day of the week (<c>WKST</c>).</summary>
    public DayOfWeek WeekStart { get; set; } = DayOfWeek.Monday;

    /// <summary>Gets the parts the rule has that the members do not read (<c>BYHOUR</c>, <c>BYMINUTE</c>, <c>BYSECOND</c>, <c>BYYEARDAY</c>, <c>BYWEEKNO</c>, extensions), by name. They are written back; <see cref="GetOccurrences"/> does not support them.</summary>
    public IDictionary<string, string> OtherParts { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads a rule.</summary>
    /// <param name="value">The rule, such as <c>FREQ=MONTHLY;BYDAY=-1FR</c>.</param>
    /// <returns>The rule, or <see langword="null"/> when it is malformed or has no <c>FREQ</c>.</returns>
    public static RecurrenceRule? Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var rule = new RecurrenceRule();
        var hasFrequency = false;
        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equal = part.IndexOf('=', StringComparison.Ordinal);
            if (equal <= 0)
            {
                return null;
            }

            var name = part[..equal].ToUpperInvariant();
            var text = part[(equal + 1)..];
            switch (name)
            {
                case "FREQ":
                    if (!Enum.TryParse<RecurrenceFrequency>(text, ignoreCase: true, out var frequency) || !Enum.IsDefined(frequency) || int.TryParse(text, CultureInfo.InvariantCulture, out _))
                    {
                        return null;
                    }

                    rule.Frequency = frequency;
                    hasFrequency = true;
                    break;
                case "INTERVAL":
                    if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var interval) || interval < 1)
                    {
                        return null;
                    }

                    rule.Interval = interval;
                    break;
                case "COUNT":
                    if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 1)
                    {
                        return null;
                    }

                    rule.Count = count;
                    break;
                case "UNTIL":
                    rule.Until = CalendarDateTime.Parse(text);
                    if (rule.Until is null)
                    {
                        return null;
                    }

                    break;
                case "WKST":
                    var index = Array.FindIndex(RecurrenceDay.Names, day => day.Equals(text, StringComparison.OrdinalIgnoreCase));
                    if (index < 0)
                    {
                        return null;
                    }

                    rule.WeekStart = (DayOfWeek)index;
                    break;
                case "BYDAY":
                    foreach (var item in text.Split(','))
                    {
                        if (RecurrenceDay.Parse(item) is not { } day)
                        {
                            return null;
                        }

                        rule.ByDay.Add(day);
                    }

                    break;
                case "BYMONTHDAY" when !ParseNumbers(text, rule.ByMonthDay, 31):
                case "BYMONTH" when !ParseNumbers(text, rule.ByMonth, 12) || rule.ByMonth.Any(month => month < 1):
                case "BYSETPOS" when !ParseNumbers(text, rule.BySetPosition, 366):
                    return null;
                case "BYMONTHDAY" or "BYMONTH" or "BYSETPOS":
                    break;
                default:
                    rule.OtherParts[name] = text;
                    break;
            }
        }

        return hasFrequency ? rule : null;
    }

    /// <summary>Returns the rule as it is written in a <c>RRULE</c> property.</summary>
    /// <returns>The rule.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder("FREQ=").Append(Frequency.ToString().ToUpperInvariant());
        if (Interval != 1)
        {
            builder.Append(";INTERVAL=").Append(Interval.ToString(CultureInfo.InvariantCulture));
        }

        if (Count is { } count)
        {
            builder.Append(";COUNT=").Append(count.ToString(CultureInfo.InvariantCulture));
        }

        if (Until is { } until)
        {
            builder.Append(";UNTIL=").Append(until.ToString());
        }

        Append(builder, "BYMONTH", ByMonth);
        Append(builder, "BYMONTHDAY", ByMonthDay);
        if (ByDay.Count > 0)
        {
            builder.Append(";BYDAY=").AppendJoin(',', ByDay);
        }

        Append(builder, "BYSETPOS", BySetPosition);
        foreach (var (name, value) in OtherParts)
        {
            builder.Append(';').Append(name.ToUpperInvariant()).Append('=').Append(value);
        }

        if (WeekStart != DayOfWeek.Monday)
        {
            builder.Append(";WKST=").Append(RecurrenceDay.Names[(int)WeekStart]);
        }

        return builder.ToString();
    }

    /// <summary>Returns the start times of the occurrences, from the first one: <paramref name="start"/> (the <c>DTSTART</c> of the component), then the dates the rule gives, until <see cref="Count"/> occurrences or <see cref="Until"/>. A rule without an end has infinitely many occurrences: take the ones you need.</summary>
    /// <param name="start">The start of the first occurrence. The occurrences have its time of day and its kind.</param>
    /// <returns>The start times of the occurrences, in order.</returns>
    /// <exception cref="NotSupportedException">The rule has a frequency of less than a day, or a part of <see cref="OtherParts"/> that changes the occurrences (<c>BYHOUR</c>, <c>BYMINUTE</c>, <c>BYSECOND</c>, <c>BYYEARDAY</c>, <c>BYWEEKNO</c>).</exception>
    public IEnumerable<DateTime> GetOccurrences(DateTime start)
    {
        if (Frequency < RecurrenceFrequency.Daily)
        {
            throw new NotSupportedException($"The occurrences of a {Frequency} rule are not supported.");
        }

        foreach (var name in new[] { "BYHOUR", "BYMINUTE", "BYSECOND", "BYYEARDAY", "BYWEEKNO" })
        {
            if (OtherParts.ContainsKey(name))
            {
                throw new NotSupportedException($"The occurrences of a rule with {name} are not supported.");
            }
        }

        return Enumerate(start);
    }

    private IEnumerable<DateTime> Enumerate(DateTime start)
    {
        var until = Until?.Value;
        var untilDate = Until?.IsDate ?? false;
        var emitted = 0;
        if (Count is not null && Count <= 0)
        {
            yield break;
        }

        // DTSTART is the first occurrence ([RFC 5545] 3.8.5.3)
        yield return start;
        if (Count is { } total && ++emitted >= total)
        {
            yield break;
        }

        var emptyPeriods = 0;
        for (long period = 0; ; period++)
        {
            if (!TryGetPeriod(start, period, out var periodStart))
            {
                yield break;
            }

            var dates = GetDates(start, periodStart);
            if (dates.Count == 0)
            {
                if (++emptyPeriods > MaxEmptyPeriods)
                {
                    yield break;
                }

                continue;
            }

            emptyPeriods = 0;
            foreach (var date in dates)
            {
                if (date.Ticks + start.TimeOfDay.Ticks > DateTime.MaxValue.Ticks)
                {
                    yield break;
                }

                var occurrence = DateTime.SpecifyKind(date + start.TimeOfDay, start.Kind);
                if (occurrence <= start)
                {
                    continue;
                }

                if (until is { } last && (untilDate ? occurrence.Date > last.Date : occurrence > last))
                {
                    yield break;
                }

                yield return occurrence;
                if (Count is { } count && ++emitted >= count)
                {
                    yield break;
                }
            }
        }
    }

    private bool TryGetPeriod(DateTime start, long period, out DateTime periodStart)
    {
        periodStart = default;
        var step = period * Interval;
        try
        {
            switch (Frequency)
            {
                case RecurrenceFrequency.Daily:
                    periodStart = start.Date.AddDays(step);
                    break;
                case RecurrenceFrequency.Weekly:
                    periodStart = start.Date.AddDays(-(((int)start.DayOfWeek - (int)WeekStart + 7) % 7)).AddDays(step * 7);
                    break;
                case RecurrenceFrequency.Monthly:
                    if (step > 120_000)
                    {
                        return false;
                    }

                    periodStart = new DateTime(start.Year, start.Month, 1).AddMonths((int)step);
                    break;
                default:
                    if (start.Year + step > 9999)
                    {
                        return false;
                    }

                    periodStart = new DateTime(start.Year + (int)step, 1, 1);
                    break;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        return true;
    }

    /// <summary>Returns the dates of a period, in order, after the BYxxx parts and BYSETPOS.</summary>
    private List<DateTime> GetDates(DateTime start, DateTime periodStart)
    {
        var dates = new List<DateTime>();
        switch (Frequency)
        {
            case RecurrenceFrequency.Daily:
                if (MatchesMonth(periodStart) && MatchesMonthDay(periodStart) && (ByDay.Count == 0 || ByDay.Any(day => day.Day == periodStart.DayOfWeek)))
                {
                    dates.Add(periodStart);
                }

                break;
            case RecurrenceFrequency.Weekly:
                for (var i = 0; i < 7; i++)
                {
                    if (periodStart > DateTime.MaxValue.AddDays(-i - 1))
                    {
                        break;
                    }

                    var date = periodStart.AddDays(i);
                    var matchesDay = ByDay.Count == 0 ? date.DayOfWeek == start.DayOfWeek : ByDay.Any(day => day.Day == date.DayOfWeek);
                    if (matchesDay && MatchesMonth(date) && MatchesMonthDay(date))
                    {
                        dates.Add(date);
                    }
                }

                break;
            case RecurrenceFrequency.Monthly:
                if (MatchesMonth(periodStart))
                {
                    dates.AddRange(GetDaysOfMonth(start, periodStart.Year, periodStart.Month));
                }

                break;
            default:
                if (ByMonth.Count > 0)
                {
                    foreach (var month in ByMonth.Distinct().Order())
                    {
                        dates.AddRange(GetDaysOfMonth(start, periodStart.Year, month));
                    }
                }
                else if (ByDay.Count > 0)
                {
                    // The days of the week in the year, at their positions in the year
                    dates.AddRange(GetDaysOfWeek(new DateTime(periodStart.Year, 1, 1), new DateTime(periodStart.Year, 12, 31)).Where(MatchesMonthDay));
                }
                else if (ByMonthDay.Count > 0)
                {
                    for (var month = 1; month <= 12; month++)
                    {
                        dates.AddRange(GetDaysOfMonth(start, periodStart.Year, month));
                    }
                }
                else if (start.Day <= DateTime.DaysInMonth(periodStart.Year, start.Month))
                {
                    dates.Add(new DateTime(periodStart.Year, start.Month, start.Day));
                }

                break;
        }

        dates = [.. dates.Distinct().Order()];
        if (BySetPosition.Count == 0 || dates.Count == 0)
        {
            return dates;
        }

        var selected = new List<DateTime>();
        foreach (var position in BySetPosition)
        {
            var index = position > 0 ? position - 1 : dates.Count + position;
            if (position != 0 && index >= 0 && index < dates.Count)
            {
                selected.Add(dates[index]);
            }
        }

        return [.. selected.Distinct().Order()];
    }

    private List<DateTime> GetDaysOfMonth(DateTime start, int year, int month)
    {
        var first = new DateTime(year, month, 1);
        var last = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        var result = new List<DateTime>();
        if (ByDay.Count > 0)
        {
            result.AddRange(GetDaysOfWeek(first, last).Where(MatchesMonthDay));
        }
        else if (ByMonthDay.Count > 0)
        {
            foreach (var day in ByMonthDay)
            {
                var value = day > 0 ? day : last.Day + day + 1;
                if (value >= 1 && value <= last.Day)
                {
                    result.Add(new DateTime(year, month, value));
                }
            }
        }
        else if (start.Day <= last.Day)
        {
            result.Add(new DateTime(year, month, start.Day));
        }

        return result;
    }

    /// <summary>Returns the days of BYDAY between two dates: all of them for a day without a position, else the day at its position (negative from the end).</summary>
    private IEnumerable<DateTime> GetDaysOfWeek(DateTime first, DateTime last)
    {
        foreach (var day in ByDay)
        {
            var days = new List<DateTime>();
            var date = first.AddDays(((int)day.Day - (int)first.DayOfWeek + 7) % 7);
            while (date <= last)
            {
                days.Add(date);
                if (date > DateTime.MaxValue.AddDays(-8))
                {
                    break;
                }

                date = date.AddDays(7);
            }

            if (day.Position == 0)
            {
                foreach (var item in days)
                {
                    yield return item;
                }
            }
            else
            {
                var index = day.Position > 0 ? day.Position - 1 : days.Count + day.Position;
                if (index >= 0 && index < days.Count)
                {
                    yield return days[index];
                }
            }
        }
    }

    private bool MatchesMonth(DateTime date) => ByMonth.Count == 0 || ByMonth.Contains(date.Month);

    private bool MatchesMonthDay(DateTime date)
    {
        if (ByMonthDay.Count == 0)
        {
            return true;
        }

        var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
        return ByMonthDay.Any(day => day > 0 ? day == date.Day : daysInMonth + day + 1 == date.Day);
    }

    private static bool ParseNumbers(string text, IList<int> values, int max)
    {
        foreach (var item in text.Split(','))
        {
            if (!int.TryParse(item.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) || value == 0 || value < -max || value > max)
            {
                return false;
            }

            values.Add(value);
        }

        return true;
    }

    private static void Append(StringBuilder builder, string name, IList<int> values)
    {
        if (values.Count > 0)
        {
            builder.Append(';').Append(name).Append('=').AppendJoin(',', values.Select(value => value.ToString(CultureInfo.InvariantCulture)));
        }
    }
}
