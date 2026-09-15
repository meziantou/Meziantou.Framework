namespace Meziantou.Framework.Scheduling;

/// <summary>Represents a calendar event with scheduling information.</summary>
public sealed class Event
{
    /// <summary>Gets or sets the unique identifier for the event.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the event summary or title.</summary>
    public string? Summary { get; set; }

    /// <summary>Gets or sets the event description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the event organizer.</summary>
    /// <remarks>An organizer without an <see cref="Organizer.Address"/> is not written.</remarks>
    public Organizer? Organizer { get; set; }

    /// <summary>Gets the attendees for the event.</summary>
    /// <remarks>An attendee without an <see cref="Attendee.Address"/> is not written.</remarks>
    public IList<Attendee> Attendees { get; } = new List<Attendee>();

    /// <summary>Gets or sets the date and time when the event was created.</summary>
    /// <remarks>
    /// Written as the CREATED property in UTC, as RFC 5545 section 3.8.7.1 requires: a <see cref="DateTimeKind.Local"/> value
    /// is converted to UTC and a <see cref="DateTimeKind.Unspecified"/> one is taken as UTC. The property is not written when
    /// the value is <c>default(DateTime)</c>.
    /// </remarks>
    public DateTime Created { get; set; }

    /// <summary>Gets or sets the date and time when the event was last modified.</summary>
    /// <remarks>
    /// Written as the LAST-MODIFIED property in UTC, as RFC 5545 section 3.8.7.3 requires: a <see cref="DateTimeKind.Local"/>
    /// value is converted to UTC and a <see cref="DateTimeKind.Unspecified"/> one is taken as UTC. The property is not written
    /// when the value is <c>default(DateTime)</c>.
    /// </remarks>
    public DateTime LastModified { get; set; }

    /// <summary>Gets or sets the date and time stamp for the event.</summary>
    /// <remarks>
    /// Written as the DTSTAMP property in UTC, as RFC 5545 section 3.8.7.2 requires: a <see cref="DateTimeKind.Local"/> value
    /// is converted to UTC and a <see cref="DateTimeKind.Unspecified"/> one is taken as UTC. RFC 5545 requires the property
    /// in a VEVENT, but it is not written when the value is <c>default(DateTime)</c>, so the output does not depend on the
    /// current time; set it, for instance to <see cref="DateTime.UtcNow"/>, to produce a conforming event.
    /// </remarks>
    public DateTime DateTimeStamp { get; set; }

    /// <summary>Gets or sets the start date and time of the event.</summary>
    public DateTime Start { get; set; }

    /// <summary>Gets or sets the end date and time of the event.</summary>
    /// <remarks>The DTEND property is not written when the value is <c>default(DateTime)</c>.</remarks>
    public DateTime End { get; set; }

    /// <summary>Gets or sets a value indicating whether <see cref="Start"/> and <see cref="End"/> denote whole days rather than instants.</summary>
    /// <remarks>
    /// <para>When <see langword="true"/>, only the date part of <see cref="Start"/> and <see cref="End"/> is written, as
    /// <c>DTSTART;VALUE=DATE:20240101</c> (RFC 5545 section 3.3.4), whatever their <see cref="DateTime.Kind"/>, and
    /// <see cref="TimeZone"/> is ignored. As in RFC 5545, <see cref="End"/> is exclusive: a one-day event on January 1 ends
    /// on January 2.</para>
    /// <para>The parser sets it when DTSTART is a DATE value, and reads the date as its first instant.</para>
    /// </remarks>
    public bool IsAllDay { get; set; }

    /// <summary>Gets or sets the time zone <see cref="Start"/> and <see cref="End"/> are expressed in.</summary>
    /// <remarks>
    /// <para>When set, the two properties are written as DTSTART;TZID= and DTEND;TZID= (RFC 5545 section 3.3.5 form 3),
    /// and the calendar carries a matching VTIMEZONE component. When <see langword="null"/>, they are written as a UTC
    /// or a floating date-time, as before.</para>
    /// <para>A value whose <see cref="DateTime.Kind"/> is <see cref="DateTimeKind.Unspecified"/> is taken as a wall-clock
    /// reading in this time zone. A <see cref="DateTimeKind.Utc"/> or <see cref="DateTimeKind.Local"/> value denotes an
    /// instant and is converted to this time zone.</para>
    /// <para><see cref="Created"/>, <see cref="LastModified"/> and <see cref="DateTimeStamp"/> are not affected, and the time
    /// zone is ignored when <see cref="IsAllDay"/> is <see langword="true"/>.</para>
    /// </remarks>
    public TimeZoneInfo? TimeZone { get; set; }

    /// <summary>Gets or sets the recurrence rule for repeating events.</summary>
    public RecurrenceRule? RecurrenceRule { get; set; }

    /// <summary>Gets or sets the status of the event.</summary>
    /// <remarks>The STATUS property is not written when the value is <see langword="null"/>, which is also what the parser produces for an event without one.</remarks>
    public EventStatus? Status { get; set; }

    /// <summary>Gets additional custom properties for the event, whose values are TEXT (RFC 5545 section 3.3.11).</summary>
    /// <remarks>
    /// <para>A value is escaped when written, so it cannot hold a structured value such as a list or a parameter; use
    /// <see cref="RawProperties"/> for those. The parser only stores a property here when this form writes it back
    /// unchanged.</para>
    /// <para>A property whose name is not a valid property name, or is the name of a property the event is written with
    /// (such as DTSTART, UID or SUMMARY, and BEGIN or END), is not written.</para>
    /// </remarks>
    public IDictionary<string, string> AdditionalProperties { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the properties of the event that are written verbatim, in order, after <see cref="AdditionalProperties"/>.</summary>
    /// <remarks>
    /// <para>The parser stores here, as written, every property the model does not represent and that
    /// <see cref="AdditionalProperties"/> cannot hold without changing it: a property with parameters, a repeated one, or one
    /// whose value is not a single TEXT value, such as <c>EXDATE;TZID=Europe/Paris:20240104T100000</c> or <c>CATEGORIES:WORK,MEETING</c>.</para>
    /// <para>A property whose name is the name of a property the event is written with is not written.</para>
    /// </remarks>
    public IList<InternetCalendarProperty> RawProperties { get; } = new List<InternetCalendarProperty>();
}
