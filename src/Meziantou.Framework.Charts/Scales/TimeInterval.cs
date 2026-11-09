namespace Meziantou.Framework.Charts;

/// <summary>An interval of a time scale (every 15 minutes, every 3 months...), with the UTC calendar.</summary>
internal sealed class TimeInterval
{
    private const double Second = 1000;
    private const double Minute = 60 * Second;
    private const double Hour = 60 * Minute;
    private const double Day = 24 * Hour;
    private const double Week = 7 * Day;
    private const double Month = 30 * Day;
    private const double Year = 365 * Day;

    private static readonly TimeInterval[] Intervals =
    [
        new(TimeUnit.Second, 1, Second),
        new(TimeUnit.Second, 5, 5 * Second),
        new(TimeUnit.Second, 15, 15 * Second),
        new(TimeUnit.Second, 30, 30 * Second),
        new(TimeUnit.Minute, 1, Minute),
        new(TimeUnit.Minute, 5, 5 * Minute),
        new(TimeUnit.Minute, 15, 15 * Minute),
        new(TimeUnit.Minute, 30, 30 * Minute),
        new(TimeUnit.Hour, 1, Hour),
        new(TimeUnit.Hour, 3, 3 * Hour),
        new(TimeUnit.Hour, 6, 6 * Hour),
        new(TimeUnit.Hour, 12, 12 * Hour),
        new(TimeUnit.Day, 1, Day),
        new(TimeUnit.Day, 2, 2 * Day),
        new(TimeUnit.Week, 1, Week),
        new(TimeUnit.Month, 1, Month),
        new(TimeUnit.Month, 3, 3 * Month),
        new(TimeUnit.Year, 1, Year),
    ];

    private TimeInterval(TimeUnit unit, double step, double duration)
    {
        Unit = unit;
        Step = step;
        Duration = duration;
    }

    public TimeUnit Unit { get; }

    public double Step { get; }

    public double Duration { get; }

    /// <summary>Chooses the interval closest to <paramref name="count"/> ticks between two times, as d3's <c>timeTicks</c>.</summary>
    public static TimeInterval Choose(double start, double stop, int count)
    {
        var target = Math.Abs(stop - start) / Math.Max(1, count);
        var index = 0;
        while (index < Intervals.Length && Intervals[index].Duration <= target)
        {
            index++;
        }

        if (index == Intervals.Length)
        {
            var step = Math.Max(1, TickGenerator.TickStep(start / Year, stop / Year, count));
            return new TimeInterval(TimeUnit.Year, Math.Max(1, Math.Round(step, MidpointRounding.AwayFromZero)), step * Year);
        }

        if (index == 0)
        {
            var step = Math.Max(1, TickGenerator.TickStep(start, stop, count));
            return new TimeInterval(TimeUnit.Millisecond, step, step);
        }

        return target / Intervals[index - 1].Duration < Intervals[index].Duration / target ? Intervals[index - 1] : Intervals[index];
    }

    /// <summary>Returns the times of the interval between two times, both included.</summary>
    public double[] Range(double start, double stop)
    {
        if (Unit is TimeUnit.Millisecond)
            return TickGenerator.Ticks(start, stop, Math.Max(1, (stop - start) / Step));

        // The times out of the range of DateTime have no tick
        start = Math.Max(start, TimeFormats.MinMilliseconds);
        stop = Math.Min(stop, TimeFormats.MaxMilliseconds);
        if (!(start <= stop))
            return [];

        var result = new List<double>();
        var first = TimeFormats.FromMilliseconds(start);
        var end = TimeFormats.FromMilliseconds(stop);
        var current = TryMove(first, Floor, out var floor) ? floor : first;
        for (var guard = 0; current <= end && guard < 10_000; guard++)
        {
            if (current >= first && IsAligned(current))
            {
                result.Add(TimeFormats.ToMilliseconds(current));
            }

            if (!TryMove(current, Next, out current))
                break;
        }

        return [.. result];
    }

    /// <summary>Rounds a time down to the interval.</summary>
    public double Floor(double milliseconds)
    {
        if (Unit is TimeUnit.Millisecond)
            return Math.Floor(milliseconds / Step) * Step;

        // A time whose interval starts out of the range of DateTime stays as it is
        if (!TimeFormats.IsInRange(milliseconds) || !TryMove(TimeFormats.FromMilliseconds(milliseconds), Floor, out var current))
            return milliseconds;

        while (!IsAligned(current))
        {
            if (!TryMove(current, Previous, out current))
                return milliseconds;
        }

        return TimeFormats.ToMilliseconds(current);
    }

    /// <summary>Rounds a time up to the interval.</summary>
    public double Ceiling(double milliseconds)
    {
        if (Unit is TimeUnit.Millisecond)
            return Math.Ceiling(milliseconds / Step) * Step;

        // A time whose interval ends out of the range of DateTime stays as it is
        if (!TimeFormats.IsInRange(milliseconds))
            return milliseconds;

        var date = TimeFormats.FromMilliseconds(milliseconds);
        if (!TryMove(date, Floor, out var current) || (current < date && !TryMove(current, Next, out current)))
            return milliseconds;

        while (!IsAligned(current))
        {
            if (!TryMove(current, Next, out current))
                return milliseconds;
        }

        return TimeFormats.ToMilliseconds(current);
    }

    /// <summary>Applies a move to a time, or returns <see langword="false"/> when the result is out of the range of <see cref="DateTime"/>.</summary>
    private static bool TryMove(DateTime date, Func<DateTime, DateTime> move, out DateTime result)
    {
        try
        {
            result = move(date);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            result = date;
            return false;
        }
    }

    private DateTime Floor(DateTime date)
    {
        return Unit switch
        {
            TimeUnit.Second => new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, DateTimeKind.Utc),
            TimeUnit.Minute => new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, 0, DateTimeKind.Utc),
            TimeUnit.Hour => new DateTime(date.Year, date.Month, date.Day, date.Hour, 0, 0, DateTimeKind.Utc),
            TimeUnit.Day => date.Date,
            TimeUnit.Week => date.Date.AddDays(-(int)date.DayOfWeek),
            TimeUnit.Month => new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => new DateTime(date.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
    }

    private DateTime Next(DateTime date)
    {
        return Unit switch
        {
            TimeUnit.Second => date.AddSeconds(1),
            TimeUnit.Minute => date.AddMinutes(1),
            TimeUnit.Hour => date.AddHours(1),
            TimeUnit.Day => date.AddDays(1),
            TimeUnit.Week => date.AddDays(7),
            TimeUnit.Month => date.AddMonths(1),
            _ => date.AddYears(1),
        };
    }

    private DateTime Previous(DateTime date)
    {
        return Unit switch
        {
            TimeUnit.Second => date.AddSeconds(-1),
            TimeUnit.Minute => date.AddMinutes(-1),
            TimeUnit.Hour => date.AddHours(-1),
            TimeUnit.Day => date.AddDays(-1),
            TimeUnit.Week => date.AddDays(-7),
            TimeUnit.Month => date.AddMonths(-1),
            _ => date.AddYears(-1),
        };
    }

    // d3's interval.every(step): the field of the unit is a multiple of the step
    private bool IsAligned(DateTime date)
    {
        var step = (int)Step;
        return Unit switch
        {
            TimeUnit.Second => date.Second % step is 0,
            TimeUnit.Minute => date.Minute % step is 0,
            TimeUnit.Hour => date.Hour % step is 0,
            TimeUnit.Day => (date.Day - 1) % step is 0,
            TimeUnit.Month => (date.Month - 1) % step is 0,
            TimeUnit.Year => date.Year % step is 0,
            _ => true,
        };
    }
}
