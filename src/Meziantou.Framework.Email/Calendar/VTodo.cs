using System.Globalization;

namespace Meziantou.Framework.Email.Calendar;

/// <summary>A to-do of an iCalendar object (<c>VTODO</c>): a task, with its due date and its progress.</summary>
public sealed class VTodo : CalendarComponent
{
    /// <summary>Creates a to-do.</summary>
    public VTodo()
        : base("VTODO")
    {
    }

    /// <summary>Creates the builder of a task of Outlook from the to-do.</summary>
    /// <returns>The builder.</returns>
    public MapiTaskBuilder ToMapiTaskBuilder() => Internals.CalendarConverter.ToTask(this);

    /// <summary>Gets or sets the unique identifier (<c>UID</c>).</summary>
    public string? Uid
    {
        get => GetProperty("UID")?.Value;
        set => SetValue("UID", value);
    }

    /// <summary>Gets or sets the summary, the subject of the to-do (<c>SUMMARY</c>).</summary>
    public string? Summary
    {
        get => GetText("SUMMARY");
        set => SetText("SUMMARY", value);
    }

    /// <summary>Gets or sets the description (<c>DESCRIPTION</c>).</summary>
    public string? Description
    {
        get => GetText("DESCRIPTION");
        set => SetText("DESCRIPTION", value);
    }

    /// <summary>Gets or sets the start (<c>DTSTART</c>).</summary>
    public CalendarDateTime? Start
    {
        get => GetDateTime("DTSTART");
        set => SetDateTime("DTSTART", value);
    }

    /// <summary>Gets or sets the due date (<c>DUE</c>).</summary>
    public CalendarDateTime? Due
    {
        get => GetDateTime("DUE");
        set => SetDateTime("DUE", value);
    }

    /// <summary>Gets or sets when the to-do was completed (<c>COMPLETED</c>).</summary>
    public CalendarDateTime? Completed
    {
        get => GetDateTime("COMPLETED");
        set => SetDateTime("COMPLETED", value);
    }

    /// <summary>Gets or sets the progress, from 0 to 100 (<c>PERCENT-COMPLETE</c>).</summary>
    public int? PercentComplete
    {
        get => GetInteger("PERCENT-COMPLETE");
        set => SetInteger("PERCENT-COMPLETE", value);
    }

    /// <summary>Gets or sets the status (<c>STATUS</c>): <c>NEEDS-ACTION</c>, <c>IN-PROCESS</c>, <c>COMPLETED</c> or <c>CANCELLED</c>.</summary>
    public string? Status
    {
        get => GetProperty("STATUS")?.Value;
        set => SetValue("STATUS", value);
    }

    /// <summary>Gets or sets the priority, from 1 (highest) to 9 (lowest), 0 for none (<c>PRIORITY</c>).</summary>
    public int? Priority
    {
        get => GetInteger("PRIORITY");
        set => SetValue("PRIORITY", value?.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Gets or sets the recurrence rule (<c>RRULE</c>).</summary>
    public RecurrenceRule? RecurrenceRule
    {
        get => GetProperty("RRULE") is { } property ? RecurrenceRule.Parse(property.Value) : null;
        set => SetValue("RRULE", value?.ToString());
    }
}
