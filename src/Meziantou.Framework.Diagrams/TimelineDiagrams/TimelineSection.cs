namespace Meziantou.Framework.Diagrams;

/// <summary>A section of a timeline: a title above periods of the same color.</summary>
public sealed class TimelineSection
{
    /// <summary>Initializes a new instance of the <see cref="TimelineSection"/> class.</summary>
    /// <param name="title">The title.</param>
    public TimelineSection(DiagramText title)
    {
        ArgumentNullException.ThrowIfNull(title);
        Title = title;
    }

    /// <summary>Gets or sets the title.</summary>
    public DiagramText Title { get; set; }

    /// <summary>Gets the periods, in order.</summary>
    public IList<TimelinePeriod> Periods { get; } = new List<TimelinePeriod>();

    /// <summary>Adds a period.</summary>
    /// <param name="label">The text of the period, such as a year.</param>
    /// <param name="events">The events.</param>
    /// <returns>The period.</returns>
    public TimelinePeriod AddPeriod(DiagramText label, params IEnumerable<DiagramText> events)
    {
        var period = new TimelinePeriod(label);
        foreach (var item in events)
        {
            period.Events.Add(item);
        }

        Periods.Add(period);
        return period;
    }
}
