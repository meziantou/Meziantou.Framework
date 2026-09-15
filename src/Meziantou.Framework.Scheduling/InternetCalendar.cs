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

    /// <summary>The names a VCALENDAR is written with, which an additional property cannot duplicate.</summary>
    private static readonly HashSet<string> CalendarPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BEGIN", "END", "VERSION", "PRODID",
    };

    /// <summary>The names a VEVENT is written with, which an additional property cannot duplicate.</summary>
    private static readonly HashSet<string> EventPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BEGIN", "END", "UID", "STATUS", "ORGANIZER", "ATTENDEE", "CREATED", "LAST-MODIFIED", "DTSTAMP", "DTSTART", "DTEND", "RRULE", "SUMMARY", "DESCRIPTION",
    };

    /// <summary>Gets additional custom properties for the calendar, each value written according to the value type of its property.</summary>
    /// <remarks>
    /// <para>A property whose value type is TEXT (RFC 5545 section 3.3.11) — an <c>X-</c> property, a property RFC 5545 does not
    /// define, or a TEXT property such as METHOD — has its value escaped when written, so it cannot hold a structured value such as
    /// a list or a parameter; use <see cref="RawProperties"/> for those. The value of a property whose value type is not TEXT, such
    /// as SOURCE or REFRESH-INTERVAL, is written as is, its control characters dropped.</para>
    /// <para>The parser stores a property here only when it carries no parameter, occurs once, has a single TEXT value, and that
    /// value is written back unchanged.</para>
    /// <para>A property whose name is not a valid property name, or is BEGIN, END, VERSION or PRODID, is not written.</para>
    /// </remarks>
    public IDictionary<string, string> AdditionalProperties { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the properties of the calendar that are written verbatim, in order, after <see cref="AdditionalProperties"/>.</summary>
    /// <remarks>
    /// <para>The parser stores here, as written, every calendar property the model does not represent and that
    /// <see cref="AdditionalProperties"/> does not hold: a property with parameters, a repeated one, one whose value type is not
    /// TEXT, or a TEXT value TEXT escaping would not write back unchanged.</para>
    /// <para>A property named BEGIN, END, VERSION or PRODID is not written.</para>
    /// </remarks>
    public IList<InternetCalendarProperty> RawProperties { get; } = new List<InternetCalendarProperty>();

    /// <summary>Gets the events in this calendar.</summary>
    public IList<Event> Events { get; } = new List<Event>();

    /// <summary>Gets or sets the iCalendar version.</summary>
    /// <remarks>
    /// The value is <c>vers</c> or <c>minver;maxver</c> (RFC 5545 section 3.7.4), so it is read and written as is rather than
    /// as TEXT. A version containing a control character other than a tab cannot be written, and <see cref="ToIcs()"/> throws
    /// an <see cref="InvalidOperationException"/> before writing anything.
    /// </remarks>
    public string Version { get; set; } = "2.0";

    /// <summary>Gets or sets the content lines of the VTIMEZONE components the calendar was parsed from, delimiters excluded, by TZID.</summary>
    internal Dictionary<string, List<ContentLine>>? TimeZoneDefinitions { get; set; }

    /// <summary>Writes the calendar to a stream in iCalendar format.</summary>
    /// <param name="stream">The stream to write to.</param>
    /// <remarks>Nothing is written when the calendar cannot be written: see <see cref="ToIcs(TextWriter)"/>.</remarks>
    public void ToIcs(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var ics = ToIcs();
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using TextWriter writer = new StreamWriter(stream, encoding, bufferSize: 1024, leaveOpen: true);
        writer.Write(ics);
    }

    /// <summary>Writes the calendar to a text writer in iCalendar format.</summary>
    /// <param name="writer">The text writer to write to.</param>
    /// <remarks>
    /// <para>Content lines longer than 75 octets are folded, as RFC 5545 section 3.1 recommends.</para>
    /// <para>The calendar is formatted before anything is written, so a value that cannot be written, such as a
    /// <see cref="Event.Status"/> that is not a member of <see cref="EventStatus"/>, throws an
    /// <see cref="InvalidOperationException"/> without producing partial output.</para>
    /// </remarks>
    public void ToIcs(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(ToIcs());
    }

    /// <summary>Converts the calendar to an iCalendar format string.</summary>
    /// <returns>The iCalendar format string representation of this calendar.</returns>
    public string ToIcs()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Write(writer);
        return writer.ToString();
    }

    private void Write(TextWriter writer)
    {
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

        var timeZones = GetTimeZones();

        // RFC 5545 section 3.7.4: the value is "vers" or "minver;maxver", which TEXT escaping would alter, so it is written
        // as is. A control character would let it start a property of its own, so it is rejected rather than altered.
        var version = Version;
        if (!string.IsNullOrEmpty(version) && !InternetCalendarProperty.IsValidValue(version))
            throw new InvalidOperationException("The iCalendar version contains a control character");

        Utilities.WriteLine(writer, "BEGIN:VCALENDAR");
        if (!string.IsNullOrEmpty(version))
            Utilities.WriteLine(writer, "VERSION:" + version);

        // PRODID is REQUIRED in a VCALENDAR (RFC 5545 section 3.6).
        Utilities.WriteLine(writer, "PRODID:" + ProductIdentifier);

        WriteAdditionalProperties(writer, AdditionalProperties, RawProperties, CalendarPropertyNames.Contains, CalendarPropertyNames.Contains);

        // A VTIMEZONE must precede the components referencing its TZID.
        foreach (var timeZone in timeZones)
        {
            WriteTimeZone(writer, timeZone);
        }

        foreach (var @event in Events)
        {
            // As for the attendees, and as GetTimeZones does, a null entry is skipped.
            if (@event is null)
                continue;

            Utilities.WriteLine(writer, "BEGIN:VEVENT");
            if (!string.IsNullOrEmpty(@event.Id))
                WriteProperty(writer, "UID", @event.IdParameters, skipParameter: null, Utilities.EscapeText(@event.Id));

            if (@event.Status is { } status)
                WriteProperty(writer, "STATUS", @event.StatusParameters, skipParameter: null, GetStatusValue(status));

            if (@event.Organizer is { Address: { } organizerAddress } organizer)
                WriteProperty(writer, "ORGANIZER", organizer.Parameters, skipParameter: null, organizerAddress.GetContentLineValue());

            foreach (var attendee in @event.Attendees)
            {
                // An empty ATTENDEE is not a calendar user address, so, as for the organizer, an attendee without one is skipped.
                if (attendee?.Address is null)
                    continue;

                WriteProperty(writer, "ATTENDEE", attendee.Parameters, skipParameter: null, attendee.Address.GetContentLineValue());
            }

            // RFC 5545 sections 3.8.7.1 to 3.8.7.3 require these values in UTC.
            if (@event.Created != default)
                WriteProperty(writer, "CREATED", @event.CreatedParameters, InternetCalendarParser.IsDateTimeParameterSetByTheWriter, Utilities.UtcDateTimeToString(@event.Created));

            if (@event.LastModified != default)
                WriteProperty(writer, "LAST-MODIFIED", @event.LastModifiedParameters, InternetCalendarParser.IsDateTimeParameterSetByTheWriter, Utilities.UtcDateTimeToString(@event.LastModified));

            if (@event.DateTimeStamp != default)
                WriteProperty(writer, "DTSTAMP", @event.DateTimeStampParameters, InternetCalendarParser.IsDateTimeParameterSetByTheWriter, Utilities.UtcDateTimeToString(@event.DateTimeStamp));

            var timeZone = @event.IsAllDay ? null : @event.TimeZone;
            if (@event.Start != default)
                WriteDateTimeProperty(writer, "DTSTART", @event.StartParameters, @event.Start, @event.IsAllDay, timeZone);

            if (@event.End != default)
            {
                // RFC 5545 section 3.6.1: an event has either DTEND or DURATION. The DURATION the end was read from is kept as
                // long as it still describes the end.
                if (@event.Duration is { } duration && duration.TryGetEnd(@event.Start, @event.IsAllDay, timeZone, out var end) && end == @event.End && end.Kind == @event.End.Kind)
                {
                    WriteProperty(writer, "DURATION", @event.EndParameters, InternetCalendarParser.IsDateTimeParameterSetByTheWriter, duration.Text);
                }
                else
                {
                    WriteDateTimeProperty(writer, "DTEND", @event.EndParameters, @event.End, @event.IsAllDay, timeZone);
                }
            }

            if (@event.RecurrenceRule is not null)
                WriteProperty(writer, "RRULE", @event.RecurrenceRuleParameters, skipParameter: null, GetRecurrenceRuleValue(@event.RecurrenceRule, @event, timeZone));

            if (!string.IsNullOrEmpty(@event.Summary))
                WriteProperty(writer, "SUMMARY", @event.SummaryParameters, skipParameter: null, Utilities.EscapeText(@event.Summary));

            WriteAdditionalProperties(writer, @event.AdditionalProperties, @event.RawProperties, name => IsReservedEventPropertyName(@event, name, isRawProperty: false), name => IsReservedEventPropertyName(@event, name, isRawProperty: true));

            if (@event.Description is { } description)
                WriteProperty(writer, "DESCRIPTION", @event.DescriptionParameters, skipParameter: null, Utilities.EscapeText(description));

            Utilities.WriteLine(writer, "END:VEVENT");
        }

        Utilities.WriteLine(writer, "END:VCALENDAR");
    }

    /// <summary>Gets a value indicating whether a property of <see cref="Event.AdditionalProperties"/> or <see cref="Event.RawProperties"/> is not written, as the event writes a property with that name itself.</summary>
    /// <remarks>A raw RRULE is written, as the parser keeps there the recurrence rules following the first one.</remarks>
    private static bool IsReservedEventPropertyName(Event @event, string name, bool isRawProperty)
    {
        if (string.Equals(name, "DURATION", StringComparison.OrdinalIgnoreCase))
            return @event.End != default;

        if (isRawProperty && string.Equals(name, "RRULE", StringComparison.OrdinalIgnoreCase))
            return false;

        return EventPropertyNames.Contains(name);
    }

    private static string GetStatusValue(EventStatus status)
    {
        if (status is not (EventStatus.Tentative or EventStatus.Confirmed or EventStatus.Cancelled))
            throw new InvalidOperationException($"The event status '{status}' is not a member of {nameof(EventStatus)}");

        return Utilities.StatusToString(status);
    }

    /// <summary>Collects the VTIMEZONE components the calendar needs (RFC 5545 section 3.2.19): one for the time zone of each event, and one for each TZID parameter of a written property that resolves.</summary>
    /// <remarks>
    /// A TZID that names a VTIMEZONE of the calendar the model was parsed from keeps that component as written. Any other one is
    /// looked up as the parser resolves it, as a time zone of the platform or as the one ending a prefixed identifier.
    /// </remarks>
    private List<TimeZoneComponent> GetTimeZones()
    {
        var result = new List<TimeZoneComponent>();
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var property in RawProperties)
        {
            if (property is not null && !CalendarPropertyNames.Contains(property.Name))
            {
                AddReferencedTimeZones(property.Parameters, property.Value, fallbackReferenceDate: null);
            }
        }

        foreach (var @event in Events)
        {
            if (@event is null)
                continue;

            // An all-day event is written with DATE values, which do not reference its time zone.
            if (@event.TimeZone is { } timeZone && !@event.IsAllDay)
            {
                if (!Utilities.IsValidTimeZoneId(timeZone.Id))
                    throw new InvalidOperationException($"The time zone identifier '{timeZone.Id}' cannot be written as a TZID property parameter");

                var referenceDate = Utilities.ToWallClock(@event.Start, timeZone);
                if (indexes.TryGetValue(timeZone.Id, out var index))
                {
                    // The time zone of an event is described by the component the writer builds for it.
                    var existing = result[index];
                    existing.ReferenceDate = Min(existing.ReferenceDate, referenceDate);
                    existing.TimeZone = timeZone;
                    existing.Definition = null;
                }
                else
                {
                    indexes.Add(timeZone.Id, result.Count);
                    result.Add(new TimeZoneComponent(timeZone.Id) { TimeZone = timeZone, ReferenceDate = referenceDate });
                }
            }

            DateTime? eventReferenceDate = @event.Start == default ? null : @event.Start;
            if (!string.IsNullOrEmpty(@event.Id))
                AddReferencedTimeZones(@event.IdParameters, value: null, eventReferenceDate);

            if (@event.Status is not null)
                AddReferencedTimeZones(@event.StatusParameters, value: null, eventReferenceDate);

            if (@event.Organizer is { Address: not null } organizer)
                AddReferencedTimeZones(organizer.Parameters, value: null, eventReferenceDate);

            foreach (var attendee in @event.Attendees)
            {
                if (attendee?.Address is not null)
                    AddReferencedTimeZones(attendee.Parameters, value: null, eventReferenceDate);
            }

            if (@event.RecurrenceRule is not null)
                AddReferencedTimeZones(@event.RecurrenceRuleParameters, value: null, eventReferenceDate);

            if (!string.IsNullOrEmpty(@event.Summary))
                AddReferencedTimeZones(@event.SummaryParameters, value: null, eventReferenceDate);

            if (@event.Description is not null)
                AddReferencedTimeZones(@event.DescriptionParameters, value: null, eventReferenceDate);

            foreach (var property in @event.RawProperties)
            {
                if (property is not null && !IsReservedEventPropertyName(@event, property.Name, isRawProperty: true))
                {
                    AddReferencedTimeZones(property.Parameters, property.Value, eventReferenceDate);
                }
            }
        }

        return result;

        void AddReferencedTimeZones(IEnumerable<KeyValuePair<string, string>> parameters, string? value, DateTime? fallbackReferenceDate)
        {
            foreach (var parameter in parameters)
            {
                if (!string.Equals(parameter.Key, "TZID", StringComparison.OrdinalIgnoreCase))
                    continue;

                var id = InternetCalendarProperty.GetSingleParameterValue(parameter.Value);
                var referenceDate = GetReferenceDate(value) ?? fallbackReferenceDate ?? new DateTime(1970, 1, 1);
                if (indexes.TryGetValue(id, out var index))
                {
                    result[index].ReferenceDate = Min(result[index].ReferenceDate, referenceDate);
                    continue;
                }

                var component = new TimeZoneComponent(id) { ReferenceDate = referenceDate };
                if (TimeZoneDefinitions is not null && TimeZoneDefinitions.TryGetValue(id, out var definition))
                {
                    component.Definition = definition;
                }
                else if (InternetCalendarParser.TimeZoneResolver.ResolveWithoutDefinitions(id) is { } timeZone)
                {
                    component.TimeZone = timeZone;
                }
                else
                {
                    // RFC 5545 section 3.2.19 requires a VTIMEZONE, but nothing describes this time zone.
                    continue;
                }

                indexes.Add(id, result.Count);
                result.Add(component);
            }
        }

        static DateTime? GetReferenceDate(string? value)
        {
            if (value is null)
                return null;

            // A DATE, DATE-TIME or PERIOD list, as the value of EXDATE, RDATE or RECURRENCE-ID is.
            DateTime? result = null;
            foreach (var item in value.Split(','))
            {
                var slash = item.IndexOf('/', StringComparison.Ordinal);
                var start = slash < 0 ? item : item[..slash];
                if (InternetCalendarParser.TryParseDateTime(start, out var date))
                {
                    result = result is { } current ? Min(current, date) : date;
                }
            }

            return result;
        }

        static DateTime Min(DateTime left, DateTime right) => right.Ticks < left.Ticks ? right : left;
    }

    private static void WriteTimeZone(TextWriter writer, TimeZoneComponent component)
    {
        if (component.Definition is not null)
        {
            Utilities.WriteLine(writer, "BEGIN:VTIMEZONE");
            foreach (var line in component.Definition)
            {
                InternetCalendarProperty.FromContentLine(line).Write(writer);
            }

            Utilities.WriteLine(writer, "END:VTIMEZONE");
            return;
        }

        var timeZone = component.TimeZone!;
        if (string.Equals(timeZone.Id, component.Id, StringComparison.Ordinal))
        {
            VTimeZoneWriter.Write(writer, timeZone, component.ReferenceDate);
            return;
        }

        // The time zone was resolved from a prefixed identifier, such as /mozilla.org/20050126_1/America/New_York, which the
        // component has to be named after for the TZID parameter to reference it.
        using var buffer = new StringWriter(CultureInfo.InvariantCulture);
        VTimeZoneWriter.Write(buffer, timeZone, component.ReferenceDate);
        var text = buffer.ToString();
        var originalLine = FormatLine("TZID:" + Utilities.EscapeText(timeZone.Id));
        var index = text.IndexOf(originalLine, StringComparison.Ordinal);
        if (index >= 0)
        {
            text = text[..index] + FormatLine("TZID:" + Utilities.EscapeText(component.Id)) + text[(index + originalLine.Length)..];
        }

        writer.Write(text);

        static string FormatLine(string line)
        {
            using var lineWriter = new StringWriter(CultureInfo.InvariantCulture);
            Utilities.WriteLine(lineWriter, line);
            return lineWriter.ToString();
        }
    }

    /// <summary>Writes a date-time property, using the TZID form (RFC 5545 section 3.3.5) when the event has a time zone, or a DATE value (section 3.3.4).</summary>
    private static void WriteDateTimeProperty(TextWriter writer, string name, IEnumerable<KeyValuePair<string, string>> parameters, DateTime value, bool isDate, TimeZoneInfo? timeZone)
    {
        if (isDate)
        {
            WriteProperty(writer, name + ";VALUE=DATE", parameters, InternetCalendarParser.IsDateTimeParameterSetByTheWriter, value.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            return;
        }

        if (timeZone is null)
        {
            WriteProperty(writer, name, parameters, InternetCalendarParser.IsDateTimeParameterSetByTheWriter, Utilities.DateTimeToString(value));
            return;
        }

        // The identifier was validated before any output was written.
        var wallClock = Utilities.ToWallClock(value, timeZone);
        WriteProperty(writer, name + ";TZID=" + Utilities.TimeZoneIdToParameterValue(timeZone.Id), parameters, InternetCalendarParser.IsDateTimeParameterSetByTheWriter, wallClock.ToString(Utilities.FloatingDateTimeFormat, CultureInfo.InvariantCulture));
    }

    /// <summary>Gets the RRULE value, whose UNTIL has the value type of the DTSTART the event is written with.</summary>
    /// <remarks>
    /// <para>RFC 5545 section 3.3.10: when DTSTART is a DATE, UNTIL must be a DATE; when DTSTART is a floating date-time, UNTIL
    /// must be floating; and when DTSTART carries a TZID or is in UTC, UNTIL must be a UTC date-time.</para>
    /// <para>A value of another form is converted, keeping the bound it denotes: a wall-clock UNTIL is read in the frame of the
    /// start (its time zone, UTC, or the local time zone); an instant is read as a wall clock in that frame, or by its own
    /// reading for a floating start, as <see cref="RecurrenceRule.GetNextOccurrences(DateTime)"/> compares them; and a DATE
    /// bounds the occurrences through the end of that day. A UTC value outside the range of <see cref="DateTime"/> is clamped
    /// to that range.</para>
    /// <para>The recurrence rule itself is not modified.</para>
    /// </remarks>
    private static string GetRecurrenceRuleValue(RecurrenceRule recurrenceRule, Event @event, TimeZoneInfo? timeZone)
    {
        var text = recurrenceRule.Text;
        if (recurrenceRule.EndDate is not { } endDate || recurrenceRule.EndDateText is not { } written)
            return text;

        var until = GetUntilValue(endDate, recurrenceRule.IsEndDateDate, @event, timeZone);
        if (string.Equals(until, written, StringComparison.Ordinal))
            return text;

        // The replaced token is a fixed-length value this library itself produced, so the substitution is unambiguous.
        return text.Replace(";UNTIL=" + written, ";UNTIL=" + until, StringComparison.Ordinal);
    }

    private static string GetUntilValue(DateTime endDate, bool isDate, Event @event, TimeZoneInfo? timeZone)
    {
        // The last second of a day, which a DATE UNTIL includes; DTSTART has no finer precision.
        var endOfDay = new TimeSpan(23, 59, 59);

        if (@event.IsAllDay)
        {
            // Only the date part of the start is written, whatever its kind, so an instant is reduced to a date in the
            // frame that kind denotes.
            var date = isDate ? endDate : ToWallClock(endDate, GetFrame(@event.Start.Kind));
            return date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        var frame = timeZone ?? GetFrame(@event.Start.Kind);
        if (frame is null)
        {
            // A floating start compares the reading of an instant with its own reading.
            var wallClock = isDate ? endDate.Date + endOfDay : endDate;
            return wallClock.ToString(Utilities.FloatingDateTimeFormat, CultureInfo.InvariantCulture);
        }

        var utc = endDate.Kind switch
        {
            DateTimeKind.Utc => endDate,
            DateTimeKind.Local => Utilities.LocalToUniversalTime(endDate),

            // The bound has to be read the same way the occurrences it bounds are, so UNTIL goes through the
            // RFC 5545 section 3.3.5 disambiguation rather than TimeZoneInfo.ConvertTimeToUtc, which throws on a
            // time inside the gap of a forward transition and resolves an ambiguous one to its second occurrence.
            // An instant outside the range of DateTime bounds nothing within it, so the bound is clamped to that range.
            _ => Utilities.TryToDateTimeOffset(isDate ? endDate.Date + endOfDay : endDate, frame, out var instant) switch
            {
                InstantConversion.Success => instant.UtcDateTime,
                InstantConversion.BeforeMinValue => DateTime.MinValue,
                _ => DateTime.MaxValue,
            },
        };

        return utc.ToString(Utilities.UtcDateTimeFormat, CultureInfo.InvariantCulture);

        static TimeZoneInfo? GetFrame(DateTimeKind kind) => kind switch
        {
            DateTimeKind.Utc => TimeZoneInfo.Utc,
            DateTimeKind.Local => TimeZoneInfo.Local,
            _ => null,
        };

        static DateTime ToWallClock(DateTime value, TimeZoneInfo? frame) => frame is null ? value : Utilities.ToWallClock(value, frame);
    }

    private static void WriteAdditionalProperties(TextWriter writer, IDictionary<string, string> additionalProperties, IList<InternetCalendarProperty> rawProperties, Func<string, bool> isReservedAdditionalPropertyName, Func<string, bool> isReservedRawPropertyName)
    {
        foreach (var additionalProperty in additionalProperties)
        {
            // A name outside the iCalendar grammar cannot be written as a content line, and a name carrying a line break
            // would start an attacker-chosen property. A reserved name would duplicate a property the model writes, or,
            // for BEGIN and END, open or close a component.
            if (!InternetCalendarProperty.IsValidName(additionalProperty.Key) || isReservedAdditionalPropertyName(additionalProperty.Key))
                continue;

            // RFC 5545 section 3.8.8.2: the default value type of a non-standard property is TEXT. The value of a property
            // whose value type is not TEXT, such as a URI, has no escaping, so only the characters it cannot hold are dropped.
            var value = InternetCalendarProperty.IsTextProperty(additionalProperty.Key) ? Utilities.EscapeText(additionalProperty.Value) : RemoveControlCharacters(additionalProperty.Value);
            Utilities.WriteLine(writer, additionalProperty.Key + ':' + value);
        }

        foreach (var rawProperty in rawProperties)
        {
            // The property validated its name, parameters and value when it was created.
            if (rawProperty is null || isReservedRawPropertyName(rawProperty.Name))
                continue;

            rawProperty.Write(writer);
        }

        static string RemoveControlCharacters(string? value)
        {
            if (value is null)
                return "";

            if (InternetCalendarProperty.IsValidValue(value))
                return value;

            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                if (c is '\t' || (c >= 0x20 && c != 0x7F))
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>Writes a content line, whose parameters are encoded and whose value is written as given.</summary>
    private static void WriteProperty(TextWriter writer, string nameAndParameters, IEnumerable<KeyValuePair<string, string>> parameters, Func<string, bool>? skipParameter, string value)
    {
        var sb = new StringBuilder(nameAndParameters);
        InternetCalendarProperty.AppendParameters(sb, parameters, skipParameter);
        sb.Append(':').Append(value);
        Utilities.WriteLine(writer, sb.ToString());
    }

    /// <summary>A VTIMEZONE component to write: the component of a parsed calendar, as written, or the one describing a time zone.</summary>
    private sealed class TimeZoneComponent(string id)
    {
        /// <summary>Gets the TZID the component is written with.</summary>
        public string Id { get; } = id;

        /// <summary>Gets or sets the time zone the component describes, when it is not written as parsed.</summary>
        public TimeZoneInfo? TimeZone { get; set; }

        /// <summary>Gets or sets the content lines of the component as parsed, delimiters excluded.</summary>
        public List<ContentLine>? Definition { get; set; }

        /// <summary>Gets or sets the earliest date the time zone is used for, which anchors the transitions written.</summary>
        public DateTime ReferenceDate { get; set; }
    }

    /// <summary>Parses an iCalendar object (RFC 5545).</summary>
    /// <param name="ics">The iCalendar content to parse.</param>
    /// <returns>The parsed calendar.</returns>
    /// <exception cref="FormatException">The content is not a valid iCalendar object.</exception>
    public static InternetCalendar Parse(string ics)
    {
        return Parse(ics.AsSpan());
    }

    /// <summary>Parses an iCalendar object (RFC 5545).</summary>
    /// <param name="ics">The iCalendar content to parse.</param>
    /// <returns>The parsed calendar.</returns>
    /// <exception cref="FormatException">The content is not a valid iCalendar object.</exception>
    public static InternetCalendar Parse(ReadOnlySpan<char> ics)
    {
        if (!TryParse(ics, out var calendar, out var error))
            throw new FormatException("The iCalendar content is invalid: " + error);

        return calendar;
    }

    /// <summary>Parses an iCalendar object (RFC 5545) read from a stream as UTF-8.</summary>
    /// <param name="stream">The stream to read the iCalendar content from.</param>
    /// <returns>The parsed calendar.</returns>
    /// <exception cref="FormatException">The content is not a valid iCalendar object.</exception>
    public static InternetCalendar Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // RFC 5545 section 6 makes UTF-8 the default charset; a UTF-16 or UTF-32 byte order mark is honoured.
        if (!InternetCalendarParser.TryParse(stream, out var calendar, out var error))
            throw new FormatException("The iCalendar content is invalid: " + error);

        return calendar;
    }

    /// <summary>Parses an iCalendar object (RFC 5545).</summary>
    /// <param name="reader">The text reader to read the iCalendar content from.</param>
    /// <returns>The parsed calendar.</returns>
    /// <exception cref="FormatException">The content is not a valid iCalendar object.</exception>
    public static InternetCalendar Parse(TextReader reader)
    {
        if (!TryParse(reader, out var calendar, out var error))
            throw new FormatException("The iCalendar content is invalid: " + error);

        return calendar;
    }

    /// <summary>Attempts to parse an iCalendar object (RFC 5545).</summary>
    /// <param name="ics">The iCalendar content to parse.</param>
    /// <param name="calendar">When successful, contains the parsed calendar.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> ics, [NotNullWhen(returnValue: true)] out InternetCalendar? calendar)
    {
        return TryParse(ics, out calendar, out _);
    }

    /// <summary>Attempts to parse an iCalendar object (RFC 5545).</summary>
    /// <param name="ics">The iCalendar content to parse.</param>
    /// <param name="calendar">When successful, contains the parsed calendar.</param>
    /// <param name="error">When parsing fails, contains the error message.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> ics, [NotNullWhen(returnValue: true)] out InternetCalendar? calendar, out string? error)
    {
        return InternetCalendarParser.TryParse(ics, out calendar, out error);
    }

    /// <summary>Attempts to parse an iCalendar object (RFC 5545).</summary>
    /// <param name="ics">The iCalendar content to parse.</param>
    /// <param name="calendar">When successful, contains the parsed calendar.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse([NotNullWhen(returnValue: true)] string? ics, [NotNullWhen(returnValue: true)] out InternetCalendar? calendar)
    {
        return TryParse(ics.AsSpan(), out calendar, out _);
    }

    /// <summary>Attempts to parse an iCalendar object (RFC 5545).</summary>
    /// <param name="ics">The iCalendar content to parse.</param>
    /// <param name="calendar">When successful, contains the parsed calendar.</param>
    /// <param name="error">When parsing fails, contains the error message.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse([NotNullWhen(returnValue: true)] string? ics, [NotNullWhen(returnValue: true)] out InternetCalendar? calendar, out string? error)
    {
        return TryParse(ics.AsSpan(), out calendar, out error);
    }

    /// <summary>Attempts to parse an iCalendar object (RFC 5545).</summary>
    /// <param name="reader">The text reader to read the iCalendar content from.</param>
    /// <param name="calendar">When successful, contains the parsed calendar.</param>
    /// <param name="error">When parsing fails, contains the error message.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(TextReader reader, [NotNullWhen(returnValue: true)] out InternetCalendar? calendar, out string? error)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return InternetCalendarParser.TryParse(reader, out calendar, out error);
    }
}
