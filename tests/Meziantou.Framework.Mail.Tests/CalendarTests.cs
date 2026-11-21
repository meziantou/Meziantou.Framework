using System.Globalization;

namespace Meziantou.Framework.Mail.Tests;

/// <summary>Tests of vCard and iCalendar: the files of TestData/Calendar, compared with FolkerKinzel.VCards and Ical.Net, the recurrence rules, and the conversions to and from the items of Outlook.</summary>
public sealed class CalendarTests
{
    public static TheoryData<string> VCardFiles => ["contacts-21.vcf", "contacts-30.vcf", "contacts-40.vcf"];

    public static TheoryData<string> Rules =>
    [
        "FREQ=DAILY;COUNT=10",
        "FREQ=DAILY;INTERVAL=3;COUNT=12",
        "FREQ=DAILY;BYMONTH=1,2;COUNT=40",
        "FREQ=WEEKLY;COUNT=10",
        "FREQ=WEEKLY;INTERVAL=2;BYDAY=TU,TH;COUNT=12",
        "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR;COUNT=20",
        "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,SU;WKST=SU;COUNT=10",
        "FREQ=MONTHLY;COUNT=12",
        "FREQ=MONTHLY;BYMONTHDAY=31;COUNT=10",
        "FREQ=MONTHLY;BYMONTHDAY=-1;COUNT=12",
        "FREQ=MONTHLY;BYMONTHDAY=1,15;COUNT=12",
        "FREQ=MONTHLY;BYDAY=2TU;COUNT=12",
        "FREQ=MONTHLY;BYDAY=-1FR;COUNT=12",
        "FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1;COUNT=12",
        "FREQ=MONTHLY;BYDAY=FR;BYMONTHDAY=13;COUNT=5",
        "FREQ=MONTHLY;INTERVAL=3;BYDAY=1MO;COUNT=8",
        "FREQ=YEARLY;COUNT=5",
        "FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU;COUNT=5",
        "FREQ=YEARLY;BYMONTH=1,7;BYMONTHDAY=4;COUNT=8",
        "FREQ=YEARLY;BYDAY=20MO;COUNT=4",
        "FREQ=YEARLY;BYMONTH=11;BYDAY=TH;BYSETPOS=4;COUNT=5",
        "FREQ=WEEKLY;BYDAY=WE;UNTIL=20260401T090000",
        "FREQ=MONTHLY;BYMONTHDAY=29;UNTIL=20280301T090000",
    ];

    internal static string GetPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "Calendar", fileName);

    [Theory]
    [MemberData(nameof(VCardFiles))]
    public void VCard_MatchesFolkerKinzel(string fileName)
    {
        var text = File.ReadAllText(GetPath(fileName));
        var expected = FolkerKinzel.VCards.Vcf.Parse(text).Select(DescribeWithFolkerKinzel).ToList();
        Assert.Equal(expected, VCard.Parse(text).Select(Describe).ToList());
    }

    [Theory]
    [MemberData(nameof(VCardFiles))]
    public void VCard_RoundTrip(string fileName)
    {
        var cards = VCard.Load(GetPath(fileName));
        foreach (var version in Enum.GetValues<VCardVersion>())
        {
            foreach (var card in cards)
            {
                var text = card.ToString(version);
                var copy = VCard.Parse(text).Single();
                Assert.Equal(Describe(card), Describe(copy));
                Assert.Equal(version, copy.Version);
                Assert.Equal(card.Photo?.ToArray(), copy.Photo?.ToArray());

                // FolkerKinzel.VCards reads the card the library writes as the library does
                Assert.Equal(DescribeWithFolkerKinzel(FolkerKinzel.VCards.Vcf.Parse(text).Single()), Describe(copy));
            }
        }
    }

    [Fact]
    public void VCard_Snippet()
    {
        var path = GetPath("contacts-30.vcf");

        #region VCard_Usage
        var card = VCard.Load(path).Single();
        Assert.Equal("Mary Ann O'Brien", card.FormattedName);
        Assert.Equal("Example, Ltd.", card.Organization);
        Assert.Equal("mary@example.com", card.Emails.First(email => email.HasType("pref")).Value);
        Assert.Equal("Springfield", card.Addresses.Single().Locality);

        // The card in another version of the format
        var text = card.ToString(VCardVersion.V40);
        Assert.Contains("EMAIL;PREF=1;TYPE=internet,work:mary@example.com", text, StringComparison.Ordinal);
        #endregion

        Assert.Contains(card.OtherProperties, property => property.Name == "X-CUSTOM" && property.GetParameter("X-PARAM") == "a:b");
        Assert.Contains("item1.X-ABLABEL:Home", text, StringComparison.Ordinal);
    }

    [Fact]
    public void VCard_21()
    {
        var cards = VCard.Load(GetPath("contacts-21.vcf"));
        Assert.HasCount(2, cards);
        var card = cards[0];
        Assert.Equal(VCardVersion.V21, card.Version);
        Assert.Equal("Müller", card.FamilyName);
        Assert.Equal("Dr. Jürgen Müller", card.FormattedName);
        Assert.Equal("First line\nSecond line", card.Note);
        Assert.Equal("Hauptstraße 1", card.Addresses.Single().Street);
        Assert.Equal(new DateOnly(1980, 4, 15), card.Birthday);
        Assert.Equal(["work", "voice"], card.Phones[0].Types);

        // vCard 2.1 writes the values that are not ASCII in quoted-printable
        var text = card.ToString(VCardVersion.V21);
        Assert.Contains("N;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:M=C3=BCller;J=C3=BCrgen;;Dr.;", text, StringComparison.Ordinal);
        Assert.Contains("TEL;WORK;VOICE:+49 30 1234567", text, StringComparison.Ordinal);
    }

    [Fact]
    public void VCard_PstContact()
    {
        var contact = Assert.IsType<PstContact>(MsgFile.Load(MsgFileTests.GetPath("poi-msgClassContact.msg")));
        var card = VCard.FromPst(contact);
        Assert.Equal(contact.DisplayName, card.FormattedName);
        Assert.Equal(contact.EmailAddresses, card.Emails.Select(email => email.Value));

        // The card written as an .msg file reads back with the same values
        var copy = Assert.IsType<PstContact>(MsgFile.Load(MsgFile.ToByteArray(card.ToPstContactBuilder())));
        Assert.Equal(Describe(card), Describe(VCard.FromPst(copy)));
    }

    [Fact]
    public void ICalendar_MatchesIcalNet()
    {
        var text = File.ReadAllText(GetPath("calendar.ics"));
        var oracle = Ical.Net.Calendar.Load(text)!;
        var calendar = VCalendar.Parse(text).Single();
        var events = calendar.Events.ToList();
        Assert.Equal(oracle.Events.Select(ev => $"{ev.Uid} {ev.Summary} {ev.DtStart?.Value:s} {(ev.DtStart?.IsUtc == true ? "UTC" : ev.DtStart?.TzId)} {ev.RecurrenceIdentifier?.StartTime.Value:s}"), events.Select(ev => $"{ev.Uid} {ev.Summary} {ev.Start?.Value:s} {(ev.Start?.IsUtc == true ? "UTC" : ev.Start?.TimeZoneId)} {ev.RecurrenceId?.Value:s}"));
        Assert.Equal(oracle.Todos.Select(todo => todo.Summary), calendar.Todos.Select(todo => todo.Summary));

        // The occurrences of the events, with their exceptions and their additional dates
        foreach (var ev in events.Where(ev => ev.RecurrenceId is null))
        {
            var expected = OccurrencesWithIcalNet(ev.ToString(), ev.Calendar!, 30);
            Assert.Equal(expected, ev.GetOccurrences().Take(30).ToList());
        }
    }

    [Theory]
    [MemberData(nameof(Rules))]
    public void RecurrenceRule_MatchesIcalNet(string rule)
    {
        foreach (var start in new[] { "20260115T090000", "20260131T170000", "20280229T080000", "20251231T235900" })
        {
            var text = $"BEGIN:VEVENT\r\nUID:test\r\nDTSTAMP:20260101T000000Z\r\nDTSTART:{start}\r\nRRULE:{rule}\r\nEND:VEVENT\r\n";
            var calendar = VCalendar.Parse("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:test\r\n" + text + "END:VCALENDAR\r\n").Single();
            var ev = calendar.Events.Single();
            var expected = OccurrencesWithIcalNet(text, calendar, 60);
            var actual = ev.GetOccurrences().Take(60).ToList();
            if (expected.FirstOrDefault() != ev.Start!.Value.Value)
            {
                // A start that the rule does not give: [RFC 5545] 3.8.5.3 counts it as the first occurrence, Ical.Net does not
                Assert.Equal(ev.Start.Value.Value, actual[0]);
                actual.RemoveAt(0);
                expected = [.. expected.Take(actual.Count)];
            }

            Assert.Equal(expected, actual);
            var written = RecurrenceRule.Parse(rule)!.ToString();
            Assert.Equal(written, RecurrenceRule.Parse(written)!.ToString());
            Assert.Equal(actual, RecurrenceRule.Parse(written)!.GetOccurrences(ev.Start.Value.Value).Skip(actual.Count == expected.Count && expected.FirstOrDefault() == ev.Start.Value.Value ? 0 : 1).Take(actual.Count));
        }
    }

    [Fact]
    public void RecurrenceRule_Snippet()
    {
        #region RecurrenceRule_GetOccurrences
        var rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=-1FR;COUNT=3")!;
        var occurrences = rule.GetOccurrences(new DateTime(2026, 1, 30, 17, 0, 0)).ToList();
        Assert.Equal([new DateTime(2026, 1, 30, 17, 0, 0), new DateTime(2026, 2, 27, 17, 0, 0), new DateTime(2026, 3, 27, 17, 0, 0)], occurrences);
        #endregion

        Assert.Null(RecurrenceRule.Parse("COUNT=3"));
        Assert.Null(RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=XX"));
        Assert.Throws<NotSupportedException>(() => RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9,17")!.GetOccurrences(new DateTime(2026, 1, 1)).First());
        Assert.Throws<NotSupportedException>(() => RecurrenceRule.Parse("FREQ=HOURLY")!.GetOccurrences(new DateTime(2026, 1, 1)));
        Assert.Equal("FREQ=DAILY;BYHOUR=9,17", RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9,17")!.ToString());
    }

    [Fact]
    public void ICalendar_Snippet()
    {
        var path = GetPath("calendar.ics");

        #region VCalendar_Usage
        var calendar = VCalendar.Load(path).Single();
        var meeting = calendar.Events.First();
        Assert.Equal("Weekly meeting", meeting.Summary);
        Assert.Equal("Room 1; building A", meeting.Location);

        // The start is 9:00 in Paris: the time zone of the calendar converts it to UTC
        Assert.Equal(new DateTime(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc), meeting.Start!.Value.ToUtc(calendar));
        Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0), meeting.GetOccurrences().First());

        var created = new VCalendar();
        var ev = created.Add(new VEvent
        {
            Uid = "launch@example.com",
            Summary = "Launch",
            Start = CalendarDateTime.FromUtc(new DateTime(2026, 6, 1, 14, 0, 0, DateTimeKind.Utc)),
            Duration = TimeSpan.FromHours(1),
            RecurrenceRule = RecurrenceRule.Parse("FREQ=WEEKLY;COUNT=4"),
        });
        var text = created.ToString();
        #endregion

        Assert.Contains("DTSTART:20260601T140000Z", text, StringComparison.Ordinal);
        Assert.HasCount(4, ev.GetOccurrences());
    }

    [Fact]
    public void ICalendar_RoundTrip()
    {
        var text = File.ReadAllText(GetPath("calendar.ics"));
        var calendar = VCalendar.Parse(text).Single();
        var written = calendar.ToString();

        // The writer folds the lines longer than 75 bytes, which the file does not
        Assert.Equal(text, written.Replace("\r\n ", "", StringComparison.Ordinal));
        Assert.Equal(written, VCalendar.Parse(written).Single().ToString());
    }

    [Fact]
    public void VTimeZone_Offsets()
    {
        var calendar = VCalendar.Load(GetPath("calendar.ics")).Single();
        var paris = calendar.FindTimeZone("Europe/Paris")!;
        Assert.Equal(TimeSpan.FromHours(1), paris.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0)));
        Assert.Equal(TimeSpan.FromHours(2), paris.GetUtcOffset(new DateTime(2026, 7, 15, 12, 0, 0)));
        Assert.Equal(TimeSpan.FromHours(2), paris.GetUtcOffset(new DateTime(2026, 3, 29, 3, 0, 0)));
        Assert.Equal(TimeSpan.FromHours(1), paris.GetUtcOffset(new DateTime(2026, 10, 25, 3, 0, 0)));
        Assert.Equal(TimeSpan.FromHours(1), paris.GetUtcOffset(new DateTime(1960, 1, 1)));
    }

    [Fact]
    public void ICalendar_Values()
    {
        var calendar = VCalendar.Load(GetPath("calendar.ics")).Single();
        var meeting = calendar.Events.First();
        Assert.Equal("Agenda:\n- news\n- plans, ideas", meeting.Description);
        Assert.Equal(new CalendarAttendee("alice@example.com", "Alice"), meeting.Organizer);
        Assert.Equal([new CalendarAttendee("bob@example.com", "Bob", "REQ-PARTICIPANT", "ACCEPTED"), new CalendarAttendee("carol@example.com", "Carol, C.", "OPT-PARTICIPANT")], meeting.Attendees);
        Assert.Equal(["Work", "Meetings"], meeting.Categories);
        var monthly = calendar.Events.Single(ev => ev.Uid == "monthly@example.com");
        Assert.Equal(TimeSpan.FromMinutes(90), monthly.Duration);
        var holiday = calendar.Events.Single(ev => ev.Uid == "holiday@example.com");
        Assert.True(holiday.IsAllDay);
        var todo = calendar.Todos.Single();
        Assert.Equal(40, todo.PercentComplete);
        Assert.Equal(CalendarDateTime.FromDate(new DateOnly(2026, 2, 15)), todo.Due);
    }

    [Fact]
    public void ICalendar_PstAppointment()
    {
        // A recurring appointment with deleted and modified occurrences, written as an iCalendar object, read by Ical.Net, then back as an appointment
        using var file = PstFile.Open(Path.Combine(AppContext.BaseDirectory, "TestData", "Pst", "java-libpst-dist-list.pst"));
        var appointment = FindItems<PstAppointment>(file.RootFolder).First(item => item.IsRecurring);
        var pattern = appointment.GetRecurrence()!;
        var calendar = new VCalendar();
        var master = calendar.AddAppointment(appointment);
        var text = calendar.ToString();
        var oracle = Ical.Net.Calendar.Load(text)!;
        Assert.HasCount(calendar.Events.Count(), oracle.Events);
        Assert.Equal(OccurrencesWithIcalNet(master.ToString(), calendar, 20, utc: true), master.GetOccurrences().Take(20).ToList());

        var builder = master.ToPstAppointmentBuilder();
        Assert.Equal(appointment.Subject, builder.Subject);
        Assert.Equal(appointment.Start, builder.Start);
        Assert.Equal(appointment.End, builder.End);
        var copy = builder.Recurrence!;
        Assert.Equal(pattern.Frequency, copy.Frequency);
        Assert.Equal(pattern.PatternType, copy.PatternType);
        Assert.Equal(pattern.Period, copy.Period);
        Assert.Equal(pattern.DaysOfWeek, copy.DaysOfWeek);
        Assert.Equal(pattern.EndType, copy.EndType);
        Assert.Equal(pattern.FirstDateTime, copy.FirstDateTime);
        Assert.Equal(pattern.Exceptions.Count, calendar.Events.Count(ev => ev.RecurrenceId is not null));
    }

    [Fact]
    public void ICalendar_PstTask()
    {
        var task = Assert.IsType<PstTask>(MsgFile.Load(MsgFileTests.GetPath("poi-msgClassTask.msg")));
        var calendar = new VCalendar();
        var todo = calendar.AddTask(task);
        Assert.Equal(task.Subject, todo.Summary);
        var copy = Assert.IsType<PstTask>(MsgFile.Load(MsgFile.ToByteArray(todo.ToPstTaskBuilder())));
        Assert.Equal(task.Subject, copy.Subject);
        Assert.Equal(task.Status == PstTaskStatus.Complete, copy.Status == PstTaskStatus.Complete);
        Assert.Equal(task.PercentComplete, copy.PercentComplete, 2);
        Assert.NotNull(Ical.Net.Calendar.Load(calendar.ToString()));
    }

    [Fact]
    public void ICalendar_Invalid()
    {
        Assert.Empty(VCalendar.Parse("not a calendar"));
        var deep = string.Concat(Enumerable.Repeat("BEGIN:X\r\n", 40));
        Assert.Throws<InvalidMailDataException>(() => VCalendar.Parse("BEGIN:VCALENDAR\r\n" + deep));
        var unclosed = VCalendar.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Unclosed\r\n").Single();
        Assert.Equal("Unclosed", unclosed.Events.Single().Summary);
    }

    private static List<DateTime> OccurrencesWithIcalNet(string eventText, VCalendar calendar, int count, bool utc = false)
    {
        var timeZones = string.Concat(calendar.TimeZones.Select(timeZone => timeZone.ToString()));
        var oracle = Ical.Net.Calendar.Load("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:test\r\n" + timeZones + eventText + "END:VCALENDAR\r\n")!;
        return [.. oracle.Events.Single().GetOccurrences().Take(count).Select(occurrence => utc ? occurrence.Period.StartTime.AsUtc : occurrence.Period.StartTime.Value)];
    }

    private static IEnumerable<T> FindItems<T>(PstFolder folder)
        where T : PstMessage
    {
        foreach (var item in folder.EnumerateItems().OfType<T>())
        {
            yield return item;
        }

        foreach (var child in folder.Folders)
        {
            foreach (var item in FindItems<T>(child))
            {
                yield return item;
            }
        }
    }

    private static string Describe(VCard card) => string.Join(" | ",
        card.FormattedName,
        card.FamilyName,
        card.GivenName,
        string.Join(",", card.Emails.Select(email => email.Value)),
        string.Join(",", card.Phones.Select(phone => phone.Value)),
        card.Organization,
        string.Join(",", card.OrganizationUnits),
        card.Title,
        card.Note,
        card.Birthday?.ToString("O", CultureInfo.InvariantCulture),
        string.Join(",", card.Categories),
        string.Join(",", card.Nicknames),
        string.Join(";", card.Addresses.Select(address => address.Street + "/" + address.Locality + "/" + address.PostalCode + "/" + address.Country)));

    private static string DescribeWithFolkerKinzel(FolkerKinzel.VCards.VCard card)
    {
        static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;
        static string Join(IReadOnlyList<string>? values) => values is null ? "" : string.Join(" ", values);
        var name = card.NameViews?.FirstOrDefault(view => view is not null)?.Value;
        var org = card.Organizations?.FirstOrDefault()?.Value;
        return string.Join(" | ",
            card.DisplayNames?.FirstOrDefault()?.Value,
            Empty(Join(name?.Surnames)),
            Empty(Join(name?.Given)),
            string.Join(",", card.EMails?.Where(email => email is not null).Select(email => email!.Value) ?? []),
            string.Join(",", card.Phones?.Where(phone => phone is not null).Select(phone => phone!.Value) ?? []),
            Empty(org?.Name),
            string.Join(",", org?.Units ?? []),
            card.Titles?.FirstOrDefault()?.Value,
            card.Notes?.FirstOrDefault()?.Value?.ReplaceLineEndings("\n"),
            card.BirthDayViews?.FirstOrDefault()?.Value?.DateOnly?.ToString("O", CultureInfo.InvariantCulture),
            string.Join(",", card.Categories?.Where(category => category is not null).SelectMany(category => category!.Value) ?? []),
            string.Join(",", card.NickNames?.Where(nickname => nickname is not null).SelectMany(nickname => nickname!.Value) ?? []),
            string.Join(";", card.Addresses?.Where(address => address is not null).Select(address => Join(address!.Value.Street) + "/" + Join(address.Value.Locality) + "/" + Join(address.Value.PostalCode) + "/" + Join(address.Value.Country)) ?? []));
    }
}
