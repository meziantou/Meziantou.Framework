namespace Meziantou.Framework.Scheduling;

/// <summary>Reads an iCalendar stream (RFC 5545) into an <see cref="InternetCalendar"/>.</summary>
internal static class InternetCalendarParser
{
    public static bool TryParse(TextReader reader, [NotNullWhen(returnValue: true)] out InternetCalendar? calendar, out string? error)
    {
        if (!TryReadContentLines(reader, out var lines, out error))
        {
            calendar = null;
            return false;
        }

        return TryParse(lines, out calendar, out error);
    }

    public static bool TryParse(ReadOnlySpan<char> ics, [NotNullWhen(returnValue: true)] out InternetCalendar? calendar, out string? error)
    {
        if (!TryReadContentLines(ics, out var lines, out error))
        {
            calendar = null;
            return false;
        }

        return TryParse(lines, out calendar, out error);
    }

    private static bool TryParse(List<ContentLine> lines, [NotNullWhen(returnValue: true)] out InternetCalendar? calendar, out string? error)
    {
        calendar = null;

        var index = 0;
        if (index >= lines.Count || !IsBegin(lines[index], out var component) || component is not "VCALENDAR")
        {
            error = "The content does not start with BEGIN:VCALENDAR";
            return false;
        }

        index++;
        var result = new InternetCalendar();
        while (index < lines.Count)
        {
            var line = lines[index];
            if (IsBegin(line, out component))
            {
                index++;
                if (component is "VEVENT")
                {
                    if (!TryParseEvent(lines, ref index, out var @event, out error))
                        return false;

                    result.Events.Add(@event);
                }
                else
                {
                    // VTIMEZONE, VTODO, VJOURNAL and VFREEBUSY have no counterpart in the model. The time zone
                    // of an event is resolved from the TZID parameter of its DTSTART rather than from VTIMEZONE.
                    if (!TrySkipComponent(lines, ref index, component, out error))
                        return false;
                }

                continue;
            }

            if (IsEnd(line, out component))
            {
                if (component is not "VCALENDAR")
                {
                    error = $"Unexpected 'END:{component}' in a VCALENDAR component";
                    return false;
                }

                index++;

                // RFC 5545 section 3.4 allows several iCalendar objects in one stream, but the model holds one.
                if (index != lines.Count)
                {
                    error = "The content contains more than one iCalendar object";
                    return false;
                }

                calendar = result;
                error = null;
                return true;
            }

            index++;
            switch (line.Name.ToUpperInvariant())
            {
                case "VERSION":
                    result.Version = line.GetTextValue();
                    break;

                // PRODID identifies the product that wrote the calendar, which this library sets itself.
                case "PRODID":
                    break;

                default:
                    result.AdditionalProperties[line.Name] = line.GetTextValue();
                    break;
            }
        }

        error = "The VCALENDAR component is not terminated by END:VCALENDAR";
        return false;
    }

    private static bool TryParseEvent(List<ContentLine> lines, ref int index, [NotNullWhen(returnValue: true)] out Event? @event, out string? error)
    {
        @event = null;

        var result = new Event();
        string? timeZoneId = null;
        while (index < lines.Count)
        {
            var line = lines[index];
            if (IsBegin(line, out var component))
            {
                // A VALARM has no counterpart in the model.
                index++;
                if (!TrySkipComponent(lines, ref index, component, out error))
                    return false;

                continue;
            }

            if (IsEnd(line, out component))
            {
                if (component is not "VEVENT")
                {
                    error = $"Unexpected 'END:{component}' in a VEVENT component";
                    return false;
                }

                index++;
                if (timeZoneId is not null)
                {
                    if (!TimeZones.TryFind(timeZoneId, out var timeZone))
                    {
                        error = $"The time zone '{timeZoneId}' referenced by a TZID property parameter was not found";
                        return false;
                    }

                    result.TimeZone = timeZone;
                }

                @event = result;
                error = null;
                return true;
            }

            index++;
            switch (line.Name.ToUpperInvariant())
            {
                case "UID":
                    result.Id = line.GetTextValue();
                    break;

                case "SUMMARY":
                    result.Summary = line.GetTextValue();
                    break;

                case "DESCRIPTION":
                    result.Description = line.GetTextValue();
                    break;

                case "STATUS":
                    if (!TryParseStatus(line.GetTextValue(), out var status, out error))
                        return false;

                    result.Status = status;
                    break;

                case "ORGANIZER":
                    if (!TryParseUserAddress(line, out var organizer, out error))
                        return false;

                    result.Organizer = new Organizer { Address = organizer };
                    break;

                case "ATTENDEE":
                    if (!TryParseUserAddress(line, out var attendee, out error))
                        return false;

                    result.Attendees.Add(new Attendee { Address = attendee });
                    break;

                case "CREATED":
                    if (!TryParseDateTimeProperty(line, ref timeZoneId, out var created, out error))
                        return false;

                    result.Created = created;
                    break;

                case "LAST-MODIFIED":
                    if (!TryParseDateTimeProperty(line, ref timeZoneId, out var lastModified, out error))
                        return false;

                    result.LastModified = lastModified;
                    break;

                case "DTSTAMP":
                    if (!TryParseDateTimeProperty(line, ref timeZoneId, out var dateTimeStamp, out error))
                        return false;

                    result.DateTimeStamp = dateTimeStamp;
                    break;

                case "DTSTART":
                    if (!TryParseDateTimeProperty(line, ref timeZoneId, out var start, out error))
                        return false;

                    result.Start = start;
                    break;

                case "DTEND":
                    if (!TryParseDateTimeProperty(line, ref timeZoneId, out var end, out error))
                        return false;

                    result.End = end;
                    break;

                case "RRULE":
                    if (!RecurrenceRule.TryParse(line.Value, out var recurrenceRule, out var recurrenceRuleError))
                    {
                        error = $"The RRULE value '{line.Value}' is invalid: {recurrenceRuleError}";
                        return false;
                    }

                    result.RecurrenceRule = recurrenceRule;
                    break;

                default:
                    result.AdditionalProperties[line.Name] = line.GetTextValue();
                    break;
            }
        }

        error = "The VEVENT component is not terminated by END:VEVENT";
        return false;
    }

    private static bool TryParseStatus(string value, out EventStatus status, out string? error)
    {
        switch (value.ToUpperInvariant())
        {
            case "TENTATIVE":
                status = EventStatus.Tentative;
                error = null;
                return true;

            case "CONFIRMED":
                status = EventStatus.Confirmed;
                error = null;
                return true;

            case "CANCELLED":
                status = EventStatus.Cancelled;
                error = null;
                return true;

            default:
                // The statuses of the other components (RFC 5545 section 3.8.1.11) cannot describe an event.
                status = default;
                error = $"The STATUS value '{value}' is not one of the statuses of an event";
                return false;
        }
    }

    private static bool TryParseUserAddress(ContentLine line, out InternetCalendarUserAddress? address, out string? error)
    {
        // RFC 5545 section 3.3.3: a CAL-ADDRESS is a URI, usually a mailto one.
        if (!Uri.TryCreate(line.Value, UriKind.Absolute, out var uri))
        {
            address = null;
            error = $"The {line.Name} value '{line.Value}' is not a calendar user address";
            return false;
        }

        address = new InternetCalendarUserAddress(uri);
        error = null;
        return true;
    }

    /// <summary>Parses a DATE-TIME or DATE value (RFC 5545 sections 3.3.4 and 3.3.5), recording the time zone it names.</summary>
    private static bool TryParseDateTimeProperty(ContentLine line, ref string? timeZoneId, out DateTime value, out string? error)
    {
        if (!TryParseDateTime(line.Value, out value))
        {
            error = $"The {line.Name} value '{line.Value}' is not a date-time";
            return false;
        }

        if (line.GetParameter("TZID") is not { Length: > 0 } id)
        {
            error = null;
            return true;
        }

        if (value.Kind is DateTimeKind.Utc)
        {
            // RFC 5545 section 3.2.19: a UTC value already carries its offset, so a TZID would contradict it.
            error = $"The {line.Name} value '{line.Value}' is a UTC date-time and cannot carry a TZID property parameter";
            return false;
        }

        if (timeZoneId is not null && !string.Equals(timeZoneId, id, StringComparison.Ordinal))
        {
            // An Event holds a single time zone, so it cannot describe properties expressed in different ones.
            error = $"The properties of the event reference two different time zones, '{timeZoneId}' and '{id}'";
            return false;
        }

        timeZoneId = id;
        error = null;
        return true;
    }

    private static bool TryParseDateTime(string value, out DateTime result)
    {
        // A DATE value, which VALUE=DATE marks, denotes the whole day and is read as its first instant.
        if (value.Length is 8)
            return DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

        // Form 2: a date-time in UTC.
        if (value.Length > 0 && value[^1] is 'Z')
        {
            if (!DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                return false;

            result = DateTime.SpecifyKind(result, DateTimeKind.Utc);
            return true;
        }

        // Form 1 and form 3: a floating date-time, or a date-time in the time zone the TZID parameter names.
        return DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }

    /// <summary>Consumes a component whose content the model does not represent, including the components nested in it.</summary>
    private static bool TrySkipComponent(List<ContentLine> lines, ref int index, string component, out string? error)
    {
        var depth = 1;
        while (index < lines.Count)
        {
            var line = lines[index];
            index++;

            if (IsBegin(line, out _))
            {
                depth++;
            }
            else if (IsEnd(line, out var end))
            {
                depth--;
                if (depth is 0)
                {
                    if (!string.Equals(end, component, StringComparison.Ordinal))
                    {
                        error = $"The {component} component is terminated by 'END:{end}'";
                        return false;
                    }

                    error = null;
                    return true;
                }
            }
        }

        error = $"The {component} component is not terminated by 'END:{component}'";
        return false;
    }

    private static bool IsBegin(ContentLine line, out string component)
    {
        return IsDelimiter(line, "BEGIN", out component);
    }

    private static bool IsEnd(ContentLine line, out string component)
    {
        return IsDelimiter(line, "END", out component);
    }

    private static bool IsDelimiter(ContentLine line, string name, out string component)
    {
        if (string.Equals(line.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            component = line.Value.ToUpperInvariant();
            return true;
        }

        component = "";
        return false;
    }

    /// <summary>Reads the content lines of a text reader.</summary>
    private static bool TryReadContentLines(TextReader reader, [NotNullWhen(returnValue: true)] out List<ContentLine>? lines, out string? error)
    {
        var reading = new ContentLineReader();
        while (reader.ReadLine() is { } line)
        {
            if (!reading.TryAppend(line.AsSpan(), out error))
            {
                lines = null;
                return false;
            }
        }

        return reading.TryComplete(out lines, out error);
    }

    /// <summary>Reads the content lines of a span, which spares the caller the string a text reader would need.</summary>
    private static bool TryReadContentLines(ReadOnlySpan<char> ics, [NotNullWhen(returnValue: true)] out List<ContentLine>? lines, out string? error)
    {
        var reading = new ContentLineReader();
        while (!ics.IsEmpty)
        {
            ReadOnlySpan<char> line;
            var index = ics.IndexOfAny('\r', '\n');
            if (index < 0)
            {
                line = ics;
                ics = default;
            }
            else
            {
                line = ics[..index];

                // A CRLF, a lone CR and a lone LF each end a line, as TextReader.ReadLine also treats them.
                var length = ics[index] is '\r' && index + 1 < ics.Length && ics[index + 1] is '\n' ? 2 : 1;
                ics = ics[(index + length)..];
            }

            if (!reading.TryAppend(line, out error))
            {
                lines = null;
                return false;
            }
        }

        return reading.TryComplete(out lines, out error);
    }

    /// <summary>Assembles content lines from the lines of a stream, undoing the folding described by RFC 5545 section 3.1.</summary>
    private sealed class ContentLineReader
    {
        private readonly List<ContentLine> _lines = [];
        private StringBuilder? _current;
        private bool _isFirstLine = true;

        public bool TryAppend(ReadOnlySpan<char> line, out string? error)
        {
            if (_isFirstLine)
            {
                _isFirstLine = false;

                // A stream read as text may still start with the encoded byte order mark.
                if (line.Length > 0 && line[0] is '\uFEFF')
                {
                    line = line[1..];
                }
            }

            // A line break followed by a single white space continues the previous content line.
            if (line.Length > 0 && line[0] is ' ' or '\t')
            {
                if (_current is null)
                {
                    error = $"The content starts with the continuation of a content line, '{line.ToString()}'";
                    return false;
                }

                Append(_current, line[1..]);
                error = null;
                return true;
            }

            if (!TryFlush(out error))
                return false;

            // An empty line is not a content line; producers occasionally leave one before END:VCALENDAR.
            if (line.Length is not 0)
            {
                _current = new StringBuilder(line.Length);
                Append(_current, line);
            }

            return true;
        }

        public bool TryComplete([NotNullWhen(returnValue: true)] out List<ContentLine>? lines, out string? error)
        {
            if (!TryFlush(out error))
            {
                lines = null;
                return false;
            }

            lines = _lines;
            return true;
        }

        private bool TryFlush(out string? error)
        {
            if (_current is null)
            {
                error = null;
                return true;
            }

            if (!ContentLine.TryParse(_current.ToString(), out var contentLine, out error))
                return false;

            _lines.Add(contentLine);
            _current = null;
            return true;
        }

        private static void Append(StringBuilder sb, ReadOnlySpan<char> value)
        {
#if NETSTANDARD2_0
            // StringBuilder.Append(ReadOnlySpan<char>) does not exist on .NET Standard 2.0.
            foreach (var c in value)
            {
                sb.Append(c);
            }
#else
            sb.Append(value);
#endif
        }
    }
}
