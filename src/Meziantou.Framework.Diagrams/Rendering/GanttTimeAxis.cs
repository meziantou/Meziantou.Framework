namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>The ticks of the time axis of a Gantt chart, as the time scales of d3: about ten ticks at a round interval, or the interval of the chart.</summary>
internal static class GanttTimeAxis
{
    public const int MaxTicks = 10_000;

    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
    private const double Second = 1000;
    private const double Minute = 60 * Second;
    private const double Hour = 60 * Minute;
    private const double Day = 24 * Hour;
    private const double Week = 7 * Day;
    private const double Month = 30 * Day;
    private const double Year = 365 * Day;

    private static readonly (GanttTimeUnit Unit, int Step, double Duration)[] Intervals =
    [
        (GanttTimeUnit.Second, 1, Second),
        (GanttTimeUnit.Second, 5, 5 * Second),
        (GanttTimeUnit.Second, 15, 15 * Second),
        (GanttTimeUnit.Second, 30, 30 * Second),
        (GanttTimeUnit.Minute, 1, Minute),
        (GanttTimeUnit.Minute, 5, 5 * Minute),
        (GanttTimeUnit.Minute, 15, 15 * Minute),
        (GanttTimeUnit.Minute, 30, 30 * Minute),
        (GanttTimeUnit.Hour, 1, Hour),
        (GanttTimeUnit.Hour, 3, 3 * Hour),
        (GanttTimeUnit.Hour, 6, 6 * Hour),
        (GanttTimeUnit.Hour, 12, 12 * Hour),
        (GanttTimeUnit.Day, 1, Day),
        (GanttTimeUnit.Day, 2, 2 * Day),
        (GanttTimeUnit.Week, 1, Week),
        (GanttTimeUnit.Month, 1, Month),
        (GanttTimeUnit.Month, 3, 3 * Month),
        (GanttTimeUnit.Year, 1, Year),
    ];

    public static List<DateTime> GetTicks(DateTime start, DateTime stop, GanttTickInterval? interval, DayOfWeek weekStart)
    {
        if (stop < start)
        {
            (start, stop) = (stop, start);
        }

        var span = (stop - start).TotalMilliseconds;
        if (interval is { } custom)
        {
            var unitDuration = custom.Unit switch
            {
                GanttTimeUnit.Millisecond => 1,
                GanttTimeUnit.Second => Second,
                GanttTimeUnit.Minute => Minute,
                GanttTimeUnit.Hour => Hour,
                GanttTimeUnit.Day => Day,
                GanttTimeUnit.Week => Week,
                _ => Month,
            };

            // As Mermaid, an interval that gives too many ticks is ignored
            if (Math.Ceiling(span / (unitDuration * custom.Count)) <= MaxTicks)
                return Range(start, stop, custom.Unit, custom.Count, weekStart);
        }

        var target = span / 10;
        var index = 0;
        while (index < Intervals.Length && Intervals[index].Duration <= target)
        {
            index++;
        }

        if (index == Intervals.Length)
        {
            var step = TickStep(ToMilliseconds(start) / Year, ToMilliseconds(stop) / Year, 10);
            return Range(start, stop, GanttTimeUnit.Year, Math.Max(1, (int)Math.Min(Math.Floor(step), 10_000)), weekStart);
        }

        if (index == 0)
        {
            var step = Math.Max(TickStep(ToMilliseconds(start), ToMilliseconds(stop), 10), 1);
            return Range(start, stop, GanttTimeUnit.Millisecond, (int)Math.Min(Math.Floor(step), int.MaxValue), weekStart);
        }

        var chosen = target / Intervals[index - 1].Duration < Intervals[index].Duration / target ? Intervals[index - 1] : Intervals[index];
        return Range(start, stop, chosen.Unit, chosen.Step, DayOfWeek.Sunday);
    }

    /// <summary>The step of d3 for about <paramref name="count"/> values between two numbers: 1, 2 or 5 times a power of ten.</summary>
    private static double TickStep(double start, double stop, int count)
    {
        var step0 = Math.Abs(stop - start) / count;
        if (step0 <= 0 || !double.IsFinite(step0))
            return 1;

        var step1 = Math.Pow(10, Math.Floor(Math.Log10(step0)));
        var error = step0 / step1;
        if (error >= Math.Sqrt(50))
        {
            step1 *= 10;
        }
        else if (error >= Math.Sqrt(10))
        {
            step1 *= 5;
        }
        else if (error >= Math.Sqrt(2))
        {
            step1 *= 2;
        }

        return step1;
    }

    private static double ToMilliseconds(DateTime date) => (date - Epoch).TotalMilliseconds;

    /// <summary>Returns the dates of an interval of d3 every <paramref name="step"/> units from the start to the stop, both included.</summary>
    private static List<DateTime> Range(DateTime start, DateTime stop, GanttTimeUnit unit, int step, DayOfWeek weekStart)
    {
        var result = new List<DateTime>();
        if (step < 1)
            return result;

        DateTime? current = Floor(start, unit, step, weekStart);
        if (current < start)
        {
            current = Next(current, unit, step);
        }

        for (var guard = 0; current is { } date && date <= stop && result.Count < MaxTicks && guard < MaxTicks * 40; guard++)
        {
            if (Matches(date, unit, step, weekStart))
            {
                result.Add(date);
            }

            current = Next(date, unit, step);
        }

        return result;
    }

    private static DateTime? Next(DateTime? date, GanttTimeUnit unit, int step)
    {
        if (date is not { } value)
            return null;

        try
        {
            return unit switch
            {
                GanttTimeUnit.Millisecond => value.AddMilliseconds(step),
                GanttTimeUnit.Second => value.AddSeconds(1),
                GanttTimeUnit.Minute => value.AddMinutes(1),
                GanttTimeUnit.Hour => value.AddHours(1),
                GanttTimeUnit.Day => value.AddDays(1),
                GanttTimeUnit.Week => value.AddDays(7),
                GanttTimeUnit.Month => value.AddMonths(1),
                _ => value.AddYears(step),
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static DateTime Floor(DateTime date, GanttTimeUnit unit, int step, DayOfWeek weekStart)
    {
        switch (unit)
        {
            case GanttTimeUnit.Millisecond:
                var milliseconds = Math.Floor(ToMilliseconds(date) / step) * step;
                return Epoch.AddMilliseconds(Math.Max(milliseconds, (DateTime.MinValue - Epoch).TotalMilliseconds));
            case GanttTimeUnit.Second:
                return new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, DateTimeKind.Unspecified);
            case GanttTimeUnit.Minute:
                return new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, 0, DateTimeKind.Unspecified);
            case GanttTimeUnit.Hour:
                return new DateTime(date.Year, date.Month, date.Day, date.Hour, 0, 0, DateTimeKind.Unspecified);
            case GanttTimeUnit.Day:
                return date.Date;
            case GanttTimeUnit.Week:
                var days = ((int)date.DayOfWeek - (int)weekStart + 7) % 7;
                return date.Date.AddDays(date.Date.Ticks / TimeSpan.TicksPerDay >= days ? -days : 0);
            case GanttTimeUnit.Month:
                return new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
            default:
                var year = Math.Max(1, date.Year / step * step);
                return new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        }
    }

    /// <summary>Indicates whether a date is a tick of the interval of d3 <c>every(step)</c>: its field, such as the minute or the month, is a multiple of the step.</summary>
    private static bool Matches(DateTime date, GanttTimeUnit unit, int step, DayOfWeek weekStart) => unit switch
    {
        GanttTimeUnit.Second => date.Second % step == 0,
        GanttTimeUnit.Minute => date.Minute % step == 0,
        GanttTimeUnit.Hour => date.Hour % step == 0,
        GanttTimeUnit.Day => (date.Day - 1) % step == 0,
        GanttTimeUnit.Week => WeeksSinceEpoch(date, weekStart) % step == 0,
        GanttTimeUnit.Month => (date.Month - 1) % step == 0,
        _ => true,
    };

    private static long WeeksSinceEpoch(DateTime date, DayOfWeek weekStart)
    {
        var epochWeek = Floor(Epoch, GanttTimeUnit.Week, 1, weekStart);
        var weeks = (long)Math.Floor((date - epochWeek).TotalDays / 7);
        return Math.Abs(weeks);
    }
}
