using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Email.Fuzz;

/// <summary>
/// The vCard and iCalendar readers read any text. A card written in any version reads back with the same values; a calendar
/// written and read back writes the same text. The occurrences of a recurrence rule are in order, after the start, and as
/// many as <c>COUNT</c>; a rule written and read back writes the same text.
/// </summary>
public static partial class EmailFuzzTargets
{
    private const string CalendarSeedPrefix = "TestData/Calendar/";

    private static readonly string[] CalendarTokens =
    [
        "BEGIN:VCARD\r\n", "END:VCARD\r\n", "VERSION:2.1\r\n", "VERSION:3.0\r\n", "VERSION:4.0\r\n", "FN:", "N:", "EMAIL;TYPE=work:", "TEL;CELL:", "ADR;HOME:", "ORG:", "BDAY:", "PHOTO;ENCODING=b;TYPE=PNG:", "data:image/png;base64,",
        ";CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:", "=C3=A9", "=\r\n", "item1.",
        "BEGIN:VCALENDAR\r\n", "END:VCALENDAR\r\n", "BEGIN:VEVENT\r\n", "END:VEVENT\r\n", "BEGIN:VTIMEZONE\r\n", "BEGIN:STANDARD\r\n", "BEGIN:DAYLIGHT\r\n", "TZOFFSETTO:+0100\r\n",
        "DTSTART;TZID=Europe/Paris:", "DTSTART;VALUE=DATE:", "20260115T090000Z", "RRULE:FREQ=", "WEEKLY", "MONTHLY", "YEARLY", ";BYDAY=", "-1FR", "2MO", ";BYMONTHDAY=", ";BYSETPOS=", ";COUNT=", ";UNTIL=", ";INTERVAL=", "EXDATE:", "RDATE:", "DURATION:PT1H",
        "\\n", "\\,", "\\;", "\r\n ", ":", ";", ",", "\"",
    ];

    private static IReadOnlyList<FuzzTarget> CalendarTargets { get; } =
    [
        new("vcard-read", VCardRead) { Seeds = () => GetCalendarSeeds(".vcf"), Dictionary = CalendarTokens },
        new("ical-read", ICalendarRead) { Seeds = () => GetCalendarSeeds(".ics"), Dictionary = CalendarTokens },
        new("ical-recurrence", RecurrenceRead) { Seeds = GetRecurrenceRuleSeeds, Dictionary = CalendarTokens },
        new("meeting-uid", MeetingUid) { Seeds = GetMeetingUidSeeds },
    ];

    private static void VCardRead(ReadOnlySpan<byte> data)
    {
        foreach (var card in VCard.Load(data))
        {
            var hasFormattedName = card.FormattedName is not null;
            var expected = DescribeCard(card, hasFormattedName);
            foreach (var version in Enum.GetValues<VCardVersion>())
            {
                var copies = VCard.Parse(card.ToString(version));
                FuzzAssert.Equal(1, copies.Count, "A card written in " + version + " reads as " + copies.Count + " cards");
                FuzzAssert.Equal(expected, DescribeCard(copies[0], hasFormattedName), "A card written in " + version + " reads back differently");
            }
        }
    }

    private static void ICalendarRead(ReadOnlySpan<byte> data)
    {
        IReadOnlyList<VCalendar> calendars;
        try
        {
            calendars = VCalendar.Load(data);
        }
        catch (InvalidEmailDataException)
        {
            return;
        }

        foreach (var calendar in calendars)
        {
            var written = calendar.ToString();
            var copy = VCalendar.Parse(written);
            FuzzAssert.Equal(1, copy.Count, "A calendar written reads as " + copy.Count + " calendars");
            FuzzAssert.Equal(written, copy[0].ToString(), "A calendar written and read back writes a different text");
            foreach (var ev in calendar.Events)
            {
                _ = ev.Start?.ToUtc(calendar);
                CheckOccurrences(ev.GetOccurrences, ev.Start?.Value);
                try
                {
                    _ = ev.ToMapiAppointmentBuilder();
                }
                catch (NotSupportedException)
                {
                }
            }

            foreach (var todo in calendar.Todos)
            {
                _ = todo.ToMapiTaskBuilder();
            }

            CheckMeetingMessage(calendar);
        }
    }

    /// <summary>An invitation converts to a meeting message, which keeps its type and its UID in an .msg file, and converts back to an invitation with the same method and UID.</summary>
    private static void CheckMeetingMessage(VCalendar calendar)
    {
        MapiMeetingMessageBuilder? builder;
        try
        {
            builder = Internals.CalendarConverter.ToMeetingMessage(calendar);
        }
        catch (NotSupportedException)
        {
            return;
        }

        if (builder is null)
        {
            return;
        }

        var message = MsgFile.Load(MsgFile.ToByteArray(builder));
        var meeting = message as MapiMeetingMessage;
        FuzzAssert.True(meeting is not null, "The meeting message reads as a " + message.GetType().Name);
        FuzzAssert.Equal(builder.MeetingMessageType, meeting.MeetingMessageType, "The meeting message has a different type");
        FuzzAssert.Equal(builder.Uid, meeting.Uid, "The meeting message has a different UID");

        var back = new VCalendar();
        var ev = back.AddMeetingMessage(meeting);
        FuzzAssert.Equal(calendar.Method?.Trim().ToUpperInvariant(), back.Method, "The invitation has a different method");
        if (meeting.Uid is not null)
        {
            FuzzAssert.Equal(meeting.Uid, ev.Uid, "The invitation has a different UID");
        }

        var copy = Internals.CalendarConverter.ToMeetingMessage(VCalendar.Parse(back.ToString())[0]);
        FuzzAssert.Equal(builder.MeetingMessageType, copy?.MeetingMessageType, "The invitation converts back to a different meeting message");
    }

    /// <summary>A UID stored in a global object identifier reads back unchanged, except an encoded identifier, which reads back in uppercase without its instance date, and stays the same after that.</summary>
    private static void MeetingUid(ReadOnlySpan<byte> data)
    {
        _ = Internals.GlobalObjectId.ToUid(data);
        var text = FuzzText.Decode(data);
        var uid = Internals.GlobalObjectId.ToUid(Internals.GlobalObjectId.FromUid(text));
        FuzzAssert.True(uid is not null, "The identifier of a UID is not an identifier");
        FuzzAssert.Equal(uid, Internals.GlobalObjectId.ToUid(Internals.GlobalObjectId.FromUid(uid)), "The UID of an identifier changes when it is stored again");
        var utf8 = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text));
        if (!text.StartsWith("040000008200E00074C5B7101A82E008", StringComparison.OrdinalIgnoreCase) && utf8 == text)
        {
            FuzzAssert.Equal(text, uid, "A UID that is not an encoded identifier changes");
        }
    }

    private static IEnumerable<FuzzSeed> GetMeetingUidSeeds()
    {
        yield return FuzzSeed.FromText("third-party", "meeting-42@example.com");
        yield return FuzzSeed.FromText("encoded", "040000008200E00074C5B7101A82E00807EA0302C0E2F0E9B9D3D801000000000000000010000000C4A5E4E6E6AE4F4BA0E9D1A4B2A3C1F0");
        yield return FuzzSeed.FromText("short", "040000008200E00074C5B7101A82E008");
        yield return FuzzSeed.FromText("empty", "");
    }

    private static void RecurrenceRead(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        var newLine = text.IndexOf('\n', StringComparison.Ordinal);
        var start = new DateTime(2026, 1, 15, 9, 0, 0);
        if (newLine >= 0 && CalendarDateTime.Parse(text[..newLine]) is { } parsed)
        {
            start = parsed.Value;
        }

        var rule = RecurrenceRule.Parse(newLine >= 0 ? text[(newLine + 1)..] : text);
        if (rule is null)
        {
            return;
        }

        var written = rule.ToString();
        FuzzAssert.Equal(written, RecurrenceRule.Parse(written)?.ToString(), "A rule written and read back writes a different text");
        CheckOccurrences(() => rule.GetOccurrences(start), start, rule.Count);
    }

    private static void CheckOccurrences(Func<IEnumerable<DateTime>> getOccurrences, DateTime? start, int? count = null)
    {
        List<DateTime> occurrences;
        try
        {
            occurrences = [.. getOccurrences().Take(200)];
        }
        catch (NotSupportedException)
        {
            return;
        }

        for (var i = 1; i < occurrences.Count; i++)
        {
            FuzzAssert.True(occurrences[i] > occurrences[i - 1], "The occurrences are not in order");
        }

        if (start is { } first && occurrences.Count > 0)
        {
            FuzzAssert.True(occurrences.All(occurrence => occurrence >= first) || occurrences[0] != first, "An occurrence is before the start");
        }

        if (count is { } max)
        {
            FuzzAssert.True(occurrences.Count <= max, "There are more occurrences than COUNT");
        }
    }

    // The writer adds the formatted name to a card that has none, from the other names: it is compared when the card has one
    private static string DescribeCard(VCard card, bool includeFormattedName) => string.Join("\u0001",
        includeFormattedName ? card.FormattedName : null,
        card.FamilyName,
        card.GivenName,
        card.AdditionalNames,
        card.NamePrefix,
        card.NameSuffix,
        string.Join(",", card.Nicknames),
        string.Join(",", card.Emails.Select(email => email.Value + ":" + Types(email.Types))),
        string.Join(",", card.Phones.Select(phone => phone.Value + ":" + Types(phone.Types))),
        string.Join(",", card.Addresses.Select(address => address + ":" + Types(address.Types))),
        string.Join(",", card.Urls.Select(url => url.Value)),
        card.Organization,
        string.Join(",", card.OrganizationUnits),
        card.Title,
        card.Role,
        card.Birthday?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        card.Note,
        card.Uid,
        string.Join(",", card.Categories),
        card.Photo is { } photo ? Convert.ToHexString(photo.Span) : null).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'); // The writers write the line breaks of the texts as \n: a CR reads back as LF

    // The types are a set: their order can change, and vCard 2.1 adds the type internet to the email addresses
    private static string Types(IEnumerable<string> types) => string.Join("/", types.Where(type => type != "internet").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    private static IEnumerable<FuzzSeed> GetCalendarSeeds(string extension)
    {
        var assembly = typeof(EmailFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(CalendarSeedPrefix, StringComparison.Ordinal) && name.EndsWith(extension, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return new FuzzSeed(name[CalendarSeedPrefix.Length..], memory.ToArray());
        }
    }

    private static IEnumerable<FuzzSeed> GetRecurrenceRuleSeeds()
    {
        string[] rules =
        [
            "20260115T090000\nFREQ=WEEKLY;BYDAY=MO,WE;COUNT=10",
            "20260131T090000\nFREQ=MONTHLY;BYMONTHDAY=-1;COUNT=12",
            "20260115T090000\nFREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1;COUNT=12",
            "19700329T020000\nFREQ=YEARLY;BYMONTH=3;BYDAY=-1SU",
            "20280229T080000\nFREQ=YEARLY;COUNT=5",
            "20260115\nFREQ=DAILY;INTERVAL=3;UNTIL=20260401",
        ];

        for (var i = 0; i < rules.Length; i++)
        {
            yield return new FuzzSeed("rule-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), FuzzText.Encode(rules[i]));
        }

        // The rules of the tests of python-dateutil: name, start, rule and occurrences, separated by tabulations
        using var stream = FuzzResources.Open(typeof(EmailFuzzTargets).Assembly, "TestData/Rules/dateutil-rrule.tsv");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split('\t');
            yield return new FuzzSeed("dateutil-" + fields[0], FuzzText.Encode(fields[1] + "\n" + fields[2]));
        }
    }
}
