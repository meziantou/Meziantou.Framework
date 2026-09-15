namespace Meziantou.Framework.Scheduling;

/// <summary>Represents a calendar event with scheduling information.</summary>
/// <remarks>
/// The <c>...Parameters</c> collections hold the parameters of the properties the event is written with, in the form
/// <see cref="Attendee.Parameters"/> describes. They are validated as they are added, filled in order by the parser, and written
/// only with their property. The writer sets the VALUE and TZID parameters of a date-time property itself, so the parser does
/// not store them and the writer skips them.
/// </remarks>
public sealed class Event
{
    /// <summary>Gets or sets the unique identifier for the event.</summary>
    public string? Id { get; set; }

    /// <summary>Gets the parameters of the UID property.</summary>
    public IList<KeyValuePair<string, string>> IdParameters { get; } = new InternetCalendarParameterCollection();

    /// <summary>Gets or sets the event summary or title.</summary>
    public string? Summary { get; set; }

    /// <summary>Gets the parameters of the SUMMARY property, such as LANGUAGE or ALTREP.</summary>
    public IList<KeyValuePair<string, string>> SummaryParameters { get; } = new InternetCalendarParameterCollection();

    /// <summary>Gets or sets the event description.</summary>
    /// <remarks>The DESCRIPTION property is not written when the value is <see langword="null"/>, which is also what the parser produces for an event without one.</remarks>
    public string? Description { get; set; }

    /// <summary>Gets the parameters of the DESCRIPTION property, such as LANGUAGE or ALTREP.</summary>
    public IList<KeyValuePair<string, string>> DescriptionParameters { get; } = new InternetCalendarParameterCollection();

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

    /// <summary>Gets the parameters of the CREATED property, VALUE and TZID excepted.</summary>
    public IList<KeyValuePair<string, string>> CreatedParameters { get; } = new InternetCalendarParameterCollection();

    /// <summary>Gets or sets the date and time when the event was last modified.</summary>
    /// <remarks>
    /// Written as the LAST-MODIFIED property in UTC, as RFC 5545 section 3.8.7.3 requires: a <see cref="DateTimeKind.Local"/>
    /// value is converted to UTC and a <see cref="DateTimeKind.Unspecified"/> one is taken as UTC. The property is not written
    /// when the value is <c>default(DateTime)</c>.
    /// </remarks>
    public DateTime LastModified { get; set; }

    /// <summary>Gets the parameters of the LAST-MODIFIED property, VALUE and TZID excepted.</summary>
    public IList<KeyValuePair<string, string>> LastModifiedParameters { get; } = new InternetCalendarParameterCollection();

    /// <summary>Gets or sets the date and time stamp for the event.</summary>
    /// <remarks>
    /// Written as the DTSTAMP property in UTC, as RFC 5545 section 3.8.7.2 requires: a <see cref="DateTimeKind.Local"/> value
    /// is converted to UTC and a <see cref="DateTimeKind.Unspecified"/> one is taken as UTC. RFC 5545 requires the property
    /// in a VEVENT, but it is not written when the value is <c>default(DateTime)</c>, so the output does not depend on the
    /// current time; set it, for instance to <see cref="DateTime.UtcNow"/>, to produce a conforming event.
    /// </remarks>
    public DateTime DateTimeStamp { get; set; }

    /// <summary>Gets the parameters of the DTSTAMP property, VALUE and TZID excepted.</summary>
    public IList<KeyValuePair<string, string>> DateTimeStampParameters { get; } = new InternetCalendarParameterCollection();

    /// <summary>Gets or sets the start date and time of the event.</summary>
    /// <remarks>The DTSTART property is not written when the value is <c>default(DateTime)</c>, which is also what the parser produces for an event without one.</remarks>
    public DateTime Start { get; set; }

    /// <summary>Gets the parameters of the DTSTART property, VALUE and TZID excepted.</summary>
    public IList<KeyValuePair<string, string>> StartParameters { get; } = new InternetCalendarParameterCollection();

    /// <summary>Gets or sets the end date and time of the event.</summary>
    /// <remarks>
    /// <para>The DTEND property is not written when the value is <c>default(DateTime)</c>.</para>
    /// <para>The parser computes it from DURATION when the event has one (RFC 5545 section 3.3.6): the days and weeks are added to
    /// the wall clock of the start and the hours, minutes and seconds to the instant it denotes. The event is then written back
    /// with that DURATION rather than DTEND as long as the end still equals the start plus the duration.</para>
    /// </remarks>
    public DateTime End { get; set; }

    /// <summary>Gets the parameters of the DTEND property, or of the DURATION property the end is read from and written with, VALUE and TZID excepted.</summary>
    public IList<KeyValuePair<string, string>> EndParameters { get; } = new InternetCalendarParameterCollection();

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
    /// instant and is converted to this time zone; a local time skipped by a forward transition of the local time zone is
    /// read with the offset in effect before the transition, as RFC 5545 section 3.3.5 reads a wall clock.</para>
    /// <para><see cref="Created"/>, <see cref="LastModified"/> and <see cref="DateTimeStamp"/> are not affected, and the time
    /// zone is ignored when <see cref="IsAllDay"/> is <see langword="true"/>.</para>
    /// </remarks>
    public TimeZoneInfo? TimeZone { get; set; }

    /// <summary>Gets or sets the recurrence rule for repeating events.</summary>
    /// <remarks>
    /// <para>RFC 5545 section 3.3.10 requires UNTIL to have the value type of DTSTART, so the writer converts it, without modifying
    /// the rule: to a DATE for an all-day event, to a floating date-time for a floating start, and to a UTC date-time for a
    /// start in UTC, in local time or in <see cref="TimeZone"/>. A floating UNTIL is read in the time zone of the start, a DATE
    /// bounds the occurrences through the end of that day, and a date-time becomes a DATE by its date in the frame of the start.
    /// An UNTIL whose instant is outside the range of <see cref="DateTime"/> is written as its first or last UTC value.</para>
    /// <para>The parser reads the first RRULE of the event; RFC 5545 discourages any further one, which goes to
    /// <see cref="RawProperties"/>.</para>
    /// </remarks>
    public RecurrenceRule? RecurrenceRule { get; set; }

    /// <summary>Gets the parameters of the RRULE property.</summary>
    public IList<KeyValuePair<string, string>> RecurrenceRuleParameters { get; } = new InternetCalendarParameterCollection();

    /// <summary>Gets or sets the status of the event.</summary>
    /// <remarks>
    /// The STATUS property is not written when the value is <see langword="null"/>, which is also what the parser produces for an
    /// event without one. A value that is not a member of <see cref="EventStatus"/> cannot be written, and
    /// <see cref="InternetCalendar.ToIcs()"/> throws an <see cref="InvalidOperationException"/> before writing anything.
    /// </remarks>
    public EventStatus? Status { get; set; }

    /// <summary>Gets the parameters of the STATUS property.</summary>
    public IList<KeyValuePair<string, string>> StatusParameters { get; } = new InternetCalendarParameterCollection();

    /// <summary>Gets additional custom properties for the event, each value written according to the value type of its property.</summary>
    /// <remarks>
    /// <para>A property whose value type is TEXT (RFC 5545 section 3.3.11) — an <c>X-</c> property, a property RFC 5545 does not
    /// define, or a TEXT property such as LOCATION or CLASS — has its value escaped when written, so it cannot hold a structured
    /// value such as a list or a parameter; use <see cref="RawProperties"/> for those. The value of a property whose value type
    /// is not TEXT, such as URL, GEO or EXDATE, is written as is, its control characters dropped.</para>
    /// <para>The parser stores a property here only when it carries no parameter, occurs once, has a single TEXT value, and
    /// that value is written back unchanged. See <see cref="RawProperties"/>.</para>
    /// <para>A property whose name is not a valid property name, or is the name of a property the event is written with
    /// (such as DTSTART, UID, SUMMARY or RRULE, and BEGIN or END, and DURATION when <see cref="End"/> is set), is not written.</para>
    /// </remarks>
    public IDictionary<string, string> AdditionalProperties { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the properties of the event that are written verbatim, in order, after <see cref="AdditionalProperties"/>.</summary>
    /// <remarks>
    /// <para>The parser stores here, as written, every property the model does not represent and that
    /// <see cref="AdditionalProperties"/> does not hold: a property with parameters, a repeated one, one whose value type is not
    /// TEXT whatever its value, such as <c>EXDATE:20240104T100000</c>, <c>URL:https://example.com/?q=a,b</c> or
    /// <c>CATEGORIES:WORK</c>, or a TEXT value TEXT escaping would not write back unchanged, such as <c>X-LIST:a,b</c>.</para>
    /// <para>A TZID parameter of these properties, as of any other written parameter, makes the calendar carry a matching
    /// VTIMEZONE component when the identifier resolves.</para>
    /// <para>A property whose name is the name of a property the event is written with is not written, except RRULE, so an
    /// additional recurrence rule the parser read is written back.</para>
    /// </remarks>
    public IList<InternetCalendarProperty> RawProperties { get; } = new List<InternetCalendarProperty>();

    /// <summary>Gets or sets the DURATION the parser computed <see cref="End"/> from.</summary>
    internal InternetCalendarDuration? Duration { get; set; }
}
