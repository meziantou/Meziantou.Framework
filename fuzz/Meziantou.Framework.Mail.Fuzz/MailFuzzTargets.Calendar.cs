using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Mail.Fuzz;

/// <summary>
/// The vCard and iCalendar readers read any text. A card written in any version reads back with the same values; a calendar
/// written and read back writes the same text. The occurrences of a recurrence rule are in order, after the start, and as
/// many as <c>COUNT</c>; a rule written and read back writes the same text.
/// </summary>
public static partial class MailFuzzTargets
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
        catch (InvalidMailDataException)
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
                    _ = ev.ToPstAppointmentBuilder();
                }
                catch (NotSupportedException)
                {
                }
            }

            foreach (var todo in calendar.Todos)
            {
                _ = todo.ToPstTaskBuilder();
            }
        }
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
        var assembly = typeof(MailFuzzTargets).Assembly;
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
    }
}
