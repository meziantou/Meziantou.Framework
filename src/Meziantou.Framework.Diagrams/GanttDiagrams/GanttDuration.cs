using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagrams;

/// <summary>A duration of a task of a Gantt chart, such as 3 days (Mermaid <c>3d</c>).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct GanttDuration
{
    /// <summary>Initializes a new instance of the <see cref="GanttDuration"/> struct.</summary>
    /// <param name="value">The number of units, positive or zero.</param>
    /// <param name="unit">The unit.</param>
    public GanttDuration(double value, GanttTimeUnit unit)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "The duration is a finite number, positive or zero.");

        Value = value;
        Unit = unit;
    }

    /// <summary>Gets the number of units.</summary>
    public double Value { get; }

    /// <summary>Gets the unit.</summary>
    public GanttTimeUnit Unit { get; }

    /// <summary>Adds the duration to a date, as dayjs: the days and the weeks are rounded, the months and the years are truncated; a date out of range does not change.</summary>
    /// <param name="date">The date.</param>
    /// <returns>The date after the duration.</returns>
    internal DateTime AddTo(DateTime date)
    {
        try
        {
            return Unit switch
            {
                GanttTimeUnit.Millisecond => date.AddMilliseconds(Value),
                GanttTimeUnit.Second => date.AddMilliseconds(Value * 1000),
                GanttTimeUnit.Minute => date.AddMilliseconds(Value * 60_000),
                GanttTimeUnit.Hour => date.AddMilliseconds(Value * 3_600_000),
                GanttTimeUnit.Day => date.AddDays(Math.Floor(Value + 0.5)),
                GanttTimeUnit.Week => date.AddDays(Math.Floor((Value * 7) + 0.5)),
                GanttTimeUnit.Month => date.AddMonths((int)Math.Min(Math.Truncate(Value), 120_000)),
                _ => date.AddYears((int)Math.Min(Math.Truncate(Value), 10_000)),
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return date;
        }
    }
}
