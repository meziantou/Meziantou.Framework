using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A timeline: periods in order, each with its events, grouped in sections of the same color (Mermaid <c>timeline</c>).
/// </summary>
public sealed class TimelineDiagram : Diagram
{
    /// <summary>Gets or sets the direction of the time axis.</summary>
    public TimelineDirection Direction { get; set; }

    /// <summary>Gets the periods that are in no section, drawn first.</summary>
    public IList<TimelinePeriod> Periods { get; } = new List<TimelinePeriod>();

    /// <summary>Gets the sections.</summary>
    public IList<TimelineSection> Sections { get; } = new List<TimelineSection>();

    /// <summary>
    /// Gets or sets a value that indicates whether the periods that are in no section have the same color. By default,
    /// each one has the next color of the palette of the theme (Mermaid <c>disableMulticolor</c>).
    /// </summary>
    public bool DisableMulticolor { get; set; }

    /// <summary>Adds a period that is in no section.</summary>
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

    /// <summary>Adds a section.</summary>
    /// <param name="title">The title.</param>
    /// <returns>The section.</returns>
    public TimelineSection AddSection(DiagramText title)
    {
        var section = new TimelineSection(title);
        Sections.Add(section);
        return section;
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => TimelineDiagramRenderer.Render(this, context, group);
}
