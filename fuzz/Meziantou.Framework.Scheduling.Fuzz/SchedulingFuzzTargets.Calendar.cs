using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Scheduling.Fuzz;

public static partial class SchedulingFuzzTargets
{
    /// <summary>The number of occurrences of the recurrence of an event that are enumerated.</summary>
    private const int MaxEventOccurrences = 20;

    // Beyond this many days, any duration exceeds the range of DateTime (InternetCalendarDuration.MaxDays)
    private const long MaxDurationDays = 3_652_059;

    private static readonly DateTimeOffset[] TimeZoneProbes =
    [
        new(1601, 01, 01, 00, 00, 00, TimeSpan.Zero),
        new(1970, 01, 01, 00, 00, 00, TimeSpan.Zero),
        new(2000, 03, 26, 01, 30, 00, TimeSpan.Zero),
        new(2024, 01, 15, 12, 00, 00, TimeSpan.Zero),
        new(2024, 03, 31, 01, 00, 00, TimeSpan.Zero),
        new(2024, 07, 15, 12, 00, 00, TimeSpan.Zero),
        new(2024, 10, 27, 01, 00, 00, TimeSpan.Zero),
        new(2100, 06, 01, 00, 00, 00, TimeSpan.Zero),
        new(3000, 07, 15, 00, 00, 00, TimeSpan.Zero),
        new(5000, 01, 15, 00, 00, 00, TimeSpan.Zero),
        new(7000, 07, 15, 00, 00, 00, TimeSpan.Zero),
        new(9998, 07, 15, 00, 00, 00, TimeSpan.Zero),
        new(9999, 12, 30, 00, 00, 00, TimeSpan.Zero),
    ];

    private static string[] CalendarTokens =>
    [
        "BEGIN:VCALENDAR\r\n", "END:VCALENDAR\r\n", "BEGIN:VEVENT\r\n", "END:VEVENT\r\n", "BEGIN:VTIMEZONE\r\n", "END:VTIMEZONE\r\n", "BEGIN:STANDARD\r\n", "END:STANDARD\r\n",
        "BEGIN:DAYLIGHT\r\n", "END:DAYLIGHT\r\n", "BEGIN:VALARM\r\n", "END:VALARM\r\n", "BEGIN:VTODO\r\n", "END:VTODO\r\n", "VERSION:2.0\r\n", "PRODID:-//Test//Test//EN\r\n",
        "TZID:", "TZOFFSETFROM:+0100\r\n", "TZOFFSETTO:-0500\r\n", "TZOFFSETTO:+1400\r\n", "TZOFFSETFROM:-123000\r\n", "TZNAME:", "TZURL:", "RDATE:", "EXDATE:", "RECURRENCE-ID:",
        "DTSTART:", "DTSTART;TZID=", "DTSTART;VALUE=DATE:", "DTEND:", "DTEND;TZID=", "DURATION:", "PT1H", "P1D", "P2W", "RRULE:FREQ=YEARLY;BYDAY=-1SU;BYMONTH=10", "RRULE:",
        "UID:", "SUMMARY:", "DESCRIPTION:", "LOCATION:", "STATUS:", "CONFIRMED", "TENTATIVE", "CANCELLED", "ORGANIZER:mailto:a@example.com", "ATTENDEE;CN=", "mailto:",
        "CREATED:", "LAST-MODIFIED:", "DTSTAMP:", "CATEGORIES:", "GEO:", "X-WR-CALNAME:", "X-", ";VALUE=DATE", ";VALUE=DATE-TIME", ";TZID=", ";CN=", "\"", ";", ":", ",", "=",
        "\r\n", "\r\n ", "\r\n\t", "\n", "\r", "\\n", "\\N", "\\,", "\\;", "\\\\", "^^", "^n", "^'", "20240101T090000", "20240101T090000Z", "20240101", "16010101T020000",
        "00010101T000000", "99991231T235959", "T235960", "Z", "Europe/Paris", "America/New_York", "/mozilla.org/20050126_1/America/New_York", "UTC", "Etc/GMT+12", "\uFEFF",
    ];

    private static string[] ContentLineTokens =>
    [
        ";", ":", "=", ",", "\"", "^^", "^n", "^'", "^", "\t", "\n", "TZID=", "VALUE=DATE", "CN=", "X-NAME", "DTSTART", "SUMMARY", "ATTENDEE", "mailto:a@example.com",
    ];

    private static string[] DurationTokens => ["P", "T", "W", "D", "H", "M", "S", "+", "-", "PT1H", "P1D", "P1W", "P1DT2H3M4S", "3652059", "521722", "315537897600", "0"];

    /// <summary>
    /// Every parse overload (string, span, <see cref="TextReader"/>, UTF-8 <see cref="Stream"/>) agrees, and an invalid
    /// calendar only fails with <see cref="FormatException"/>. A parsed calendar can be written, by every overload alike, and
    /// the written calendar parses back to the same events and writes back to the same text. The recurrence of each event
    /// is evaluated, bounded, in the time zone of the event, and gives the same occurrences once written and parsed back. The
    /// time zones built from VTIMEZONE components answer the usual queries.
    /// </summary>
    private static void Calendar(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        var parsed = InternetCalendar.TryParse(text, out var calendar, out var error);
        FuzzAssert.Equal(parsed, InternetCalendar.TryParse(text, out _), "TryParse(string) with and without the error disagree");
        FuzzAssert.Equal(parsed, InternetCalendar.TryParse(text.AsSpan(), out var spanCalendar, out var spanError), "TryParse(string) and TryParse(ReadOnlySpan<char>) disagree");
        FuzzAssert.Equal(error, spanError, "TryParse(string) and TryParse(ReadOnlySpan<char>) report different errors");
        FuzzAssert.Equal(parsed, InternetCalendar.TryParse(text.AsSpan(), out _), "TryParse(ReadOnlySpan<char>) with and without the error disagree");
        FuzzAssert.Equal(parsed, InternetCalendar.TryParse(new StringReader(text), out var readerCalendar, out var readerError), "TryParse(string) and TryParse(TextReader) disagree");

        // A stream is read as UTF-8, which cannot hold an unpaired surrogate
        InternetCalendar? streamCalendar = null;
        var hasStream = !HasLoneSurrogate(text);
        if (hasStream)
        {
            try
            {
                streamCalendar = InternetCalendar.Parse(new MemoryStream(Encoding.UTF8.GetBytes(text)));
            }
            catch (FormatException ex)
            {
                FuzzAssert.False(parsed, $"Parse(Stream) rejects a calendar TryParse(string) accepts: {ex.Message}");
            }

            FuzzAssert.True(!parsed || streamCalendar is not null, "Parse(Stream) accepts a calendar TryParse(string) rejects");
        }

        if (!parsed)
        {
            FuzzAssert.True(error is not null && readerError is not null, "TryParse returned false without an error");
            ExpectFormatException(() => InternetCalendar.Parse(text), "Parse(string)");
            ExpectFormatException(() => InternetCalendar.Parse(text.AsSpan()), "Parse(ReadOnlySpan<char>)");
            ExpectFormatException(() => InternetCalendar.Parse(new StringReader(text)), "Parse(TextReader)");
            return;
        }

        FuzzAssert.True(calendar is not null && spanCalendar is not null && readerCalendar is not null && error is null && readerError is null, "TryParse returned true with an error or without a calendar");

        // A parsed calendar can always be written, and the overloads agree
        var ics = calendar.ToIcs();
        FuzzAssert.Equal(ics, spanCalendar.ToIcs(), "The calendars parsed from a string and from a span differ");
        FuzzAssert.Equal(ics, readerCalendar.ToIcs(), "The calendars parsed from a string and from a TextReader differ");
        FuzzAssert.Equal(ics, InternetCalendar.Parse(text).ToIcs(), "Parse and TryParse give different calendars");
        if (streamCalendar is not null)
        {
            FuzzAssert.Equal(ics, streamCalendar.ToIcs(), "The calendars parsed from a string and from a stream differ");
        }

        using (var writer = new StringWriter(CultureInfo.InvariantCulture))
        {
            calendar.ToIcs(writer);
            FuzzAssert.Equal(ics, writer.ToString(), "ToIcs(TextWriter) and ToIcs() differ");
        }

        using (var stream = new MemoryStream())
        {
            calendar.ToIcs(stream);
            FuzzAssert.True(stream.ToArray().AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(ics)), "ToIcs(Stream) is not the UTF-8 encoding of ToIcs()");
        }

        CheckWrittenLines(ics);

        // The written calendar is read back as the same calendar, and written back the same way
        if (!InternetCalendar.TryParse(ics, out var reparsed, out var reparseError))
            FuzzAssert.Fail($"The written calendar does not parse back: {reparseError}{Environment.NewLine}{FuzzAssert.Format(ics)}");

        // The VTIMEZONE sub-components are grouped by whether TimeZoneInfo.IsDaylightSavingTime reports daylight saving time,
        // which .NET 10 can report differently for time zones with the same UTC offsets, such as one whose daylight saving time
        // ends with the year. The VTIMEZONE components are then only compared through the offsets they describe, below.
#if NET11_0_OR_GREATER
        FuzzAssert.Equal(ics, reparsed.ToIcs(), "Writing the calendar parsed from the written calendar gives another text");
#else
        FuzzAssert.Equal(RemoveTimeZones(ics), RemoveTimeZones(reparsed.ToIcs()), "Writing the calendar parsed from the written calendar gives another text");
#endif
        FuzzAssert.Equal(DescribeCalendar(calendar), DescribeCalendar(reparsed), "The calendar parsed from the written calendar differs");

        for (var i = 0; i < calendar.Events.Count; i++)
        {
            var @event = calendar.Events[i];
            var occurrences = CheckEventOccurrences(@event, i);
            var reparsedOccurrences = CheckEventOccurrences(reparsed.Events[i], i);
            FuzzAssert.Equal(occurrences, reparsedOccurrences, $"The recurrence of the event {i.ToString(CultureInfo.InvariantCulture)} has other occurrences once written and parsed back");
            if (@event.TimeZone is { } timeZone)
            {
                CheckTimeZone(timeZone, @event.Start);
                CheckSameOffsets(timeZone, reparsed.Events[i].TimeZone, @event.Start);
            }
        }
    }

    /// <summary>Evaluates the recurrence of an event, as an application would, and returns a description of its occurrences.</summary>
    private static string CheckEventOccurrences(Event @event, int index)
    {
        if (@event.RecurrenceRule is not { } rule)
            return "";

        var what = $"The recurrence {FuzzAssert.Format(rule.Text)} of the event {index.ToString(CultureInfo.InvariantCulture)} from {Format(@event.Start)}";
        var timeZone = @event.IsAllDay ? null : @event.TimeZone;
        if (timeZone is null)
        {
            var occurrences = rule.GetNextOccurrences(@event.Start).Take(MaxEventOccurrences).ToList();
            CheckRecurrenceRuleOccurrences(rule, @event.Start, occurrences, what);
            return Format(occurrences.Select(occurrence => DateTime.SpecifyKind(occurrence, @event.Start.Kind)));
        }

        var zoned = rule.GetNextOccurrences(@event.Start, timeZone).Take(MaxEventOccurrences).ToList();
        CheckZonedOccurrences(zoned, timeZone, what + " in " + timeZone.Id);
        FuzzAssert.True(rule.Occurrences is null || zoned.Count <= rule.Occurrences, $"{what}: more occurrences than COUNT");
        return Format(zoned);
    }

    /// <summary>Queries a time zone built from a VTIMEZONE component, around the start of the event and at instants spanning the calendar.</summary>
    private static void CheckTimeZone(TimeZoneInfo timeZone, DateTime start)
    {
        _ = timeZone.GetAdjustmentRules();
        _ = timeZone.ToSerializedString();
        var wallClock = DateTime.SpecifyKind(start, DateTimeKind.Unspecified);
        _ = timeZone.IsInvalidTime(wallClock);
        _ = timeZone.IsAmbiguousTime(wallClock);
        _ = timeZone.GetUtcOffset(wallClock);
        foreach (var probe in TimeZoneProbes)
        {
            var offset = timeZone.GetUtcOffset(probe);
            FuzzAssert.True(offset >= TimeSpan.FromHours(-14) && offset <= TimeSpan.FromHours(14), $"The offset {offset} of {timeZone.Id} is out of range");
            if (probe.UtcTicks + offset.Ticks is >= 0 and <= 3_155_378_975_999_999_999)
            {
                var converted = TimeZoneInfo.ConvertTime(probe, timeZone);
                FuzzAssert.Equal(probe.UtcTicks, converted.UtcTicks, $"Converting {Format(probe)} to {timeZone.Id} changes the instant");
                _ = timeZone.IsDaylightSavingTime(converted);
            }
        }
    }

    /// <summary>Checks that the time zone written and parsed back has the same UTC offsets from the year of the event, from which the VTIMEZONE is written.</summary>
    private static void CheckSameOffsets(TimeZoneInfo expected, TimeZoneInfo? actual, DateTime start)
    {
        FuzzAssert.True(actual is not null, $"The time zone {expected.Id} is lost once written and parsed back");
        var from = new DateTime(Math.Clamp(start.Year, 2, 9998), 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var probe in TimeZoneProbes)
        {
            if (probe.UtcDateTime < from)
                continue;

            for (var instant = probe.UtcDateTime; instant < probe.UtcDateTime.AddDays(1); instant = instant.AddHours(1))
            {
                FuzzAssert.Equal(expected.GetUtcOffset(instant), actual.GetUtcOffset(instant), $"The time zone {expected.Id} has another offset at {Format(instant)} once written and parsed back");
            }
        }
    }

#if !NET11_0_OR_GREATER
    private static string RemoveTimeZones(string ics) => TimeZoneComponentRegex().Replace(ics, "");

    [GeneratedRegex("""BEGIN:VTIMEZONE\r\n.*?END:VTIMEZONE\r\n""", RegexOptions.Singleline, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex TimeZoneComponentRegex();
#endif

    /// <summary>Checks the written content lines: CRLF line breaks, and no physical line longer than 75 octets.</summary>
    private static void CheckWrittenLines(string ics)
    {
        FuzzAssert.True(ics.EndsWith("\r\n", StringComparison.Ordinal), "The written calendar does not end with CRLF");
        foreach (var line in ics[..^2].Split("\r\n"))
        {
            FuzzAssert.False(line.Contains('\r', StringComparison.Ordinal) || line.Contains('\n', StringComparison.Ordinal), $"A written line contains a line break that is not a CRLF: {FuzzAssert.Format(line)}");
            FuzzAssert.True(Encoding.UTF8.GetByteCount(line) <= 75, $"A written line is longer than 75 octets: {FuzzAssert.Format(line)}");
            FuzzAssert.True(line.Length > 0, "The written calendar contains an empty line");
        }
    }

    private static string DescribeCalendar(InternetCalendar calendar)
    {
        var sb = new StringBuilder();
        sb.Append("VERSION=").Append(calendar.Version).Append('\n');
        AppendProperties(sb, calendar.AdditionalProperties, calendar.RawProperties);
        foreach (var @event in calendar.Events)
        {
            sb.Append("EVENT\n");
            // A property is only described with its parameters when it is written: the model denotes a missing date with
            // default(DateTime), whatever its kind, so 0001-01-01T00:00:00Z is not written, and TEXT cannot hold a control
            // character but a tab and a line feed, so the writer drops the other ones (Utilities.EscapeText).
            AppendProperty(sb, "UID", WrittenText(@event.Id), @event.IdParameters);
            AppendProperty(sb, "SUMMARY", WrittenText(@event.Summary), @event.SummaryParameters);
            AppendProperty(sb, "DESCRIPTION", @event.Description is null ? null : WrittenText(@event.Description), @event.DescriptionParameters, isWrittenWhenEmpty: true);
            AppendProperty(sb, "STATUS", @event.Status?.ToString(), @event.StatusParameters);

            // An address is written in its absolute form, in which Uri normalizes the case of the host
            AppendProperty(sb, "ORGANIZER", @event.Organizer?.Address?.Uri.AbsoluteUri, @event.Organizer?.Parameters);
            foreach (var attendee in @event.Attendees)
            {
                AppendProperty(sb, "ATTENDEE", attendee.Address?.Uri.AbsoluteUri, attendee.Parameters);
            }

            AppendProperty(sb, "CREATED", FormatModelDate(@event.Created), @event.CreatedParameters);
            AppendProperty(sb, "LAST-MODIFIED", FormatModelDate(@event.LastModified), @event.LastModifiedParameters);
            AppendProperty(sb, "DTSTAMP", FormatModelDate(@event.DateTimeStamp), @event.DateTimeStampParameters);
            AppendProperty(sb, "DTSTART", FormatModelDate(@event.Start) is { } start ? start + (@event.IsAllDay ? " (date)" : "") : null, @event.StartParameters);
            AppendProperty(sb, "DTEND", FormatModelDate(@event.End), @event.EndParameters);

            // The time zone is written with the start and the end of an event that is not all day
            var hasTimeZone = !@event.IsAllDay && (@event.Start != default || @event.End != default);
            AppendProperty(sb, "TZID", hasTimeZone ? @event.TimeZone?.Id : null, parameters: null);
            AppendProperty(sb, "RRULE", @event.RecurrenceRule is null ? null : "(set)", @event.RecurrenceRuleParameters);
            AppendProperties(sb, @event.AdditionalProperties, @event.RawProperties);
        }

        return sb.ToString();

        static void AppendProperties(StringBuilder sb, IDictionary<string, string> additionalProperties, IList<InternetCalendarProperty> rawProperties)
        {
            foreach (var property in additionalProperties.OrderBy(property => property.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append("ADDITIONAL ").Append(property.Key).Append('=').Append(property.Value).Append('\n');
            }

            foreach (var property in rawProperties)
            {
                sb.Append("RAW ").Append(property.Name).Append(Describe(property.Parameters)).Append('=').Append(property.Value).Append('\n');
            }
        }

        static string Describe(IEnumerable<KeyValuePair<string, string>>? parameters) => parameters is null ? "" : string.Concat(parameters.Select(parameter => $";{parameter.Key}={parameter.Value}"));

        static void AppendProperty(StringBuilder sb, string name, string? value, IEnumerable<KeyValuePair<string, string>>? parameters, bool isWrittenWhenEmpty = false)
        {
            sb.Append(name).Append('=');
            if (value is not null && (value.Length > 0 || isWrittenWhenEmpty))
            {
                sb.Append(value).Append(Describe(parameters));
            }

            sb.Append('\n');
        }

        static string? FormatModelDate(DateTime value) => value.Ticks is 0 ? null : Format(value);

        static string WrittenText(string? value) => value is null ? "" : string.Concat(value.Where(c => c is '\t' or '\n' || (c >= 0x20 && c != 0x7F)));
    }

    /// <summary>
    /// A content line is split into its name, its parameters (RFC 6868 decoded) and its value. Written back as a property, it
    /// is folded into lines of at most 75 octets which, unfolded, parse back to the same name, parameters and value.
    /// </summary>
    private static void CalendarContentLine(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        if (!ContentLine.TryParse(text, out var line, out var error))
        {
            FuzzAssert.True(error is not null, "TryParse returned false without an error");
            return;
        }

        FuzzAssert.True(error is null, "TryParse returned true with an error");
        FuzzAssert.True(line.Name.Length > 0 && text.StartsWith(line.Name, StringComparison.Ordinal) && InternetCalendarProperty.IsValidName(line.Name), $"The name {FuzzAssert.Format(line.Name)} is not the start of the line");
        FuzzAssert.True(text.EndsWith(":" + line.Value, StringComparison.Ordinal), $"The value {FuzzAssert.Format(line.Value)} is not the end of the line");
        var parameters = line.GetParameters();
        FuzzAssert.Equal(parameters.Count > 0, line.HasParameters, "HasParameters does not reflect the parameters");
        foreach (var parameter in parameters)
        {
            FuzzAssert.True(InternetCalendarProperty.IsValidName(parameter.Key), $"The parameter name {FuzzAssert.Format(parameter.Key)} is not valid");
            FuzzAssert.True(InternetCalendarProperty.IsValidParameterValue(parameter.Value), $"The value of the parameter {parameter.Key} cannot be written: {FuzzAssert.Format(parameter.Value)}");
            _ = line.GetParameter(parameter.Key);
        }

        _ = line.GetTextValue();

        // A content line has no line break once unfolded, so only such a line is written back
        if (text.Contains('\r', StringComparison.Ordinal) || text.Contains('\n', StringComparison.Ordinal))
            return;

        var property = InternetCalendarProperty.FromContentLine(line);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        property.Write(writer);
        var written = writer.ToString();
        CheckWrittenLines(written);

        var unfolded = written[..^2].Replace("\r\n ", "", StringComparison.Ordinal);
        if (!ContentLine.TryParse(unfolded, out var reparsed, out var reparseError))
            FuzzAssert.Fail($"The written line {FuzzAssert.Format(unfolded)} does not parse back: {reparseError}");

        FuzzAssert.Equal(line.Name, reparsed.Name, "The name changes once written and parsed back");
        FuzzAssert.Equal(line.Value, reparsed.Value, "The value changes once written and parsed back");
        FuzzAssert.Equal(Describe(parameters), Describe(reparsed.GetParameters()), $"The parameters change once written and parsed back as {FuzzAssert.Format(unfolded)}");

        static string Describe(List<KeyValuePair<string, string>> parameters) => string.Join(Environment.NewLine, parameters.Select(parameter => FuzzAssert.Format(parameter.Key) + "=" + FuzzAssert.Format(parameter.Value)));
    }

    /// <summary>
    /// The first bytes select a start date, a time zone and whether the event is all day, the rest is the DURATION value.
    /// The parser accepts exactly the positive dur-value of RFC 5545 section 3.3.6 (ASCII letters in either case, units of the
    /// time in order) within the range of <see cref="DateTime"/>, and the end of an event is its start plus the duration.
    /// </summary>
    private static void CalendarDuration(ReadOnlySpan<byte> data)
    {
        var start = ReadStart(ref data, allowLocal: false);
        var options = FuzzInput.ReadByte(ref data);
        var timeZone = (options & 0x80) is 0 ? null : FuzzInput.Pick(TimeZones, options);
        var isAllDay = (options & 0x40) is not 0;
        var text = FuzzText.Decode(data);

        var parsed = InternetCalendarDuration.TryParse(text, out var duration, out var error);
        var expected = TryParseReferenceDuration(text, out var days, out var seconds);
        FuzzAssert.Equal(expected, parsed, $"The parser and the RFC 5545 grammar disagree on {FuzzAssert.Format(text)}: {error}");
        if (!parsed)
        {
            FuzzAssert.True(error is not null, "TryParse returned false without an error");
            return;
        }

        FuzzAssert.True(duration is not null && error is null, "TryParse returned true with an error");
        FuzzAssert.Equal(text, duration.Text, "Text is not the value as written");
        FuzzAssert.Equal(days, (long)duration.Days, $"The days of {FuzzAssert.Format(text)} differ");
        FuzzAssert.Equal(TimeSpan.FromSeconds(seconds), duration.Time, $"The time of {FuzzAssert.Format(text)} differs");

        var what = $"The end of an event starting at {Format(start)} with the duration {FuzzAssert.Format(text)}";
        var hasEnd = duration.TryGetEnd(start, isAllDay, timeZone, out var end);
        var exactTicks = start.Ticks + (days * TimeSpan.TicksPerDay) + (isAllDay ? 0 : seconds * TimeSpan.TicksPerSecond);
        if (isAllDay || seconds is 0 || timeZone is null || start.Kind is not DateTimeKind.Unspecified)
        {
            FuzzAssert.Equal(exactTicks <= DateTime.MaxValue.Ticks, hasEnd, $"{what} is reported in range incorrectly");
            if (hasEnd)
            {
                FuzzAssert.Equal(exactTicks, end.Ticks, $"{what} is not the start plus the duration");
                FuzzAssert.Equal(start.Kind, end.Kind, $"{what} does not have the kind of the start");
            }
        }
        else if (hasEnd)
        {
            // The time is exact, so it is added to the instant; daylight saving time moves the wall clock by less than a day
            FuzzAssert.Equal(DateTimeKind.Unspecified, end.Kind, $"{what} is not a wall-clock time");
            FuzzAssert.True(Math.Abs(end.Ticks - exactTicks) <= TimeSpan.TicksPerDay, $"{what}, {Format(end)}, is too far from the start plus the duration");
        }
    }

    /// <summary>Parses <c>["+"] "P" (dur-date / dur-time / dur-week)</c>, with the units of a dur-time in order, possibly skipping one.</summary>
    private static bool TryParseReferenceDuration(string text, out long days, out long seconds)
    {
        days = 0;
        seconds = 0;
        var match = DurationRegex().Match(text);
        if (!match.Success)
            return false;

        if (match.Groups["weeks"].Success)
        {
            if (!TryParseDurationNumber(match.Groups["weeks"].Value, MaxDurationDays / 7, out var weeks))
                return false;

            days = weeks * 7;
            return true;
        }

        if (match.Groups["days"].Success && !TryParseDurationNumber(match.Groups["days"].Value, MaxDurationDays, out days))
            return false;

        var maxSeconds = MaxDurationDays * 86400;
        foreach (var (name, factor) in (ReadOnlySpan<(string, long)>)[("hours", 3600L), ("minutes", 60L), ("seconds", 1L)])
        {
            var group = match.Groups[name];
            if (!group.Success)
                continue;

            if (!TryParseDurationNumber(group.Value, maxSeconds / factor, out var value))
                return false;

            seconds += value * factor;
            if (seconds > maxSeconds)
                return false;
        }

        return match.Groups["days"].Success || match.Groups["time"].Success;

        static bool TryParseDurationNumber(string value, long max, out long result)
        {
            var trimmed = value.TrimStart('0');
            if (trimmed.Length > 19 || !long.TryParse(trimmed.Length is 0 ? "0" : trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out result))
            {
                result = 0;
                return false;
            }

            return result <= max;
        }
    }

    // The letters are ASCII, in either case; a time has at least one unit
    [GeneratedRegex("""^\+?[Pp](?:(?<weeks>[0-9]+)[Ww]|(?:(?<days>[0-9]+)[Dd])?(?<time>[Tt](?=[0-9])(?:(?<hours>[0-9]+)[Hh])?(?:(?<minutes>[0-9]+)[Mm])?(?:(?<seconds>[0-9]+)[Ss])?)?)\z""", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex DurationRegex();

    private static IEnumerable<FuzzSeed> GetCalendarSeeds()
    {
        var index = 0;
        foreach (var calendar in GetCalendarSeedTexts())
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"ics-{index++}"), calendar);
        }
    }

    /// <summary>Rebuilds the calendars of the unit tests: their content lines, wrapped in a VEVENT, after the VTIMEZONE they reference.</summary>
    private static IEnumerable<string> GetCalendarSeedTexts()
    {
        var source = FuzzResources.ReadText(typeof(SchedulingFuzzTargets).Assembly, "InternetCalendarTests.cs");
        var members = MemberSeparatorRegex().Split(source);
        var timeZones = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            if (TimeZoneFieldRegex().Match(member) is { Success: true } field)
            {
                timeZones[field.Groups["name"].Value] = [.. GetStringLiteralsFromSource(member).Where(IsContentLine)];
            }
        }

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            if (TimeZoneFieldRegex().IsMatch(member))
                continue;

            var literals = GetStringLiteralsFromSource(member);
            foreach (var literal in literals)
            {
                if (literal.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(literal);
                }
            }

            var lines = literals.Where(IsContentLine).Where(line => !line.Contains("VCALENDAR", StringComparison.OrdinalIgnoreCase)).ToList();
            if (lines.Count is 0)
                continue;

            var sb = new StringBuilder("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Test//Test//EN\r\n");
            foreach (Match reference in TimeZoneReferenceRegex().Matches(member))
            {
                if (timeZones.TryGetValue(reference.Groups["name"].Value, out var timeZoneLines))
                {
                    sb.Append("BEGIN:VTIMEZONE\r\n");
                    foreach (var line in timeZoneLines)
                    {
                        sb.Append(line).Append("\r\n");
                    }

                    sb.Append("END:VTIMEZONE\r\n");
                }
            }

            // The lines of the time zone components a test builds itself come first, then those of the event
            var componentEnd = lines.FindLastIndex(line => line.StartsWith("END:STANDARD", StringComparison.OrdinalIgnoreCase) || line.StartsWith("END:DAYLIGHT", StringComparison.OrdinalIgnoreCase));
            if (componentEnd >= 0)
            {
                sb.Append("BEGIN:VTIMEZONE\r\n");
                foreach (var line in lines.Take(componentEnd + 1))
                {
                    sb.Append(line).Append("\r\n");
                }

                sb.Append("END:VTIMEZONE\r\n");
            }

            var hasEvent = lines.Exists(line => line.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase));
            if (!hasEvent)
            {
                sb.Append("BEGIN:VEVENT\r\n");
            }

            foreach (var line in lines.Skip(componentEnd + 1))
            {
                sb.Append(line).Append("\r\n");
            }

            if (!hasEvent)
            {
                sb.Append("END:VEVENT\r\n");
            }

            sb.Append("END:VCALENDAR\r\n");
            result.Add(sb.ToString());
        }

        return result.Order(StringComparer.Ordinal);

        static bool IsContentLine(string literal) => ContentLineRegex().IsMatch(literal);
    }

    private static IEnumerable<FuzzSeed> GetContentLineSeeds()
    {
        var lines = new HashSet<string>(StringComparer.Ordinal);
        foreach (var calendar in GetCalendarSeedTexts())
        {
            foreach (var line in calendar.Split("\r\n"))
            {
                if (line.Length > 0)
                {
                    lines.Add(line);
                }
            }
        }

        var index = 0;
        foreach (var line in lines.Order(StringComparer.Ordinal))
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"line-{index++}"), line);
        }

        yield return FuzzSeed.FromText("line-parameters", "ATTENDEE;CN=\"Doe, Jane\";ROLE=REQ-PARTICIPANT;X-A=a^^b^nc^'d;MEMBER=\"mailto:a@example.com\",\"mailto:b@example.com\":mailto:jane@example.com");
        yield return FuzzSeed.FromText("line-long", "DESCRIPTION:" + string.Concat(Enumerable.Repeat("é€😀a", 40)));
    }

    private static IEnumerable<FuzzSeed> GetDurationSeeds()
    {
        var durations = new HashSet<string>(StringComparer.Ordinal) { "PT1H", "P1D", "P2W", "+P1DT2H3M4S", "PT90M", "PT1H30S", "P3652059D", "P521722W", "PT315537897600S", "P0D", "pt1h", "P1DT", "-PT1H", "P", "PT" };
        foreach (var literal in GetStringLiterals("InternetCalendarTests.cs"))
        {
            if (literal.StartsWith("DURATION:", StringComparison.Ordinal))
            {
                durations.Add(literal["DURATION:".Length..]);
            }
            else if (literal.Length is > 1 and < 40 && literal[0] is 'P' or '+' or '-' && literal.Contains('P', StringComparison.Ordinal))
            {
                durations.Add(literal);
            }
        }

        // The second notable start, 2024-01-01, then a time zone and the all-day flag
        var index = 0;
        foreach (var duration in durations.Order(StringComparer.Ordinal))
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"duration-{index}"), [0x01, (byte)(index % 3 is 0 ? 0x00 : index % 3 is 1 ? 0x81 : 0x40)], duration);
            index++;
        }

        yield return FuzzSeed.FromText("duration-dst", [0x03, 0x81], "PT1H");
        yield return FuzzSeed.FromText("duration-max", [0x0D, 0x00], "PT1S");
    }

    [GeneratedRegex("""^[A-Za-z0-9-]+[;:]""", RegexOptions.None, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex ContentLineRegex();

    // Each member of the test class starts with an attribute or a modifier at the indentation of the members
    [GeneratedRegex("""\n(?=    (?:\[|private |public |internal ))""", RegexOptions.None, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex MemberSeparatorRegex();

    [GeneratedRegex("""static readonly string\[\] (?<name>\w+)\s*=""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex TimeZoneFieldRegex();

    [GeneratedRegex("""CreateIcsWithTimeZone\((?<name>\w+)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex TimeZoneReferenceRegex();
}
