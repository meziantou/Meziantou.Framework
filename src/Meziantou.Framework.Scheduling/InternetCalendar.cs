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
    /// <summary>iCalendar requires CRLF between content lines (RFC 5545 section 3.1), which
    /// <see cref="TextWriter.WriteLine()"/> does not guarantee: it emits <see cref="Environment.NewLine"/>.</summary>
    private const string CrLf = "\r\n";

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

        WriteLine(writer, "BEGIN:VCALENDAR");
        if (!string.IsNullOrEmpty(Version))
            WriteTextProperty(writer, "VERSION", Version);

        WriteAdditionalProperties(writer, AdditionalProperties);

        foreach (var @event in Events)
        {
            WriteLine(writer, "BEGIN:VEVENT");
            if (!string.IsNullOrEmpty(@event.Id))
                WriteTextProperty(writer, "UID", @event.Id);

            WriteLine(writer, "STATUS:" + Utilities.StatusToString(@event.Status));
            if ((@event.Organizer?.Address) is not null)
                WriteLine(writer, "ORGANIZER:" + @event.Organizer.Address);

            foreach (var attendee in @event.Attendees)
            {
                if (attendee is null)
                    continue;

                WriteLine(writer, "ATTENDEE:" + attendee.Address);
            }

            WriteLine(writer, "CREATED:" + Utilities.DateTimeToString(@event.Created));
            WriteLine(writer, "LAST-MODIFIED:" + Utilities.DateTimeToString(@event.LastModified));
            WriteLine(writer, "DTSTAMP:" + Utilities.DateTimeToString(@event.DateTimeStamp));
            WriteLine(writer, "DTSTART:" + Utilities.DateTimeToString(@event.Start));
            WriteLine(writer, "DTEND:" + Utilities.DateTimeToString(@event.End));
            if (@event.RecurrenceRule is not null)
                WriteLine(writer, "RRULE:" + @event.RecurrenceRule.Text);

            if (!string.IsNullOrEmpty(@event.Summary))
                WriteTextProperty(writer, "SUMMARY", @event.Summary);

            WriteAdditionalProperties(writer, @event.AdditionalProperties);

            WriteLine(writer, "DESCRIPTION:\\n");
            WriteLine(writer, "END:VEVENT");
        }

        WriteLine(writer, "END:VCALENDAR");
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
        writer.Write(CrLf);
    }

    /// <summary>Writes a content line whose value is already in its final form.</summary>
    private static void WriteLine(TextWriter writer, string line)
    {
        writer.Write(line);
        writer.Write(CrLf);
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
