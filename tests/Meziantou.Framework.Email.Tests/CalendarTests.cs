using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Email.Tests;

/// <summary>Tests of vCard and iCalendar: the files of TestData/Calendar, the recurrence rules, and the conversions to and from the items of Outlook.</summary>
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

    // The rules of python-dateutil whose occurrences differ. Remove a rule from the list when its occurrences are the ones dateutil expects.
    private static readonly Dictionary<string, string> DateutilDifferences = new(StringComparer.Ordinal)
    {
        // [RFC 5545] 3.3.10: BYMONTHDAY must not be used with FREQ=WEEKLY. dateutil gives the days of the month, the reader limits the weekday of the start to them
        ["testWeeklyByMonthDay"] = "BYMONTHDAY with FREQ=WEEKLY",
        ["testWeeklyByMonthAndMonthDay"] = "BYMONTHDAY with FREQ=WEEKLY",
    };

    public static TheoryData<string> DateutilRules => [.. File.ReadLines(GetPath("dateutil-rrule.tsv")).Select(line => line[..line.IndexOf('\t', StringComparison.Ordinal)])];

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
        var contact = Assert.IsType<MapiContact>(MsgFile.Load(MsgFileTests.GetPath("poi-msgClassContact.msg")));
        var card = VCard.FromMapi(contact);
        Assert.Equal(contact.DisplayName, card.FormattedName);
        Assert.Equal(contact.EmailAddresses, card.Emails.Select(email => email.Value));

        // The card written as an .msg file reads back with the same values
        var copy = Assert.IsType<MapiContact>(MsgFile.Load(MsgFile.ToByteArray(card.ToMapiContactBuilder())));
        Assert.Equal(Describe(card), Describe(VCard.FromMapi(copy)));
    }

    [Fact]
    public void VEvent_GetOccurrences_RecurrenceDateBeforeStart()
    {
        // RDATE adds dates to the recurrence set, also before the start
        var text = "BEGIN:VEVENT\r\nUID:test\r\nDTSTAMP:20260101T000000Z\r\nDTSTART:20260110T090000\r\nRRULE:FREQ=DAILY;COUNT=2\r\nRDATE:20260105T090000,20260110T090000,20260120T090000\r\nEND:VEVENT\r\n";
        var calendar = VCalendar.Parse("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:test\r\n" + text + "END:VCALENDAR\r\n").Single();
        var actual = calendar.Events.Single().GetOccurrences().ToList();
        Assert.Equal([new DateTime(2026, 1, 5, 9, 0, 0), new DateTime(2026, 1, 10, 9, 0, 0), new DateTime(2026, 1, 11, 9, 0, 0), new DateTime(2026, 1, 20, 9, 0, 0)], actual);
    }

    [Theory]
    [MemberData(nameof(Rules))]
    public void RecurrenceRule_RoundTrip(string rule)
    {
        foreach (var start in new[] { "20260115T090000", "20260131T170000", "20280229T080000", "20251231T235900" })
        {
            var text = $"BEGIN:VEVENT\r\nUID:test\r\nDTSTAMP:20260101T000000Z\r\nDTSTART:{start}\r\nRRULE:{rule}\r\nEND:VEVENT\r\n";
            var calendar = VCalendar.Parse("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:test\r\n" + text + "END:VCALENDAR\r\n").Single();
            var ev = calendar.Events.Single();
            var actual = ev.GetOccurrences().Take(60).ToList();

            // [RFC 5545] 3.8.5.3: the start is the first occurrence, even when the rule does not give it
            Assert.Equal(ev.Start!.Value.Value, actual[0]);
            Assert.Equal(actual.Order(), actual);
            Assert.Equal(actual.Distinct(), actual);

            var written = RecurrenceRule.Parse(rule)!.ToString();
            Assert.Equal(written, RecurrenceRule.Parse(written)!.ToString());
            Assert.Equal(actual, RecurrenceRule.Parse(written)!.GetOccurrences(ev.Start.Value.Value).Take(60));
        }
    }

    [Theory]
    [MemberData(nameof(DateutilRules))]
    public void RecurrenceRule_MatchesDateutil(string name)
    {
        // The rules of the tests of python-dateutil, with the occurrences they expect
        var fields = File.ReadLines(GetPath("dateutil-rrule.tsv")).Select(line => line.Split('\t')).Single(fields => fields[0] == name);
        var start = ParseDateTime(fields[1]);
        var expected = fields[3] is "-" ? [] : fields[3].Split(',').Select(ParseDateTime).ToList();
        var rule = RecurrenceRule.Parse(fields[2])!;
        var actual = rule.GetOccurrences(start).ToList();
        if (expected.FirstOrDefault() != start)
        {
            // [RFC 5545] 3.8.5.3: the start is the first occurrence, and counts in COUNT, even when the rule does not give it; dateutil does not count it
            Assert.Equal(start, actual[0]);
            actual.RemoveAt(0);
            if (rule.Count is { } count)
            {
                expected = [.. expected.Take(count - 1)];
            }
        }

        if (DateutilDifferences.TryGetValue(name, out var difference))
        {
            Assert.NotEqual(expected, actual, "The occurrences are the ones dateutil expects: remove the rule from the differences (" + difference + ")");
        }
        else
        {
            Assert.Equal(expected, actual);
        }

        static DateTime ParseDateTime(string value) => DateTime.ParseExact(value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
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
        // A recurring appointment with deleted and modified occurrences, written as an iCalendar object, then back as an appointment
        using var file = PstFile.Open(Path.Combine(AppContext.BaseDirectory, "TestData", "Pst", "java-libpst-dist-list.pst"));
        var appointment = FindItems<MapiAppointment>(file.RootFolder).First(item => item.IsRecurring);
        var pattern = appointment.GetRecurrence()!;
        var calendar = new VCalendar();
        var master = calendar.AddAppointment(appointment);
        var text = calendar.ToString();
        var read = VCalendar.Parse(text).Single();
        Assert.HasCount(calendar.Events.Count(), read.Events);
        Assert.Equal(master.GetOccurrences().Take(20), read.Events.Single(ev => ev.RecurrenceId is null).GetOccurrences().Take(20));

        var builder = master.ToMapiAppointmentBuilder();
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
    public void Meeting_Request()
    {
        #region MapiMeetingMessageBuilder_Usage
        var invitation = VCalendar.Parse("""
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:-//Example//Example//EN
            METHOD:REQUEST
            BEGIN:VEVENT
            UID:meeting-42@example.com
            SEQUENCE:1
            DTSTAMP:20260301T080000Z
            DTSTART:20260302T090000Z
            DTEND:20260302T100000Z
            SUMMARY:Project review
            LOCATION:Room 1
            ORGANIZER;CN=Alice:mailto:alice@example.com
            ATTENDEE;CN=Bob;ROLE=REQ-PARTICIPANT:mailto:bob@example.com
            END:VEVENT
            END:VCALENDAR
            """)[0];

        // The invitation as a meeting request of Outlook
        var request = Assert.IsType<MapiMeetingMessage>(MsgFile.Load(MsgFile.ToByteArray(invitation.ToMapiMeetingMessageBuilder())));
        Assert.Equal(MapiMeetingMessageType.Request, request.MeetingMessageType);
        Assert.Equal("IPM.Schedule.Meeting.Request", request.MessageClass);
        Assert.Equal("meeting-42@example.com", request.Uid);
        Assert.Equal("alice@example.com", request.SenderEmailAddress);
        Assert.Equal("bob@example.com", request.Recipients.Single().EmailAddress);

        // And back as an invitation
        var calendar = new VCalendar();
        var meeting = calendar.AddMeetingMessage(request);
        Assert.Equal("REQUEST", calendar.Method);
        Assert.Equal("meeting-42@example.com", meeting.Uid);
        Assert.Equal(1, meeting.Sequence);
        #endregion

        Assert.Equal(new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc), request.Start);
        Assert.Equal("Room 1", request.Location);
        Assert.Equal(1, request.Sequence);
        Assert.True(request.IsMeeting);

        // The invitation written and read back
        var read = VCalendar.Parse(calendar.ToString()).Single();
        Assert.Equal("REQUEST", read.Method);
        var ev = Assert.Single(read.Events);
        Assert.Equal("meeting-42@example.com", ev.Uid);
        Assert.Equal(1, ev.Sequence);
        Assert.Equal("CONFIRMED", ev.Status);
        Assert.Equal("alice@example.com", ev.Organizer!.Address);
        Assert.Equal("bob@example.com", Assert.Single(ev.Attendees).Address);
    }

    [Theory]
    [InlineData("ACCEPTED", MapiMeetingMessageType.Accepted, "IPM.Schedule.Meeting.Resp.Pos")]
    [InlineData("TENTATIVE", MapiMeetingMessageType.Tentative, "IPM.Schedule.Meeting.Resp.Tent")]
    [InlineData("DECLINED", MapiMeetingMessageType.Declined, "IPM.Schedule.Meeting.Resp.Neg")]
    public void Meeting_Reply(string status, MapiMeetingMessageType expected, string messageClass)
    {
        // RFC 5546 3.2.3: the attendee sends the reply to the organizer
        var reply = VCalendar.Parse($"""
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:-//Example//Example//EN
            METHOD:REPLY
            BEGIN:VEVENT
            UID:meeting-42@example.com
            DTSTAMP:20260301T090000Z
            DTSTART:20260302T090000Z
            DTEND:20260302T100000Z
            ORGANIZER;CN=Alice:mailto:alice@example.com
            ATTENDEE;CN=Bob;PARTSTAT={status}:mailto:bob@example.com
            END:VEVENT
            END:VCALENDAR
            """)[0];
        var message = Assert.IsType<MapiMeetingMessage>(MsgFile.Load(MsgFile.ToByteArray(reply.ToMapiMeetingMessageBuilder())));
        Assert.Equal(expected, message.MeetingMessageType);
        Assert.Equal(messageClass, message.MessageClass);
        Assert.Equal("bob@example.com", message.SenderEmailAddress);
        Assert.Equal("alice@example.com", message.Recipients.Single().EmailAddress);

        var calendar = new VCalendar();
        calendar.AddMeetingMessage(message);
        var read = VCalendar.Parse(calendar.ToString()).Single();
        Assert.Equal("REPLY", read.Method);
        var ev = Assert.Single(read.Events);
        Assert.Equal("alice@example.com", ev.Organizer!.Address);
        var attendee = Assert.Single(ev.Attendees);
        Assert.Equal("bob@example.com", attendee.Address);
        Assert.Equal(status, attendee.ParticipationStatus);
    }

    [Fact]
    public void Meeting_Cancellation()
    {
        var cancel = VCalendar.Parse("""
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:-//Example//Example//EN
            METHOD:CANCEL
            BEGIN:VEVENT
            UID:meeting-42@example.com
            SEQUENCE:2
            DTSTAMP:20260301T100000Z
            DTSTART:20260302T090000Z
            DTEND:20260302T100000Z
            STATUS:CANCELLED
            ORGANIZER:mailto:alice@example.com
            ATTENDEE:mailto:bob@example.com
            END:VEVENT
            END:VCALENDAR
            """)[0];
        var message = Assert.IsType<MapiMeetingMessage>(MsgFile.Load(MsgFile.ToByteArray(cancel.ToMapiMeetingMessageBuilder())));
        Assert.Equal(MapiMeetingMessageType.Cancellation, message.MeetingMessageType);
        Assert.Equal("IPM.Schedule.Meeting.Canceled", message.MessageClass);
        Assert.Equal(MapiBusyStatus.Free, message.BusyStatus);
        Assert.Equal(2, message.Sequence);

        var calendar = new VCalendar();
        var ev = calendar.AddMeetingMessage(message);
        Assert.Equal("CANCEL", calendar.Method);
        Assert.Equal("CANCELLED", ev.Status);
        Assert.Equal("CANCELLED", Assert.Single(VCalendar.Parse(calendar.ToString()).Single().Events).Status);
    }

    [Theory]
    [InlineData("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:x\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n")]
    [InlineData("BEGIN:VCALENDAR\r\nMETHOD:PUBLISH\r\nBEGIN:VEVENT\r\nUID:x\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n")]
    [InlineData("BEGIN:VCALENDAR\r\nMETHOD:REQUEST\r\nEND:VCALENDAR\r\n")]
    [InlineData("BEGIN:VCALENDAR\r\nMETHOD:REPLY\r\nBEGIN:VEVENT\r\nUID:x\r\nATTENDEE;PARTSTAT=NEEDS-ACTION:mailto:a@b.c\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n")]
    public void Meeting_NotAMeetingMessage(string text)
    {
        Assert.Throws<InvalidOperationException>(() => VCalendar.Parse(text)[0].ToMapiMeetingMessageBuilder());
    }

    [Fact]
    public void Meeting_GlobalObjectIdentifier()
    {
        // [MS-OXCICAL] 2.1.3.1.1.20.26: the UID of Outlook is the hexadecimal identifier, without its instance date
        const string Header = "040000008200E00074C5B7101A82E008";
        var uid = Header + "07EA0302" + "C0E2F0E9B9D3D801" + "0000000000000000" + "10000000" + "C4A5E4E6E6AE4F4BA0E9D1A4B2A3C1F0";
        var builder = new MapiAppointmentBuilder { Subject = "x", Uid = uid };
        var item = Assert.IsType<MapiAppointment>(MsgFile.Load(MsgFile.ToByteArray(builder)));
        Assert.Equal(Header + "00000000" + uid[40..], item.Uid);

        // Another UID is kept as it is, after "vCal-Uid"
        builder.Uid = "040000008200E00074C5B7101A82E008-not-hex@example.com";
        Assert.Equal("040000008200E00074C5B7101A82E008-not-hex@example.com", Assert.IsType<MapiAppointment>(MsgFile.Load(MsgFile.ToByteArray(builder))).Uid);
        var id = (byte[])builder.Properties.Get(new MapiNamedProperty(MapiPropertySets.Meeting, 0x0003))!;
        Assert.Equal("vCal-Uid", Encoding.ASCII.GetString(id, 40, 8));

        // An event keeps its UID through an appointment
        var appointment = Assert.IsType<MapiAppointment>(MsgFile.Load(MsgFile.ToByteArray(VCalendar.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:event-1@example.com\r\nDTSTART:20260302T090000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n")[0].Events.Single().ToMapiAppointmentBuilder())));
        Assert.Equal("event-1@example.com", new VCalendar().AddAppointment(appointment).Uid);
    }

    [Fact]
    public void Meeting_OutlookAppointment()
    {
        // The appointment of Outlook of the test files: its UID is the identifier Outlook created
        var appointment = Assert.IsType<MapiAppointment>(MsgFile.Load(MsgFileTests.GetPath("poi-msgClassAppointment.msg")));
        var id = appointment.Properties.GetValueOrDefault<byte[]>(new MapiNamedProperty(MapiPropertySets.Meeting, 0x0023));
        Assert.NotNull(id);
        Assert.Equal(Convert.ToHexString(id), appointment.Uid);
        Assert.StartsWith("040000008200E00074C5B7101A82E008", appointment.Uid, StringComparison.Ordinal);
        Assert.Equal(appointment.Uid, new VCalendar().AddAppointment(appointment).Uid);
    }

    [Fact]
    public void Meeting_EmailMessage()
    {
        // A MIME invitation (iMIP, RFC 6047): the text/calendar alternative becomes a meeting request
        var invitation = new EmailMessage
        {
            Subject = "Project review",
            From = new MailboxAddress("Alice", "alice@example.com"),
            TextBody = "Let's review the project.",
            CalendarBody = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Example//Example//EN\r\nMETHOD:REQUEST\r\nBEGIN:VEVENT\r\nUID:meeting-42@example.com\r\nDTSTAMP:20260301T080000Z\r\nDTSTART:20260302T090000Z\r\nDTEND:20260302T100000Z\r\nSUMMARY:Project review\r\nORGANIZER:mailto:alice@example.com\r\nATTENDEE:mailto:bob@example.com\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n",
        };
        invitation.To.Add(new MailboxAddress("Bob", "bob@example.com"));
        var mime = invitation.ToMime().ToByteArray();

        // The calendar alternative has its method
        Assert.Contains("method=REQUEST", Encoding.UTF8.GetString(mime), StringComparison.Ordinal);

        var read = EmailMessage.FromMime(MimeMessage.Load(mime));
        Assert.NotNull(read.CalendarBody);
        Assert.Empty(read.Attachments);
        var request = Assert.IsType<MapiMeetingMessage>(MsgFile.Load(MsgFile.ToByteArray(read.ToMapiBuilder())));
        Assert.Equal(MapiMeetingMessageType.Request, request.MeetingMessageType);
        Assert.Equal("meeting-42@example.com", request.Uid);
        Assert.Equal("Project review", request.Subject);
        Assert.Equal("Let's review the project.", request.Body);
        Assert.Equal("Bob", request.Recipients.Single().DisplayName);

        // A meeting request of Outlook converts to a MIME invitation
        var back = EmailMessage.FromMapi(request);
        Assert.Contains("METHOD:REQUEST", back.CalendarBody, StringComparison.Ordinal);
        Assert.Contains("UID:meeting-42@example.com", back.CalendarBody, StringComparison.Ordinal);
        Assert.Contains("method=REQUEST", Encoding.UTF8.GetString(back.ToMime().ToByteArray()), StringComparison.Ordinal);
    }

    [Fact]
    public void ICalendar_PstTask()
    {
        var task = Assert.IsType<MapiTask>(MsgFile.Load(MsgFileTests.GetPath("poi-msgClassTask.msg")));
        var calendar = new VCalendar();
        var todo = calendar.AddTask(task);
        Assert.Equal(task.Subject, todo.Summary);
        var copy = Assert.IsType<MapiTask>(MsgFile.Load(MsgFile.ToByteArray(todo.ToMapiTaskBuilder())));
        Assert.Equal(task.Subject, copy.Subject);
        Assert.Equal(task.Status == MapiTaskStatus.Complete, copy.Status == MapiTaskStatus.Complete);
        Assert.Equal(task.PercentComplete, copy.PercentComplete, 2);
        Assert.Equal(task.Subject, VCalendar.Parse(calendar.ToString()).Single().Todos.Single().Summary);
    }

    [Fact]
    public void ICalendar_Invalid()
    {
        Assert.Empty(VCalendar.Parse("not a calendar"));
        var deep = string.Concat(Enumerable.Repeat("BEGIN:X\r\n", 40));
        Assert.Throws<InvalidEmailDataException>(() => VCalendar.Parse("BEGIN:VCALENDAR\r\n" + deep));
        var unclosed = VCalendar.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Unclosed\r\n").Single();
        Assert.Equal("Unclosed", unclosed.Events.Single().Summary);
    }

    [Theory]
    [InlineData("X-A;X-B=QUOTED-PRINTABLE:a=", "a=")]
    [InlineData("X-A;X-QUOTED-PRINTABLE:a=", "a=")]
    [InlineData("X-A;ENCODING=QUOTED-PRINTABLE;ENCODING=8BIT:a=", "a=")]
    [InlineData("X-A;ENCODING=QUOTED-PRINTABLE;QUOTED-PRINTABLE:a=3D3D", "a=3D")]
    public void ICalendar_QuotedPrintable(string line, string expected)
    {
        // Only an encoding in quoted-printable makes "=" at the end of a line a soft line break, and its value is decoded once
        var calendar = VCalendar.Parse("BEGIN:VCALENDAR\r\n" + line + "\r\nEND:VCALENDAR\r\n").Single();
        Assert.Equal(expected, calendar.GetProperty("X-A")?.Value);
        var written = calendar.ToString();
        var copy = VCalendar.Parse(written).Single();
        Assert.Equal(expected, copy.GetProperty("X-A")?.Value);
        Assert.Equal(written, copy.ToString());
    }

    private static IEnumerable<T> FindItems<T>(PstFolder folder)
        where T : MapiMessage
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
}
