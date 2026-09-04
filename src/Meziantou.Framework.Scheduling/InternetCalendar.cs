namespace Meziantou.Framework.Scheduling;

/// <summary>Represents an iCalendar calendar containing events.</summary>
/// <example>
/// <code>
/// var calendar = new InternetCalendar();
/// calendar.Events.Add(new Event
/// {
///     Start = DateTime.Now,
///     End = DateTime.Now.AddHours(1),
///     Summary = "Meeting"
/// });
/// var icsContent = calendar.ToIcs();
/// </code>
/// </example>
public sealed class InternetCalendar
{
    /// <summary>The product identifier written as PRODID, in the FPI form suggested by RFC 5545 section 3.7.3.</summary>
    private const string ProductIdentifier = "-//Meziantou//Meziantou.Framework.Scheduling//EN";

    /// <summary>Gets additional custom properties for the calendar.</summary>
    public IDictionary<string, string> AdditionalProperties { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the events in this calendar.</summary>
    public IList<Event> Events { get; } = new List<Event>();

    /// <summary>Gets or sets the iCalendar version.</summary>
    public string Version { get; set; } = "2.0";

    /// <summary>Writes the calendar to a stream in iCalendar format.</summary>
    /// <param name="stream">The stream to write to.</param>
    public void ToIcs(Stream stream)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using TextWriter writer = new StreamWriter(stream, encoding, bufferSize: 1024, leaveOpen: true);
        ToIcs(writer);
    }

    /// <summary>Writes the calendar to a text writer in iCalendar format.</summary>
    /// <param name="writer">The text writer to write to.</param>
    public void ToIcs(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        /*
        BEGIN:VCALENDAR

        VERSION:2.0
        PRODID:LUCCA.FIGGO
        METHOD:REQUEST

        BEGIN:VEVENT
        STATUS:CONFIRMED
        ORGANIZER:MAILTO:contact@meziantou.net
        ATTENDEE:MAILTO:contact@meziantou.net
        CREATED:20141208T100900Z
        DTSTAMP:20141208T100900Z
        LAST-MODIFIED:19960817T133000Z
        SUMMARY:Absent
        DESCRIPTION:\n
        DTSTART:20150102T080000
        DTEND:20150109T200000
        UID:-softfluent-ilucca-net-figgo_26_42006-0800_42013-2000
        X-MICROSOFT-CDO-BUSYSTATUS:OOF
        X-MICROSOFT-CDO-ALLDAYEVENT:1
        END:VEVENT

        END:VCALENDAR
        */

        // The identifiers are validated before the first write so an invalid one cannot produce partial output.
        var timeZones = GetTimeZones();

        Utilities.WriteLine(writer, "BEGIN:VCALENDAR");
        if (!string.IsNullOrEmpty(Version))
            WriteTextProperty(writer, "VERSION", Version);

        // PRODID is REQUIRED in a VCALENDAR (RFC 5545 section 3.6).
        Utilities.WriteLine(writer, "PRODID:" + ProductIdentifier);

        WriteAdditionalProperties(writer, AdditionalProperties);

        // A VTIMEZONE must precede the components referencing its TZID.
        foreach (var timeZone in timeZones)
        {
            VTimeZoneWriter.Write(writer, timeZone.TimeZone, timeZone.ReferenceDate);
        }

        foreach (var @event in Events)
        {
            Utilities.WriteLine(writer, "BEGIN:VEVENT");
            if (!string.IsNullOrEmpty(@event.Id))
                WriteTextProperty(writer, "UID", @event.Id);

            Utilities.WriteLine(writer, "STATUS:" + Utilities.StatusToString(@event.Status));
            if ((@event.Organizer?.Address) is not null)
                Utilities.WriteLine(writer, "ORGANIZER:" + @event.Organizer.Address);

            foreach (var attendee in @event.Attendees)
            {
                if (attendee is null)
                    continue;

                Utilities.WriteLine(writer, "ATTENDEE:" + attendee.Address);
            }

            Utilities.WriteLine(writer, "CREATED:" + Utilities.DateTimeToString(@event.Created));
            Utilities.WriteLine(writer, "LAST-MODIFIED:" + Utilities.DateTimeToString(@event.LastModified));
            Utilities.WriteLine(writer, "DTSTAMP:" + Utilities.DateTimeToString(@event.DateTimeStamp));
            WriteDateTimeProperty(writer, "DTSTART", @event.Start, @event.TimeZone);
            WriteDateTimeProperty(writer, "DTEND", @event.End, @event.TimeZone);
            if (@event.RecurrenceRule is not null)
                Utilities.WriteLine(writer, "RRULE:" + GetRecurrenceRuleValue(@event.RecurrenceRule, @event.TimeZone));

            if (!string.IsNullOrEmpty(@event.Summary))
                WriteTextProperty(writer, "SUMMARY", @event.Summary);

            WriteAdditionalProperties(writer, @event.AdditionalProperties);

            Utilities.WriteLine(writer, "DESCRIPTION:\\n");
            Utilities.WriteLine(writer, "END:VEVENT");
        }

        Utilities.WriteLine(writer, "END:VCALENDAR");
    }

    /// <summary>Collects the distinct time zones referenced by the events, with the earliest start each is used for.</summary>
    private List<(TimeZoneInfo TimeZone, DateTime ReferenceDate)> GetTimeZones()
    {
        var result = new List<(TimeZoneInfo TimeZone, DateTime ReferenceDate)>();
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var @event in Events)
        {
            if (@event?.TimeZone is not { } timeZone)
                continue;

            if (!Utilities.IsValidTimeZoneId(timeZone.Id))
                throw new InvalidOperationException($"The time zone identifier '{timeZone.Id}' cannot be written as a TZID property parameter");

            var referenceDate = Utilities.ToWallClock(@event.Start, timeZone);
            if (indexes.TryGetValue(timeZone.Id, out var index))
            {
                if (referenceDate < result[index].ReferenceDate)
                {
                    result[index] = (result[index].TimeZone, referenceDate);
                }
            }
            else
            {
                indexes.Add(timeZone.Id, result.Count);
                result.Add((timeZone, referenceDate));
            }
        }

        return result;
    }

    /// <summary>Writes a date-time property, using the TZID form (RFC 5545 section 3.3.5) when the event has a time zone.</summary>
    private static void WriteDateTimeProperty(TextWriter writer, string name, DateTime value, TimeZoneInfo? timeZone)
    {
        if (timeZone is null)
        {
            Utilities.WriteLine(writer, name + ':' + Utilities.DateTimeToString(value));
            return;
        }

        // The identifier was validated before any output was written.
        var wallClock = Utilities.ToWallClock(value, timeZone);
        Utilities.WriteLine(writer, name + ";TZID=" + timeZone.Id + ':' + wallClock.ToString(Utilities.FloatingDateTimeFormat, CultureInfo.InvariantCulture));
    }

    /// <summary>RFC 5545 section 3.3.10: when DTSTART carries a TZID, UNTIL must be a UTC date-time.</summary>
    private static string GetRecurrenceRuleValue(RecurrenceRule recurrenceRule, TimeZoneInfo? timeZone)
    {
        var text = recurrenceRule.Text;
        if (timeZone is null || recurrenceRule.EndDate is not { Kind: DateTimeKind.Unspecified } endDate)
            return text;

        // TimeZoneInfo.ConvertTimeToUtc rejects a time that does not exist in the time zone, so a value in the
        // gap of a forward transition is resolved rather than failing the whole write.
        var utc = timeZone.IsInvalidTime(endDate)
            ? DateTime.SpecifyKind(endDate - timeZone.BaseUtcOffset, DateTimeKind.Utc)
            : TimeZoneInfo.ConvertTimeToUtc(endDate, timeZone);

        // The replaced token is a fixed-length value this library itself produced, so the substitution is unambiguous.
        var floating = ";UNTIL=" + endDate.ToString(Utilities.FloatingDateTimeFormat, CultureInfo.InvariantCulture);
        return text.Replace(floating, ";UNTIL=" + utc.ToString(Utilities.UtcDateTimeFormat, CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static void WriteAdditionalProperties(TextWriter writer, IDictionary<string, string> properties)
    {
        foreach (var additionalProperty in properties)
        {
            // A name outside the iCalendar grammar cannot be written as a content line, and a
            // name carrying a line break would start an attacker-chosen property.
            if (!IsValidPropertyName(additionalProperty.Key))
                continue;

            // RFC 5545 section 3.8.8.2: the default value type of a non-standard property is TEXT.
            WriteTextProperty(writer, additionalProperty.Key, additionalProperty.Value);
        }
    }

    /// <summary>Writes a content line whose value is escaped as an iCalendar TEXT value.</summary>
    private static void WriteTextProperty(TextWriter writer, string name, string? value)
    {
        writer.Write(name);
        writer.Write(':');
        WriteEscaped(writer, value);
        writer.Write(Utilities.CrLf);
    }

    /// <summary>Escapes an iCalendar TEXT value per RFC 5545 section 3.3.11.</summary>
    private static void WriteEscaped(TextWriter writer, string? value)
    {
        if (value is null)
            return;

        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    writer.Write("\\\\");
                    break;
                case ';':
                    writer.Write("\\;");
                    break;
                case ',':
                    writer.Write("\\,");
                    break;
                case '\r':
                    break;
                case '\n':
                    writer.Write("\\n");
                    break;
                default:
                    writer.Write(c);
                    break;
            }
        }
    }

    /// <summary>An iCalendar property name is ALPHA / DIGIT / "-" (RFC 5545 section 3.1).</summary>
    private static bool IsValidPropertyName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not '-')
                return false;
        }

        return true;
    }

    /// <summary>Converts the calendar to an iCalendar format string.</summary>
    /// <returns>The iCalendar format string representation of this calendar.</returns>
    public string ToIcs()
    {
        using var writer = new StringWriter();
        ToIcs(writer);
        return writer.ToString();
    }
}
