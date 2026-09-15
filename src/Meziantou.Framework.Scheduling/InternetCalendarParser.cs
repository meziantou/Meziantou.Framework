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

    /// <summary>Parses a stream, removing the folds of RFC 5545 section 3.1 before decoding its UTF-8 content.</summary>
    /// <remarks>
    /// A producer folding a line by octets can split the UTF-8 sequence of a character, which decoding each physical line
    /// would turn into replacement characters. A stream starting with a UTF-16 or UTF-32 byte order mark is decoded first,
    /// as its folds cannot split a UTF-8 sequence.
    /// </remarks>
    public static bool TryParse(Stream stream, [NotNullWhen(returnValue: true)] out InternetCalendar? calendar, out string? error)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.GetBuffer();
        var length = (int)buffer.Length;

        if (IsUnicodeByteOrderMark(bytes, length))
        {
            buffer.Position = 0;
            using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return TryParse(reader, out calendar, out error);
        }

        var start = length >= 3 && bytes[0] is 0xEF && bytes[1] is 0xBB && bytes[2] is 0xBF ? 3 : 0;
        var unfoldedLength = Unfold(bytes, start, length);
        return TryParse(Encoding.UTF8.GetString(bytes, start, unfoldedLength - start).AsSpan(), out calendar, out error);

        static bool IsUnicodeByteOrderMark(byte[] bytes, int length)
        {
            return (length >= 2 && bytes[0] is 0xFF && bytes[1] is 0xFE) ||
                (length >= 2 && bytes[0] is 0xFE && bytes[1] is 0xFF) ||
                (length >= 4 && bytes[0] is 0 && bytes[1] is 0 && bytes[2] is 0xFE && bytes[3] is 0xFF);
        }
    }

    /// <summary>Removes, in place, every line break followed by a single white space, returning the new end of the content.</summary>
    /// <remarks>
    /// A CRLF, a lone CR and a lone LF each end a line, as the text readers treat them. As for text, a fold only continues a
    /// non-empty line: one at the start of the content, or after an empty line, is left for the content line reader to report.
    /// </remarks>
    private static int Unfold(byte[] bytes, int start, int length)
    {
        var written = start;
        var index = start;
        while (index < length)
        {
            var b = bytes[index];
            if (b is (byte)'\r' or (byte)'\n')
            {
                var breakLength = b is (byte)'\r' && index + 1 < length && bytes[index + 1] is (byte)'\n' ? 2 : 1;
                var next = index + breakLength;
                var continuesLine = written > start && bytes[written - 1] is not (byte)'\r' and not (byte)'\n';
                if (continuesLine && next < length && bytes[next] is (byte)' ' or (byte)'\t')
                {
                    index = next + 1;
                    continue;
                }

                for (var i = 0; i < breakLength; i++)
                {
                    bytes[written++] = bytes[index++];
                }

                continue;
            }

            bytes[written++] = b;
            index++;
        }

        return written;
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
        var timeZones = new TimeZoneResolver(lines);
        List<ContentLine>? unknownProperties = null;
        while (index < lines.Count)
        {
            var line = lines[index];
            if (IsBegin(line, out component))
            {
                index++;
                if (component is "VEVENT")
                {
                    if (!TryParseEvent(lines, ref index, timeZones, out var @event, out error))
                        return false;

                    result.Events.Add(@event);
                }
                else
                {
                    // VTODO, VJOURNAL and VFREEBUSY have no counterpart in the model, and the time zone resolver
                    // reads the VTIMEZONE components on its own.
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

                AddUnknownProperties(unknownProperties, result.AdditionalProperties, result.RawProperties);
                result.TimeZoneDefinitions = timeZones.Definitions;
                calendar = result;
                error = null;
                return true;
            }

            index++;
            switch (line.Name.ToUpperInvariant())
            {
                // RFC 5545 section 3.7.4: the value is "vers" or "minver;maxver", not TEXT, so it is kept as written. An empty
                // value, which the writer would omit, leaves the default version so the calendar is written back the same way.
                case "VERSION":
                    if (line.Value.Length > 0)
                    {
                        result.Version = line.Value;
                    }

                    break;

                // PRODID identifies the product that wrote the calendar, which this library sets itself.
                case "PRODID":
                    break;

                default:
                    unknownProperties ??= [];
                    unknownProperties.Add(line);
                    break;
            }
        }

        error = "The VCALENDAR component is not terminated by END:VCALENDAR";
        return false;
    }

    private static bool TryParseEvent(List<ContentLine> lines, ref int index, TimeZoneResolver timeZones, [NotNullWhen(returnValue: true)] out Event? @event, out string? error)
    {
        @event = null;

        var result = new Event();
        string? timeZoneId = null;
        ContentLine? dtEnd = null;
        ContentLine? duration = null;
        List<ContentLine>? unknownProperties = null;
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

                // A DATE value denotes a day wherever the reader is, so no time zone applies to it (RFC 5545 section 3.3.4).
                if (timeZoneId is not null && !result.IsAllDay)
                {
                    // An identifier that cannot be resolved leaves the start and the end floating, which keeps their
                    // wall-clock reading rather than rejecting the whole calendar.
                    result.TimeZone = timeZones.Resolve(timeZoneId);
                }

                if (duration is not null && !TryApplyDuration(result, duration, dtEnd, ref unknownProperties, out error))
                    return false;

                AddUnknownProperties(unknownProperties, result.AdditionalProperties, result.RawProperties);
                @event = result;
                error = null;
                return true;
            }

            index++;
            switch (line.Name.ToUpperInvariant())
            {
                case "UID":
                    result.Id = line.GetTextValue();
                    SetParameters(result.IdParameters, line);
                    break;

                case "SUMMARY":
                    result.Summary = line.GetTextValue();
                    SetParameters(result.SummaryParameters, line);
                    break;

                case "DESCRIPTION":
                    result.Description = line.GetTextValue();
                    SetParameters(result.DescriptionParameters, line);
                    break;

                case "STATUS":
                    if (!TryParseStatus(line.GetTextValue(), out var status, out error))
                        return false;

                    result.Status = status;
                    SetParameters(result.StatusParameters, line);
                    break;

                case "ORGANIZER":
                    if (!TryParseUserAddress(line, out var organizer, out error))
                        return false;

                    result.Organizer = new Organizer { Address = organizer };
                    SetParameters(result.Organizer.Parameters, line);
                    break;

                case "ATTENDEE":
                    if (!TryParseUserAddress(line, out var attendeeAddress, out error))
                        return false;

                    var attendee = new Attendee { Address = attendeeAddress };
                    SetParameters(attendee.Parameters, line);
                    result.Attendees.Add(attendee);
                    break;

                case "CREATED":
                    if (!TryParseUtcDateTimeProperty(line, timeZones, out var created, out error))
                        return false;

                    result.Created = created;
                    SetParameters(result.CreatedParameters, line, IsDateTimeParameterSetByTheWriter);
                    break;

                case "LAST-MODIFIED":
                    if (!TryParseUtcDateTimeProperty(line, timeZones, out var lastModified, out error))
                        return false;

                    result.LastModified = lastModified;
                    SetParameters(result.LastModifiedParameters, line, IsDateTimeParameterSetByTheWriter);
                    break;

                case "DTSTAMP":
                    if (!TryParseUtcDateTimeProperty(line, timeZones, out var dateTimeStamp, out error))
                        return false;

                    result.DateTimeStamp = dateTimeStamp;
                    SetParameters(result.DateTimeStampParameters, line, IsDateTimeParameterSetByTheWriter);
                    break;

                case "DTSTART":
                    if (!TryParseDateOrDateTimeProperty(line, ref timeZoneId, out var start, out var isStartDate, out error))
                        return false;

                    result.Start = start;
                    result.IsAllDay = isStartDate;
                    SetParameters(result.StartParameters, line, IsDateTimeParameterSetByTheWriter);
                    break;

                case "DTEND":
                    if (!TryParseDateOrDateTimeProperty(line, ref timeZoneId, out var end, out _, out error))
                        return false;

                    result.End = end;
                    dtEnd = line;
                    SetParameters(result.EndParameters, line, IsDateTimeParameterSetByTheWriter);
                    break;

                case "DURATION":
                    // The end is computed once the start and its time zone are known, which may be read later.
                    duration = line;
                    break;

                case "RRULE":
                    if (!RecurrenceRule.TryParse(line.Value, out var recurrenceRule, out var recurrenceRuleError))
                    {
                        error = $"The RRULE value '{line.Value}' is invalid: {recurrenceRuleError}";
                        return false;
                    }

                    if (result.RecurrenceRule is null)
                    {
                        result.RecurrenceRule = recurrenceRule;
                        SetParameters(result.RecurrenceRuleParameters, line);
                    }
                    else
                    {
                        // RFC 5545 section 3.6.1 discourages another RRULE, which the model cannot hold, so it is kept as written.
                        unknownProperties ??= [];
                        unknownProperties.Add(line);
                    }

                    break;

                default:
                    unknownProperties ??= [];
                    unknownProperties.Add(line);
                    break;
            }
        }

        error = "The VEVENT component is not terminated by END:VEVENT";
        return false;
    }

    /// <summary>The VALUE and TZID parameters of a date-time property are determined by the model, which writes them itself.</summary>
    internal static bool IsDateTimeParameterSetByTheWriter(string name)
    {
        return string.Equals(name, "VALUE", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "TZID", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Replaces the parameters of a modelled property with those of <paramref name="line"/>, as the last occurrence of the property is the one the model holds.</summary>
    private static void SetParameters(IList<KeyValuePair<string, string>> parameters, ContentLine line, Func<string, bool>? skip = null)
    {
        parameters.Clear();
        ((InternetCalendarParameterCollection)parameters).AddParsed(line.GetParameters(), skip);
    }

    /// <summary>Computes the end of the event from its DURATION (RFC 5545 section 3.3.6).</summary>
    private static bool TryApplyDuration(Event result, ContentLine line, ContentLine? dtEnd, ref List<ContentLine>? unknownProperties, out string? error)
    {
        // RFC 5545 section 3.6.1: either DTEND or DURATION may appear in a VEVENT, but not both.
        if (dtEnd is not null)
        {
            error = "The VEVENT component has both a DTEND and a DURATION property";
            return false;
        }

        if (!InternetCalendarDuration.TryParse(line.Value, out var duration, out error))
            return false;

        if (result.Start == default)
        {
            // Without DTSTART the duration has nothing to apply to, so it is kept as written.
            unknownProperties ??= [];
            unknownProperties.Add(line);
            return true;
        }

        // RFC 5545 section 3.8.2.5: the duration of an event starting on a date is a number of days or weeks.
        if (result.IsAllDay && duration.Time != TimeSpan.Zero)
        {
            error = $"The DURATION value '{line.Value}' of an event starting on a date is not a number of days or weeks";
            return false;
        }

        if (!duration.TryGetEnd(result.Start, result.IsAllDay, result.TimeZone, out var end))
        {
            error = $"The end of an event starting at '{result.Start.ToString("s", CultureInfo.InvariantCulture)}' with the DURATION '{line.Value}' is outside the range of DateTime";
            return false;
        }

        result.End = end;
        result.Duration = duration;
        SetParameters(result.EndParameters, line, IsDateTimeParameterSetByTheWriter);
        error = null;
        return true;
    }

    /// <summary>Stores the properties the model does not have.</summary>
    /// <remarks>
    /// A property goes to <paramref name="additionalProperties"/> only when its value is a single TEXT value, as the value of an
    /// <c>X-</c> property or of LOCATION is, it carries no parameter, its name occurs once, and escaping its unescaped value gives
    /// back the value as written. Any other property, such as <c>GEO:37.38;-122.08</c>, <c>URL:https://example.com/</c>,
    /// <c>CATEGORIES:A</c>, an <c>EXDATE</c> or one with a TZID parameter, is kept verbatim in <paramref name="rawProperties"/>.
    /// </remarks>
    private static void AddUnknownProperties(List<ContentLine>? lines, IDictionary<string, string> additionalProperties, IList<InternetCalendarProperty> rawProperties)
    {
        if (lines is null)
            return;

        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            occurrences[line.Name] = occurrences.TryGetValue(line.Name, out var count) ? count + 1 : 1;
        }

        foreach (var line in lines)
        {
            if (occurrences[line.Name] is 1 && !line.HasParameters && InternetCalendarProperty.IsSingleTextProperty(line.Name))
            {
                var text = line.GetTextValue();
                if (string.Equals(Utilities.EscapeText(text), line.Value, StringComparison.Ordinal))
                {
                    additionalProperties[line.Name] = text;
                    continue;
                }
            }

            rawProperties.Add(InternetCalendarProperty.FromContentLine(line));
        }
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
        // RFC 5545 section 3.3.3: a CAL-ADDRESS is a URI, usually a mailto one. Uri also accepts an absolute path, as a
        // file URI on Unix, and a Windows path, so the value itself has to start with the scheme the URI reports.
        if (!Uri.TryCreate(line.Value, UriKind.Absolute, out var uri) || !StartsWithScheme(line.Value, uri.Scheme))
        {
            address = null;
            error = $"The {line.Name} value '{line.Value}' is not a calendar user address";
            return false;
        }

        address = new InternetCalendarUserAddress(uri);
        error = null;
        return true;
    }

    /// <summary>Checks the value starts with <c>scheme ":"</c>, where a scheme starts with a letter (RFC 3986 section 3.1).</summary>
    private static bool StartsWithScheme(string value, string scheme)
    {
        if (value.Length <= scheme.Length || value[scheme.Length] is not ':' || !char.IsAsciiLetter(value[0]))
            return false;

        return string.Compare(value, 0, scheme, 0, scheme.Length, StringComparison.OrdinalIgnoreCase) is 0;
    }

    /// <summary>Parses a DTSTART or a DTEND: a DATE-TIME value (RFC 5545 section 3.3.5), recording the time zone it names, or a DATE value (section 3.3.4).</summary>
    private static bool TryParseDateOrDateTimeProperty(ContentLine line, ref string? timeZoneId, out DateTime value, out bool isDate, out string? error)
    {
        var valueType = line.GetParameter("VALUE");
        isDate = string.Equals(valueType, "DATE", StringComparison.OrdinalIgnoreCase) || (valueType is null && line.Value.Length is 8);
        if (isDate)
        {
            // A DATE value denotes the whole day and is read as its first instant. A date without VALUE=DATE is
            // tolerated, as some producers omit the parameter.
            value = default;
            if (line.Value.Length is not 8 || !TryParseDateTime(line.Value, out value))
            {
                error = $"The {line.Name} value '{line.Value}' is not a date";
                return false;
            }

            error = null;
            return true;
        }

        if (!TryParseDateTimeWithTimeZoneId(line, out value, out var id, out error))
            return false;

        if (id is null)
            return true;

        if (timeZoneId is not null && !string.Equals(timeZoneId, id, StringComparison.Ordinal))
        {
            // An Event holds a single time zone, so it cannot describe a start and an end expressed in different ones.
            error = $"The properties of the event reference two different time zones, '{timeZoneId}' and '{id}'";
            return false;
        }

        timeZoneId = id;
        return true;
    }

    /// <summary>Parses a property RFC 5545 requires in UTC, such as DTSTAMP (section 3.8.7.2), as a <see cref="DateTimeKind.Utc"/> value.</summary>
    /// <remarks>
    /// A value carrying a TZID is converted from that time zone, and a floating value, or one whose time zone cannot be
    /// resolved, is taken as UTC. The time zone of such a property does not affect the time zone of the event.
    /// </remarks>
    private static bool TryParseUtcDateTimeProperty(ContentLine line, TimeZoneResolver timeZones, out DateTime value, out string? error)
    {
        if (!TryParseDateTimeWithTimeZoneId(line, out value, out var id, out error))
            return false;

        if (value.Kind is DateTimeKind.Utc)
            return true;

        if (id is not null && timeZones.Resolve(id) is { } timeZone)
        {
            if (Utilities.TryToDateTimeOffset(value, timeZone, out var instant) is not InstantConversion.Success)
            {
                error = $"The {line.Name} value '{line.Value}' in the time zone '{id}' is outside the range of DateTime in UTC";
                return false;
            }

            value = instant.UtcDateTime;
            return true;
        }

        value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return true;
    }

    private static bool TryParseDateTimeWithTimeZoneId(ContentLine line, out DateTime value, out string? timeZoneId, out string? error)
    {
        timeZoneId = null;
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

        timeZoneId = id;
        error = null;
        return true;
    }

    /// <summary>Parses a DATE (RFC 5545 section 3.3.4) or a DATE-TIME (section 3.3.5) value.</summary>
    internal static bool TryParseDateTime(string value, out DateTime result)
    {
        // A DATE value denotes the whole day and is read as its first instant.
        if (value.Length is 8)
            return DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

        // RFC 5545 section 3.3.12 allows a second of 60 for a leap second. DateTime cannot represent one, so it is read
        // as 59, as a BYSECOND=60 in a recurrence rule is.
        if (value.Length is 15 or 16 && value[13] is '6' && value[14] is '0')
        {
            value = value[..13] + "59" + value[15..];
        }

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

    /// <summary>Resolves the time zones the TZID parameters of a calendar name.</summary>
    /// <remarks>
    /// An identifier is looked up, in order, as a time zone of the platform, as a VTIMEZONE component of the calendar, and
    /// as the IANA identifier ending a prefixed one, such as <c>/mozilla.org/20050126_1/America/New_York</c>. A VTIMEZONE
    /// may follow the components referencing it, so the calendar is scanned for them up front.
    /// </remarks>
    internal sealed class TimeZoneResolver
    {
        private readonly Dictionary<string, TimeZoneInfo?> _cache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ContentLine>> _definitions = new(StringComparer.Ordinal);

        public TimeZoneResolver(List<ContentLine> lines)
        {
            var depth = 0;
            for (var i = 0; i < lines.Count; i++)
            {
                if (IsEnd(lines[i], out _))
                {
                    depth--;
                    continue;
                }

                if (!IsBegin(lines[i], out var component))
                    continue;

                depth++;
                if (depth is not 2 || component is not "VTIMEZONE")
                    continue;

                var body = new List<ContentLine>();
                string? id = null;
                var nesting = 0;
                for (i++; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (IsBegin(line, out _))
                    {
                        nesting++;
                    }
                    else if (IsEnd(line, out _))
                    {
                        if (nesting is 0)
                            break;

                        nesting--;
                    }
                    else if (nesting is 0 && string.Equals(line.Name, "TZID", StringComparison.OrdinalIgnoreCase))
                    {
                        id = line.GetTextValue();
                    }

                    body.Add(line);
                }

                depth--;

                // The first definition of an identifier wins.
                if (id is not null && !_definitions.ContainsKey(id))
                {
                    _definitions.Add(id, body);
                }
            }
        }

        /// <summary>Gets the content lines of the VTIMEZONE components of the calendar, delimiters excluded, by TZID.</summary>
        public Dictionary<string, List<ContentLine>>? Definitions => _definitions.Count is 0 ? null : _definitions;

        public TimeZoneInfo? Resolve(string id)
        {
            if (!_cache.TryGetValue(id, out var timeZone))
            {
                timeZone = ResolveCore(id);
                _cache.Add(id, timeZone);
            }

            return timeZone;
        }

        private TimeZoneInfo? ResolveCore(string id)
        {
            // An identifier the writer could not write back, as one holding a line feed encoded as ^n, is not resolved.
            if (!Utilities.IsValidTimeZoneId(id))
                return null;

            if (TimeZones.TryFindWithoutThrowing(id, out var timeZone))
                return timeZone;

            if (_definitions.TryGetValue(id, out var definition) && VTimeZoneReader.Create(id, definition) is { } custom)
                return custom;

            return ResolvePrefixedIdentifier(id);
        }

        /// <summary>Resolves a time zone of the platform, or the one whose identifier ends a prefixed identifier.</summary>
        public static TimeZoneInfo? ResolveWithoutDefinitions(string id)
        {
            if (!Utilities.IsValidTimeZoneId(id))
                return null;

            if (TimeZones.TryFindWithoutThrowing(id, out var timeZone))
                return timeZone;

            return ResolvePrefixedIdentifier(id);
        }

        private static TimeZoneInfo? ResolvePrefixedIdentifier(string id)
        {
            // A prefixed identifier, as Mozilla and other producers write, usually ends with an IANA identifier. The
            // longest suffix starting with a letter wins, so America/Argentina/Buenos_Aires is preferred to Buenos_Aires.
            var segments = id.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
            for (var count = segments.Length - 1; count >= 1; count--)
            {
                var first = segments.Length - count;
                if (!char.IsAsciiLetter(segments[first][0]))
                    continue;

                var candidate = string.Join("/", segments, first, count);
                if (TimeZones.TryFindWithoutThrowing(candidate, out var timeZone))
                    return timeZone;
            }

            return null;
        }
    }

    /// <summary>Consumes a component whose content the model does not represent, including the components nested in it.</summary>
    /// <remarks>Every nested component has to be terminated by an END naming it, as the components the model reads are.</remarks>
    private static bool TrySkipComponent(List<ContentLine> lines, ref int index, string component, out string? error)
    {
        var components = new Stack<string>();
        components.Push(component);
        while (index < lines.Count)
        {
            var line = lines[index];
            index++;

            if (IsBegin(line, out var begin))
            {
                components.Push(begin);
            }
            else if (IsEnd(line, out var end))
            {
                var current = components.Pop();
                if (!string.Equals(end, current, StringComparison.Ordinal))
                {
                    error = $"The {current} component is terminated by 'END:{end}'";
                    return false;
                }

                if (components.Count is 0)
                {
                    error = null;
                    return true;
                }
            }
        }

        error = $"The {components.Peek()} component is not terminated by 'END:{components.Peek()}'";
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
            // Trailing white space, which some producers leave, is not part of the component name.
            component = line.Value.TrimEnd(' ', '\t').ToUpperInvariant();
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
