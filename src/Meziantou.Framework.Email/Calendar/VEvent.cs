namespace Meziantou.Framework.Email.Calendar;

/// <summary>An event of an iCalendar object (<c>VEVENT</c>): a meeting, an appointment or an all-day event, with its recurrence.</summary>
public sealed class VEvent : CalendarComponent
{
    /// <summary>Creates an event.</summary>
    public VEvent()
        : base("VEVENT")
    {
    }

    /// <summary>Gets or sets the unique identifier (<c>UID</c>).</summary>
    public string? Uid
    {
        get => GetProperty("UID")?.Value;
        set => SetValue("UID", value);
    }

    /// <summary>Gets or sets the summary, the subject of the event (<c>SUMMARY</c>).</summary>
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

    /// <summary>Gets or sets the location (<c>LOCATION</c>).</summary>
    public string? Location
    {
        get => GetText("LOCATION");
        set => SetText("LOCATION", value);
    }

    /// <summary>Gets or sets the start (<c>DTSTART</c>).</summary>
    public CalendarDateTime? Start
    {
        get => GetDateTime("DTSTART");
        set => SetDateTime("DTSTART", value);
    }

    /// <summary>Gets or sets the end (<c>DTEND</c>), exclusive: the day after the last day of an all-day event.</summary>
    public CalendarDateTime? End
    {
        get => GetDateTime("DTEND");
        set => SetDateTime("DTEND", value);
    }

    /// <summary>Gets or sets the duration (<c>DURATION</c>), which an event has instead of an end.</summary>
    public TimeSpan? Duration
    {
        get => CalendarDuration.Parse(GetProperty("DURATION")?.Value);
        set => SetValue("DURATION", value is { } duration ? CalendarDuration.Format(duration) : null);
    }

    /// <summary>Gets whether the event lasts whole days: its start is a date.</summary>
    public bool IsAllDay => Start?.IsDate ?? false;

    /// <summary>Gets or sets the recurrence rule (<c>RRULE</c>).</summary>
    public RecurrenceRule? RecurrenceRule
    {
        get => GetProperty("RRULE") is { } property ? RecurrenceRule.Parse(property.Value) : null;
        set => SetValue("RRULE", value?.ToString());
    }

    /// <summary>Gets the dates of the occurrences the recurrence excludes (<c>EXDATE</c>).</summary>
    public IReadOnlyList<CalendarDateTime> ExceptionDates => GetDates("EXDATE");

    /// <summary>Gets the dates of the occurrences the recurrence adds (<c>RDATE</c>).</summary>
    public IReadOnlyList<CalendarDateTime> RecurrenceDates => GetDates("RDATE");

    /// <summary>Gets or sets the original start of the occurrence this event replaces (<c>RECURRENCE-ID</c>), for an exception of a recurring event.</summary>
    public CalendarDateTime? RecurrenceId
    {
        get => GetDateTime("RECURRENCE-ID");
        set => SetDateTime("RECURRENCE-ID", value);
    }

    /// <summary>Gets or sets the organizer (<c>ORGANIZER</c>).</summary>
    public CalendarAttendee? Organizer
    {
        get => GetProperty("ORGANIZER") is { } property ? CalendarAttendee.FromLine(property) : null;
        set
        {
            RemoveProperties("ORGANIZER");
            if (value is not null)
            {
                Properties.Add(value.ToLine("ORGANIZER"));
            }
        }
    }

    /// <summary>Gets the attendees (<c>ATTENDEE</c>).</summary>
    public IReadOnlyList<CalendarAttendee> Attendees => [.. GetProperties("ATTENDEE").Select(CalendarAttendee.FromLine)];

    /// <summary>Gets or sets the status (<c>STATUS</c>): <c>TENTATIVE</c>, <c>CONFIRMED</c> or <c>CANCELLED</c>.</summary>
    public string? Status
    {
        get => GetProperty("STATUS")?.Value;
        set => SetValue("STATUS", value);
    }

    /// <summary>Gets or sets whether the event blocks time (<c>TRANSP</c>: <c>OPAQUE</c> or <c>TRANSPARENT</c>).</summary>
    public string? Transparency
    {
        get => GetProperty("TRANSP")?.Value;
        set => SetValue("TRANSP", value);
    }

    /// <summary>Gets the categories (<c>CATEGORIES</c>).</summary>
    public IReadOnlyList<string> Categories => [.. GetProperties("CATEGORIES").SelectMany(property => property.GetValues())];

    /// <summary>Gets or sets the revision of the event (<c>SEQUENCE</c>).</summary>
    public int? Sequence
    {
        get => GetInteger("SEQUENCE");
        set => SetInteger("SEQUENCE", value);
    }

    /// <summary>Gets or sets when the event was last modified (<c>LAST-MODIFIED</c>).</summary>
    public CalendarDateTime? LastModified
    {
        get => GetDateTime("LAST-MODIFIED");
        set => SetDateTime("LAST-MODIFIED", value);
    }

    /// <summary>Gets or sets when the object was written (<c>DTSTAMP</c>).</summary>
    public CalendarDateTime? Stamp
    {
        get => GetDateTime("DTSTAMP");
        set => SetDateTime("DTSTAMP", value);
    }

    /// <summary>Creates the builder of an appointment of Outlook from the event: its times in UTC (with the time zones of the calendar), its recurrence when Outlook can represent it, its attendees and its first reminder.</summary>
    /// <returns>The builder.</returns>
    public MapiAppointmentBuilder ToMapiAppointmentBuilder() => Internals.CalendarConverter.ToAppointment(this);

    /// <summary>Adds an attendee.</summary>
    /// <param name="attendee">The attendee.</param>
    public void AddAttendee(CalendarAttendee attendee)
    {
        ArgumentNullException.ThrowIfNull(attendee);
        Properties.Add(attendee.ToLine("ATTENDEE"));
    }

    /// <summary>Sets the categories.</summary>
    /// <param name="categories">The categories.</param>
    public void SetCategories(IEnumerable<string> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        var list = categories.ToList();
        SetValue("CATEGORIES", list.Count == 0 ? null : string.Join(",", list.Select(ContentLine.EncodeText)));
    }

    /// <summary>Adds a date to the dates the recurrence excludes (<c>EXDATE</c>).</summary>
    /// <param name="date">The original start of the occurrence.</param>
    public void AddExceptionDate(CalendarDateTime date)
    {
        var line = new ContentLine("EXDATE", "");
        date.Apply(line);
        Properties.Add(line);
    }

    /// <summary>Returns the start times of the occurrences of the event: its start, the dates of its recurrence rule and <c>RDATE</c>, without the dates of <c>EXDATE</c>. The times are in the basis of the start (UTC, local in its time zone, or floating).</summary>
    /// <returns>The start times, in order; infinitely many for a rule without an end.</returns>
    /// <exception cref="NotSupportedException">The recurrence rule has parts <see cref="RecurrenceRule.GetOccurrences(DateTime)"/> does not support.</exception>
    public IEnumerable<DateTime> GetOccurrences()
    {
        if (Start is not { } start)
        {
            return [];
        }

        var excluded = new HashSet<DateTime>(ExceptionDates.Select(date => ToStartBasis(date, start)));
        var additional = RecurrenceDates.Select(date => ToStartBasis(date, start)).Order().ToList();
        var rule = RecurrenceRule;
        if (rule?.Until is { IsUtc: true } until && !start.IsUtc && !start.IsDate)
        {
            // The end of a rule in UTC, for a start in a time zone
            rule.Until = new CalendarDateTime(ToStartBasis(until, start), start.TimeZoneId);
        }

        var occurrences = rule is null ? [start.Value] : rule.GetOccurrences(start.Value);
        return Merge(occurrences, additional).Where(date => !excluded.Contains(date));
    }

    private static IEnumerable<DateTime> Merge(IEnumerable<DateTime> occurrences, List<DateTime> additional)
    {
        var index = 0;
        DateTime? previous = null;
        foreach (var occurrence in occurrences)
        {
            while (index < additional.Count && additional[index] < occurrence)
            {
                if (additional[index] != previous)
                {
                    yield return additional[index];
                    previous = additional[index];
                }

                index++;
            }

            if (occurrence != previous)
            {
                yield return occurrence;
                previous = occurrence;
            }
        }

        for (; index < additional.Count; index++)
        {
            if (additional[index] != previous)
            {
                yield return additional[index];
                previous = additional[index];
            }
        }
    }

    /// <summary>Converts a date of EXDATE or RDATE to the basis of the start: a date gets the time of the start, a time in UTC the time zone of the start.</summary>
    private DateTime ToStartBasis(CalendarDateTime date, CalendarDateTime start)
    {
        if (start.IsDate || date.IsDate)
        {
            return DateTime.SpecifyKind(date.Value.Date + (start.IsDate ? TimeSpan.Zero : start.Value.TimeOfDay), start.Value.Kind);
        }

        if (date.IsUtc && !start.IsUtc && start.TimeZoneId is { } id && Calendar?.FindTimeZone(id) is { } timeZone && timeZone.GetUtcOffset(date.Value) is { } offset)
        {
            var local = date.Value + offset;

            // The offset of the local time, which differs from the offset of the time in UTC around a transition
            if (timeZone.GetUtcOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified)) is { } localOffset)
            {
                local = date.Value + localOffset;
            }

            return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        }

        if (!date.IsUtc && start.IsUtc && date.ToUtc(Calendar) is { } utc)
        {
            return utc;
        }

        return DateTime.SpecifyKind(date.Value, start.Value.Kind);
    }

    private List<CalendarDateTime> GetDates(string name)
    {
        var result = new List<CalendarDateTime>();
        foreach (var property in GetProperties(name))
        {
            var timeZone = property.GetParameter("TZID");
            foreach (var value in property.Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (CalendarDateTime.Parse(value, timeZone) is { } date)
                {
                    result.Add(date);
                }
            }
        }

        return result;
    }
}
