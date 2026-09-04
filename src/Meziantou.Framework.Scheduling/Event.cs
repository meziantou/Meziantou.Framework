namespace Meziantou.Framework.Scheduling;

/// <summary>Represents a calendar event with scheduling information.</summary>
public sealed class Event
{
    /// <summary>Gets or sets the unique identifier for the event.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the event summary or title.</summary>
    public string? Summary { get; set; }

    /// <summary>Gets or sets the event organizer.</summary>
    public Organizer? Organizer { get; set; }

    /// <summary>Gets the attendees for the event.</summary>
    public IList<Attendee> Attendees { get; } = new List<Attendee>();

    /// <summary>Gets or sets the date and time when the event was created.</summary>
    public DateTime Created { get; set; }

    /// <summary>Gets or sets the date and time when the event was last modified.</summary>
    public DateTime LastModified { get; set; }

    /// <summary>Gets or sets the date and time stamp for the event.</summary>
    public DateTime DateTimeStamp { get; set; }

    /// <summary>Gets or sets the start date and time of the event.</summary>
    public DateTime Start { get; set; }

    /// <summary>Gets or sets the end date and time of the event.</summary>
    public DateTime End { get; set; }

    /// <summary>Gets or sets the time zone <see cref="Start"/> and <see cref="End"/> are expressed in.</summary>
    /// <remarks>
    /// <para>When set, the two properties are written as DTSTART;TZID= and DTEND;TZID= (RFC 5545 section 3.3.5 form 3),
    /// and the calendar carries a matching VTIMEZONE component. When <see langword="null"/>, they are written as a UTC
    /// or a floating date-time, as before.</para>
    /// <para>A value whose <see cref="DateTime.Kind"/> is <see cref="DateTimeKind.Unspecified"/> is taken as a wall-clock
    /// reading in this time zone. A <see cref="DateTimeKind.Utc"/> or <see cref="DateTimeKind.Local"/> value denotes an
    /// instant and is converted to this time zone.</para>
    /// <para><see cref="Created"/>, <see cref="LastModified"/> and <see cref="DateTimeStamp"/> are not affected.</para>
    /// </remarks>
    public TimeZoneInfo? TimeZone { get; set; }

    /// <summary>Gets or sets the recurrence rule for repeating events.</summary>
    public RecurrenceRule? RecurrenceRule { get; set; }

    /// <summary>Gets or sets the status of the event.</summary>
    public EventStatus Status { get; set; }

    /// <summary>Gets additional custom properties for the event.</summary>
    public IDictionary<string, string> AdditionalProperties { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
