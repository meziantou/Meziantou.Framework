using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagrams;

/// <summary>The interval between the ticks of the axis of a Gantt chart, such as every week (Mermaid <c>tickInterval 1week</c>).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct GanttTickInterval
{
    /// <summary>Initializes a new instance of the <see cref="GanttTickInterval"/> struct.</summary>
    /// <param name="count">The number of units, from 1.</param>
    /// <param name="unit">The unit: from <see cref="GanttTimeUnit.Millisecond"/> to <see cref="GanttTimeUnit.Month"/>.</param>
    public GanttTickInterval(int count, GanttTimeUnit unit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        if (unit is GanttTimeUnit.Year || !Enum.IsDefined(unit))
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "The unit of the ticks is from a millisecond to a month.");

        Count = count;
        Unit = unit;
    }

    /// <summary>Gets the number of units.</summary>
    public int Count { get; }

    /// <summary>Gets the unit.</summary>
    public GanttTimeUnit Unit { get; }
}
